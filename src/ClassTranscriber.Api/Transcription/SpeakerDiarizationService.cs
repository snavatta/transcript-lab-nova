using ClassTranscriber.Api.Contracts;

namespace ClassTranscriber.Api.Transcription;

public interface ISpeakerDiarizer
{
    TranscriptSegmentDto[] AssignSpeakers(string audioPath, IReadOnlyList<TranscriptSegmentDto> segments, string mode = "Basic", CancellationToken ct = default);
}

public sealed class BasicSpeakerDiarizer : ISpeakerDiarizer
{
    private readonly ILogger<BasicSpeakerDiarizer> _logger;

    public BasicSpeakerDiarizer(ILogger<BasicSpeakerDiarizer> logger)
    {
        _logger = logger;
    }

    public TranscriptSegmentDto[] AssignSpeakers(string audioPath, IReadOnlyList<TranscriptSegmentDto> segments, string mode = "Basic", CancellationToken ct = default)
    {
        if (segments.Count == 0)
            return [];

        var config = DiarizationConfig.FromMode(mode);
        var clonedSegments = segments.Select(segment => segment with { Speaker = null }).ToArray();
        if (clonedSegments.Length == 1)
            return LabelAllSegments(clonedSegments, "Speaker 1");

        var wave = PreparedAudioWaveFile.Read(audioPath);
        var turns = BuildTurns(clonedSegments, wave.DurationMs, config);
        if (turns.Count == 0)
            return LabelAllSegments(clonedSegments, "Speaker 1");

        var features = new TurnFeatures[turns.Count];
        for (var i = 0; i < turns.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            features[i] = ExtractFeatures(wave, turns[i], config);
        }

        var labels = AssignClusterLabels(features, turns, config);
        var smoothedLabels = SmoothAssignments(labels, turns);
        var speakerNames = BuildSpeakerNames(smoothedLabels);

        for (var turnIndex = 0; turnIndex < turns.Count; turnIndex++)
        {
            var speaker = speakerNames[smoothedLabels[turnIndex]];
            var turn = turns[turnIndex];
            for (var segmentIndex = turn.StartSegmentIndex; segmentIndex <= turn.EndSegmentIndex; segmentIndex++)
                clonedSegments[segmentIndex] = clonedSegments[segmentIndex] with { Speaker = speaker };
        }

        _logger.LogInformation(
            "Assigned diarization labels for {SegmentCount} transcript segments across {TurnCount} turns and {SpeakerCount} speakers",
            clonedSegments.Length,
            turns.Count,
            speakerNames.Count);

        return clonedSegments;
    }

    private static TranscriptSegmentDto[] LabelAllSegments(TranscriptSegmentDto[] segments, string speaker)
        => segments.Select(segment => segment with { Speaker = speaker }).ToArray();

    private static IReadOnlyList<DiarizationTurn> BuildTurns(
        IReadOnlyList<TranscriptSegmentDto> segments,
        long durationMs,
        DiarizationConfig config)
    {
        var turns = new List<DiarizationTurn>();
        var currentStartSegment = -1;
        var currentStartMs = 0L;
        var currentEndMs = 0L;

        for (var segmentIndex = 0; segmentIndex < segments.Count; segmentIndex++)
        {
            var segment = segments[segmentIndex];
            if (string.IsNullOrWhiteSpace(segment.Text))
                continue;

            var normalizedStart = Math.Max(0L, segment.StartMs);
            var normalizedEnd = Math.Min(durationMs, Math.Max(segment.EndMs, normalizedStart + 1));

            if (currentStartSegment < 0)
            {
                currentStartSegment = segmentIndex;
                currentStartMs = normalizedStart;
                currentEndMs = normalizedEnd;
                continue;
            }

            var gapMs = Math.Max(0L, normalizedStart - currentEndMs);
            var candidateDurationMs = normalizedEnd - currentStartMs;
            var currentTurnDurationMs = currentEndMs - currentStartMs;
            var currentSegmentDurationMs = normalizedEnd - normalizedStart;
            var shouldMergeIntoCurrentTurn = gapMs <= config.MergeGapMs
                && candidateDurationMs <= config.MaxTurnDurationMs
                && (currentTurnDurationMs <= config.ExpandingTurnDurationMs || currentSegmentDurationMs <= config.WordLikeSegmentDurationMs);

            if (!shouldMergeIntoCurrentTurn)
            {
                turns.Add(new DiarizationTurn(currentStartSegment, segmentIndex - 1, currentStartMs, currentEndMs));
                currentStartSegment = segmentIndex;
                currentStartMs = normalizedStart;
                currentEndMs = normalizedEnd;
                continue;
            }

            currentEndMs = Math.Max(currentEndMs, normalizedEnd);
        }

        if (currentStartSegment >= 0)
            turns.Add(new DiarizationTurn(currentStartSegment, segments.Count - 1, currentStartMs, currentEndMs));

        return turns;
    }

    private static TurnFeatures ExtractFeatures(PreparedAudioWaveFile wave, DiarizationTurn turn, DiarizationConfig config)
    {
        var sampleRate = wave.SampleRate;
        var startSample = MillisecondsToSampleIndex(Math.Max(0L, turn.StartMs - config.PaddingMs), sampleRate, wave.Samples.Length);
        var endSample = MillisecondsToSampleIndex(Math.Min(wave.DurationMs, turn.EndMs + config.PaddingMs), sampleRate, wave.Samples.Length);
        if (endSample <= startSample)
            endSample = Math.Min(wave.Samples.Length, startSample + Math.Max(1, sampleRate / 2));

        var slice = wave.Samples[startSample..endSample];
        if (slice.Length == 0)
            return TurnFeatures.Silent(turn.DurationMs, config.GoertzelFrequenciesHz.Length);

        var frameSize = Math.Max(64, (int)Math.Round(sampleRate * (config.FrameSizeMs / 1000d)));
        var frameHop = Math.Max(32, (int)Math.Round(sampleRate * (config.FrameHopMs / 1000d)));
        var frames = new List<FrameFeatures>();

        for (var offset = 0; offset < slice.Length; offset += frameHop)
        {
            var length = Math.Min(frameSize, slice.Length - offset);
            if (length <= 0)
                break;

            frames.Add(ExtractFrameFeatures(slice, offset, length, sampleRate, config));
            if (offset + length >= slice.Length)
                break;
        }

        if (frames.Count == 0)
            frames.Add(ExtractFrameFeatures(slice, 0, slice.Length, sampleRate, config));

        var meanRms = frames.Average(frame => frame.Rms);
        var silenceThreshold = Math.Max(0.008, meanRms * 0.45);
        var activeFrames = frames.Where(frame => frame.Rms >= silenceThreshold).ToArray();
        if (activeFrames.Length == 0)
            activeFrames = [frames.OrderByDescending(frame => frame.Rms).First()];

        var voicedFrames = activeFrames.Where(frame => frame.PitchHz > 0).ToArray();
        var pitchMean = voicedFrames.Length > 0 ? voicedFrames.Average(frame => frame.PitchHz) : 0d;
        var pitchStdDev = voicedFrames.Length > 1
            ? Math.Sqrt(voicedFrames.Average(frame => Math.Pow(frame.PitchHz - pitchMean, 2)))
            : 0d;
        var totalBandEnergy = activeFrames.Sum(frame => frame.BandEnergies.Sum());
        if (totalBandEnergy <= 0)
            totalBandEnergy = 1;

        var normalizedBandEnergies = new double[config.GoertzelFrequenciesHz.Length];
        for (var bandIndex = 0; bandIndex < normalizedBandEnergies.Length; bandIndex++)
            normalizedBandEnergies[bandIndex] = activeFrames.Sum(frame => frame.BandEnergies[bandIndex]) / totalBandEnergy;

        return new TurnFeatures(
            turn.DurationMs,
            [
                Math.Log10(activeFrames.Average(frame => frame.Rms) + 1e-6),
                activeFrames.Average(frame => frame.ZeroCrossingRate),
                pitchMean / config.MaxPitchHz,
                pitchStdDev / config.MaxPitchHz,
                voicedFrames.Length / (double)activeFrames.Length,
                .. normalizedBandEnergies,
            ]);
    }

    private static FrameFeatures ExtractFrameFeatures(float[] samples, int offset, int length, int sampleRate, DiarizationConfig config)
    {
        double energy = 0;
        var zeroCrossings = 0;
        var previous = samples[offset];
        double mean = 0;
        for (var i = 0; i < length; i++)
            mean += samples[offset + i];
        mean /= length;

        for (var i = 0; i < length; i++)
        {
            var centered = samples[offset + i] - mean;
            energy += centered * centered;
            if (i > 0)
            {
                var current = samples[offset + i];
                if ((current >= 0 && previous < 0) || (current < 0 && previous >= 0))
                    zeroCrossings++;
                previous = current;
            }
        }

        var rms = Math.Sqrt(energy / Math.Max(1, length));
        var bandEnergies = new double[config.GoertzelFrequenciesHz.Length];
        for (var bandIndex = 0; bandIndex < config.GoertzelFrequenciesHz.Length; bandIndex++)
            bandEnergies[bandIndex] = ComputeGoertzelPower(samples, offset, length, sampleRate, config.GoertzelFrequenciesHz[bandIndex]);

        var pitchHz = rms >= 0.01
            ? EstimatePitchHz(samples, offset, length, sampleRate, mean, config)
            : 0d;

        return new FrameFeatures(
            rms,
            zeroCrossings / (double)Math.Max(1, length - 1),
            pitchHz,
            bandEnergies);
    }

    private static double EstimatePitchHz(float[] samples, int offset, int length, int sampleRate, double mean, DiarizationConfig config)
    {
        var minLag = Math.Max(1, sampleRate / config.MaxPitchHz);
        var maxLag = Math.Min(length - 2, sampleRate / config.MinPitchHz);
        if (maxLag <= minLag)
            return 0;

        double bestCorrelation = 0;
        var bestLag = 0;

        for (var lag = minLag; lag <= maxLag; lag++)
        {
            double numerator = 0;
            double leftEnergy = 0;
            double rightEnergy = 0;

            for (var i = 0; i < length - lag; i++)
            {
                var left = samples[offset + i] - mean;
                var right = samples[offset + i + lag] - mean;
                numerator += left * right;
                leftEnergy += left * left;
                rightEnergy += right * right;
            }

            var denominator = Math.Sqrt(leftEnergy * rightEnergy);
            if (denominator <= 1e-9)
                continue;

            var correlation = numerator / denominator;
            if (correlation > bestCorrelation)
            {
                bestCorrelation = correlation;
                bestLag = lag;
            }
        }

        if (bestLag == 0 || bestCorrelation < config.PitchCorrelationThreshold)
            return 0;

        return sampleRate / (double)bestLag;
    }

    private static double ComputeGoertzelPower(float[] samples, int offset, int length, int sampleRate, double targetFrequency)
    {
        var omega = 2d * Math.PI * targetFrequency / sampleRate;
        var coefficient = 2d * Math.Cos(omega);
        double q0 = 0;
        double q1 = 0;
        double q2 = 0;

        for (var i = 0; i < length; i++)
        {
            q0 = coefficient * q1 - q2 + samples[offset + i];
            q2 = q1;
            q1 = q0;
        }

        return Math.Max(0, q1 * q1 + q2 * q2 - coefficient * q1 * q2);
    }

    private static int[] AssignClusterLabels(IReadOnlyList<TurnFeatures> features, IReadOnlyList<DiarizationTurn> turns, DiarizationConfig config)
    {
        if (features.Count == 0)
            return [];

        var normalized = NormalizeFeatures(features.Select(feature => feature.Values).ToArray());
        if (normalized.Length <= 1)
            return Enumerable.Repeat(0, normalized.Length).ToArray();

        var sampleWeights = turns.Select(turn => Math.Max(1d, turn.DurationMs / 1000d)).ToArray();
        var baseModel = RunKMeans(normalized, sampleWeights, 1);
        var bestModel = baseModel;
        var bestScore = double.NegativeInfinity;

        var maxSpeakers = Math.Min(config.MaxSpeakerCount, normalized.Length);
        for (var speakerCount = 2; speakerCount <= maxSpeakers; speakerCount++)
        {
            var model = RunKMeans(normalized, sampleWeights, speakerCount);
            if (model.ClusterCount <= 1)
                continue;

            var improvement = baseModel.WithinClusterSumOfSquares <= 0
                ? 0
                : 1d - (model.WithinClusterSumOfSquares / baseModel.WithinClusterSumOfSquares);
            if (improvement < config.MinClusterImprovementRatio)
                continue;

            var centroidDistance = GetMinimumCentroidDistance(model.Centroids);
            if (centroidDistance < config.MinCentroidDistance)
                continue;

            var score = ComputeCalinskiHarabaszScore(normalized, sampleWeights, model);
            if (score > bestScore)
            {
                bestScore = score;
                bestModel = model;
            }
        }

        if (bestModel.ClusterCount <= 1)
        {
            var pitchFallback = TryAssignByPitch(features, config);
            if (pitchFallback is not null)
                return pitchFallback;

            var zeroCrossingFallback = TryAssignByFeatureGap(features, 1, 0.008, minimumValue: 0.005);
            if (zeroCrossingFallback is not null)
                return zeroCrossingFallback;
        }

        return bestModel.Assignments;
    }

    private static int[]? TryAssignByPitch(IReadOnlyList<TurnFeatures> features, DiarizationConfig config)
    {
        var voicedTurns = features
            .Select((feature, index) => new { index, pitch = feature.Values[2] })
            .Where(item => item.pitch > 0.05)
            .OrderBy(item => item.pitch)
            .ToArray();

        if (voicedTurns.Length < 2)
            return null;

        var largestGap = 0d;
        var gapIndex = -1;
        for (var index = 0; index < voicedTurns.Length - 1; index++)
        {
            var gap = voicedTurns[index + 1].pitch - voicedTurns[index].pitch;
            if (gap > largestGap)
            {
                largestGap = gap;
                gapIndex = index;
            }
        }

        if (gapIndex < 0 || largestGap < config.StrongPitchSplitThreshold)
            return null;

        var threshold = (voicedTurns[gapIndex].pitch + voicedTurns[gapIndex + 1].pitch) / 2d;
        var assignments = features
            .Select(feature => feature.Values[2] > threshold ? 1 : 0)
            .ToArray();

        return assignments.Distinct().Count() > 1 ? assignments : null;
    }

    private static int[]? TryAssignByFeatureGap(
        IReadOnlyList<TurnFeatures> features,
        int featureIndex,
        double minimumGap,
        double minimumValue)
    {
        var ordered = features
            .Select((feature, index) => new { index, value = feature.Values[featureIndex] })
            .Where(item => item.value > minimumValue)
            .OrderBy(item => item.value)
            .ToArray();

        if (ordered.Length < 2)
            return null;

        var largestGap = 0d;
        var gapIndex = -1;
        for (var index = 0; index < ordered.Length - 1; index++)
        {
            var gap = ordered[index + 1].value - ordered[index].value;
            if (gap > largestGap)
            {
                largestGap = gap;
                gapIndex = index;
            }
        }

        if (gapIndex < 0 || largestGap < minimumGap)
            return null;

        var threshold = (ordered[gapIndex].value + ordered[gapIndex + 1].value) / 2d;
        var assignments = features
            .Select(feature => feature.Values[featureIndex] > threshold ? 1 : 0)
            .ToArray();

        return assignments.Distinct().Count() > 1 ? assignments : null;
    }

    private static double[][] NormalizeFeatures(double[][] values)
    {
        var dimension = values[0].Length;
        var means = new double[dimension];
        var standardDeviations = new double[dimension];

        for (var featureIndex = 0; featureIndex < dimension; featureIndex++)
        {
            means[featureIndex] = values.Average(vector => vector[featureIndex]);
            standardDeviations[featureIndex] = Math.Sqrt(values.Average(vector => Math.Pow(vector[featureIndex] - means[featureIndex], 2)));
            if (standardDeviations[featureIndex] <= 1e-9)
                standardDeviations[featureIndex] = 1;
        }

        return values
            .Select(vector =>
            {
                var normalized = new double[dimension];
                for (var featureIndex = 0; featureIndex < dimension; featureIndex++)
                    normalized[featureIndex] = (vector[featureIndex] - means[featureIndex]) / standardDeviations[featureIndex];
                return normalized;
            })
            .ToArray();
    }

    private static KMeansResult RunKMeans(double[][] samples, double[] weights, int clusterCount)
    {
        var centroids = InitializeCentroids(samples, clusterCount);
        var assignments = new int[samples.Length];
        var changed = true;

        for (var iteration = 0; iteration < 50 && changed; iteration++)
        {
            changed = false;

            for (var sampleIndex = 0; sampleIndex < samples.Length; sampleIndex++)
            {
                var bestCluster = 0;
                var bestDistance = double.PositiveInfinity;
                for (var clusterIndex = 0; clusterIndex < centroids.Length; clusterIndex++)
                {
                    var distance = GetDistanceSquared(samples[sampleIndex], centroids[clusterIndex]);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestCluster = clusterIndex;
                    }
                }

                if (assignments[sampleIndex] != bestCluster)
                {
                    assignments[sampleIndex] = bestCluster;
                    changed = true;
                }
            }

            var nextCentroids = new double[clusterCount][];
            var clusterWeights = new double[clusterCount];
            for (var clusterIndex = 0; clusterIndex < clusterCount; clusterIndex++)
                nextCentroids[clusterIndex] = new double[samples[0].Length];

            for (var sampleIndex = 0; sampleIndex < samples.Length; sampleIndex++)
            {
                var assignment = assignments[sampleIndex];
                var weight = weights[sampleIndex];
                clusterWeights[assignment] += weight;
                for (var featureIndex = 0; featureIndex < samples[sampleIndex].Length; featureIndex++)
                    nextCentroids[assignment][featureIndex] += samples[sampleIndex][featureIndex] * weight;
            }

            for (var clusterIndex = 0; clusterIndex < clusterCount; clusterIndex++)
            {
                if (clusterWeights[clusterIndex] <= 0)
                {
                    nextCentroids[clusterIndex] = samples[clusterIndex % samples.Length].ToArray();
                    continue;
                }

                for (var featureIndex = 0; featureIndex < nextCentroids[clusterIndex].Length; featureIndex++)
                    nextCentroids[clusterIndex][featureIndex] /= clusterWeights[clusterIndex];
            }

            centroids = nextCentroids;
        }

        double withinClusterSumOfSquares = 0;
        var distinctAssignments = assignments.Distinct().Count();
        for (var sampleIndex = 0; sampleIndex < samples.Length; sampleIndex++)
            withinClusterSumOfSquares += weights[sampleIndex] * GetDistanceSquared(samples[sampleIndex], centroids[assignments[sampleIndex]]);

        return new KMeansResult(assignments, centroids, distinctAssignments, withinClusterSumOfSquares);
    }

    private static double[][] InitializeCentroids(double[][] samples, int clusterCount)
    {
        var centroids = new List<double[]> { samples[0].ToArray() };

        while (centroids.Count < clusterCount)
        {
            var farthestSample = samples
                .OrderByDescending(sample => centroids.Min(centroid => GetDistanceSquared(sample, centroid)))
                .First();
            centroids.Add(farthestSample.ToArray());
        }

        return centroids.ToArray();
    }

    private static double ComputeCalinskiHarabaszScore(double[][] samples, double[] weights, KMeansResult model)
    {
        if (model.ClusterCount <= 1 || samples.Length <= model.ClusterCount)
            return double.NegativeInfinity;

        var dimension = samples[0].Length;
        var globalMean = new double[dimension];
        var totalWeight = weights.Sum();
        for (var sampleIndex = 0; sampleIndex < samples.Length; sampleIndex++)
        {
            for (var featureIndex = 0; featureIndex < dimension; featureIndex++)
                globalMean[featureIndex] += samples[sampleIndex][featureIndex] * weights[sampleIndex];
        }

        for (var featureIndex = 0; featureIndex < dimension; featureIndex++)
            globalMean[featureIndex] /= Math.Max(1e-9, totalWeight);

        double betweenClusterDispersion = 0;
        for (var clusterIndex = 0; clusterIndex < model.Centroids.Length; clusterIndex++)
        {
            var clusterWeight = 0d;
            for (var sampleIndex = 0; sampleIndex < samples.Length; sampleIndex++)
            {
                if (model.Assignments[sampleIndex] == clusterIndex)
                    clusterWeight += weights[sampleIndex];
            }

            if (clusterWeight <= 0)
                continue;

            betweenClusterDispersion += clusterWeight * GetDistanceSquared(model.Centroids[clusterIndex], globalMean);
        }

        var numerator = betweenClusterDispersion / (model.ClusterCount - 1d);
        var denominator = model.WithinClusterSumOfSquares / (samples.Length - model.ClusterCount);
        return denominator <= 0 ? double.NegativeInfinity : numerator / denominator;
    }

    private static double GetMinimumCentroidDistance(double[][] centroids)
    {
        if (centroids.Length < 2)
            return 0;

        var minDistance = double.PositiveInfinity;
        for (var left = 0; left < centroids.Length; left++)
        {
            for (var right = left + 1; right < centroids.Length; right++)
                minDistance = Math.Min(minDistance, Math.Sqrt(GetDistanceSquared(centroids[left], centroids[right])));
        }

        return minDistance;
    }

    private static int[] SmoothAssignments(int[] assignments, IReadOnlyList<DiarizationTurn> turns)
    {
        if (assignments.Length < 3)
            return assignments;

        var smoothed = assignments.ToArray();
        for (var index = 1; index < smoothed.Length - 1; index++)
        {
            var previous = smoothed[index - 1];
            var current = smoothed[index];
            var next = smoothed[index + 1];
            if (previous == next && current != previous && turns[index].DurationMs <= 2_500)
                smoothed[index] = previous;
        }

        return smoothed;
    }

    private static Dictionary<int, string> BuildSpeakerNames(IReadOnlyList<int> assignments)
    {
        var map = new Dictionary<int, string>();
        foreach (var assignment in assignments)
        {
            if (!map.ContainsKey(assignment))
                map[assignment] = $"Speaker {map.Count + 1}";
        }

        return map;
    }

    private static int MillisecondsToSampleIndex(long milliseconds, int sampleRate, int maxLength)
        => Math.Clamp((int)Math.Round(milliseconds * sampleRate / 1000d), 0, maxLength);

    private static double GetDistanceSquared(double[] left, double[] right)
    {
        double sum = 0;
        for (var index = 0; index < left.Length; index++)
        {
            var delta = left[index] - right[index];
            sum += delta * delta;
        }

        return sum;
    }

    private sealed record PreparedAudioWaveFile(float[] Samples, int SampleRate, long DurationMs)
    {
        public static PreparedAudioWaveFile Read(string path)
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);

            if (new string(reader.ReadChars(4)) != "RIFF")
                throw new InvalidOperationException($"Unsupported WAV file at {path}: missing RIFF header.");

            _ = reader.ReadInt32();

            if (new string(reader.ReadChars(4)) != "WAVE")
                throw new InvalidOperationException($"Unsupported WAV file at {path}: missing WAVE header.");

            ushort formatTag = 0;
            ushort channels = 0;
            var sampleRate = 0;
            ushort bitsPerSample = 0;
            byte[]? dataChunk = null;

            while (stream.Position < stream.Length)
            {
                if (stream.Length - stream.Position < 8)
                    break;

                var chunkId = new string(reader.ReadChars(4));
                var chunkSize = reader.ReadInt32();
                if (chunkSize < 0)
                    throw new InvalidOperationException($"Invalid WAV chunk size in {path}.");

                switch (chunkId)
                {
                    case "fmt ":
                        formatTag = reader.ReadUInt16();
                        channels = reader.ReadUInt16();
                        sampleRate = reader.ReadInt32();
                        _ = reader.ReadInt32();
                        _ = reader.ReadUInt16();
                        bitsPerSample = reader.ReadUInt16();
                        if (chunkSize > 16)
                            reader.ReadBytes(chunkSize - 16);
                        break;
                    case "data":
                        dataChunk = reader.ReadBytes(chunkSize);
                        break;
                    default:
                        reader.ReadBytes(chunkSize);
                        break;
                }

                if ((chunkSize & 1) == 1 && stream.Position < stream.Length)
                    reader.ReadByte();
            }

            if (channels != 1 || sampleRate <= 0 || dataChunk is null)
                throw new InvalidOperationException($"Unsupported prepared audio file at {path}: expected mono WAV data.");

            float[] samples = (formatTag, bitsPerSample) switch
            {
                (1, 16) => ConvertInt16Pcm(dataChunk),
                (3, 32) => ConvertFloatPcm(dataChunk),
                _ => throw new InvalidOperationException($"Unsupported prepared WAV encoding in {path}: formatTag={formatTag}, bitsPerSample={bitsPerSample}."),
            };

            var durationMs = (long)Math.Round(samples.Length * 1000d / sampleRate);
            return new PreparedAudioWaveFile(samples, sampleRate, durationMs);
        }

        private static float[] ConvertInt16Pcm(byte[] buffer)
        {
            var sampleCount = buffer.Length / 2;
            var samples = new float[sampleCount];
            for (var index = 0; index < sampleCount; index++)
                samples[index] = BitConverter.ToInt16(buffer, index * 2) / 32768f;
            return samples;
        }

        private static float[] ConvertFloatPcm(byte[] buffer)
        {
            var sampleCount = buffer.Length / 4;
            var samples = new float[sampleCount];
            for (var index = 0; index < sampleCount; index++)
                samples[index] = BitConverter.ToSingle(buffer, index * 4);
            return samples;
        }
    }

    private sealed record DiarizationTurn(int StartSegmentIndex, int EndSegmentIndex, long StartMs, long EndMs)
    {
        public long DurationMs => Math.Max(1, EndMs - StartMs);
    }

    private sealed record FrameFeatures(double Rms, double ZeroCrossingRate, double PitchHz, double[] BandEnergies);

    private sealed record TurnFeatures(long DurationMs, double[] Values)
    {
        public static TurnFeatures Silent(long durationMs, int goertzelBandCount)
            => new(durationMs, new double[5 + goertzelBandCount]);
    }

    private sealed record KMeansResult(int[] Assignments, double[][] Centroids, int ClusterCount, double WithinClusterSumOfSquares);

    private sealed record DiarizationConfig(
        int MergeGapMs,
        int MaxTurnDurationMs,
        int WordLikeSegmentDurationMs,
        int ExpandingTurnDurationMs,
        int PaddingMs,
        int FrameSizeMs,
        int FrameHopMs,
        int MinPitchHz,
        int MaxPitchHz,
        int MaxSpeakerCount,
        double MinClusterImprovementRatio,
        double MinCentroidDistance,
        double StrongPitchSplitThreshold,
        double PitchCorrelationThreshold,
        double[] GoertzelFrequenciesHz)
    {
        public static DiarizationConfig Basic { get; } = new(
            MergeGapMs: 450,
            MaxTurnDurationMs: 12_000,
            WordLikeSegmentDurationMs: 800,
            ExpandingTurnDurationMs: 1_200,
            PaddingMs: 120,
            FrameSizeMs: 30,
            FrameHopMs: 15,
            MinPitchHz: 85,
            MaxPitchHz: 320,
            MaxSpeakerCount: 3,
            MinClusterImprovementRatio: 0.05,
            MinCentroidDistance: 0.8,
            StrongPitchSplitThreshold: 0.18,
            PitchCorrelationThreshold: 0.35,
            GoertzelFrequenciesHz: [150, 300, 600, 1200, 2400]);

        public static DiarizationConfig Improved { get; } = new(
            MergeGapMs: 350,
            MaxTurnDurationMs: 15_000,
            WordLikeSegmentDurationMs: 800,
            ExpandingTurnDurationMs: 1_200,
            PaddingMs: 150,
            FrameSizeMs: 25,
            FrameHopMs: 10,
            MinPitchHz: 80,
            MaxPitchHz: 350,
            MaxSpeakerCount: 6,
            MinClusterImprovementRatio: 0.03,
            MinCentroidDistance: 0.5,
            StrongPitchSplitThreshold: 0.12,
            PitchCorrelationThreshold: 0.25,
            GoertzelFrequenciesHz: [150, 225, 300, 450, 600, 900, 1200, 1800, 2400, 3600]);

        public static DiarizationConfig FromMode(string mode)
            => mode == "Improved" ? Improved : Basic;
    }
}
