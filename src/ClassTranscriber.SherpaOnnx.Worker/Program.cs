using System.Text.Json;
using SherpaOnnx;

var exitCode = await SherpaOnnxWorkerProgram.RunAsync();
return exitCode;

internal static class SherpaOnnxWorkerProgram
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<int> RunAsync()
    {
        try
        {
            await using var input = Console.OpenStandardInput();
            var request = await JsonSerializer.DeserializeAsync<SherpaOnnxWorkerRequest>(input, JsonOptions);
            if (request is null)
                throw new InvalidOperationException("No SherpaOnnx worker request was provided.");

            await Console.Error.WriteLineAsync(
                $"SherpaOnnx worker received request. backend={request.Backend}, provider={request.Provider}, audioPath={request.AudioPath}");
            var response = SherpaOnnxWorkerProcessor.Process(request);

            await using var output = Console.OpenStandardOutput();
            await JsonSerializer.SerializeAsync(output, response, JsonOptions);
            await output.FlushAsync();
            return 0;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync(ex.Message);
            return 1;
        }
    }
}

internal static class SherpaOnnxWorkerProcessor
{
    public static SherpaOnnxWorkerResponse Process(SherpaOnnxWorkerRequest request)
    {
        try
        {
            Console.Error.WriteLine($"SherpaOnnx worker building recognizer config for backend {request.Backend}");
            var config = SherpaOnnxRecognizerConfigFactory.Create(request);
            Console.Error.WriteLine($"SherpaOnnx worker reading WAV input {request.AudioPath}");
            var wave = WaveFileReader.ReadMonoPcm(request.AudioPath);
            Console.Error.WriteLine($"SherpaOnnx worker loaded WAV. sampleRate={wave.SampleRate}, durationMs={wave.DurationMs}, sampleCount={wave.Samples.Length}");
            var decoded = request.Backend == SherpaOnnxBackend.Whisper
                ? SherpaOnnxWhisperChunkProcessor.Process(config, wave, request.LogSegments)
                : SherpaOnnxSinglePassProcessor.Process(config, wave);
            var detectedLanguage = string.Equals(request.LanguageMode, "Fixed", StringComparison.OrdinalIgnoreCase)
                ? request.LanguageCode?.Trim()
                : null;

            if (request.LogSegments && request.Backend != SherpaOnnxBackend.Whisper)
                SherpaOnnxSegmentLogger.LogSegments(decoded.Segments, "SherpaOnnx");

            Console.Error.WriteLine(
                $"SherpaOnnx worker decode complete. segments={decoded.Segments.Length}, plainTextLength={decoded.PlainText.Length}");

            return new SherpaOnnxWorkerResponse
            {
                PlainText = decoded.PlainText,
                Segments = decoded.Segments,
                DetectedLanguage = string.IsNullOrWhiteSpace(detectedLanguage) ? null : detectedLanguage,
                DurationMs = wave.DurationMs,
            };
        }
        catch (DllNotFoundException ex)
        {
            throw new InvalidOperationException(
                "SherpaOnnx native runtime is unavailable. Ensure the org.k2fsa.sherpa.onnx runtime assets for this platform are deployed.",
                ex);
        }
        catch (BadImageFormatException ex)
        {
            throw new InvalidOperationException(
                "SherpaOnnx native runtime could not be loaded for this platform. Verify that the deployed runtime assets match the host architecture.",
                ex);
        }
    }
}

internal static class SherpaOnnxRecognizerConfigFactory
{
    private const int WhisperChunkDurationMs = 28_000;

    public static OfflineRecognizerConfig Create(SherpaOnnxWorkerRequest request)
    {
        var normalizedLanguage = string.Equals(request.LanguageMode, "Fixed", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(request.LanguageCode)
                ? request.LanguageCode.Trim()
                : "auto";

        var config = new OfflineRecognizerConfig
        {
            DecodingMethod = "greedy_search",
            ModelConfig = new OfflineModelConfig
            {
                Debug = 0,
                NumThreads = Math.Max(1, request.NumThreads),
                Provider = request.Provider,
                Tokens = request.TokensPath,
                ModelType = request.Backend == SherpaOnnxBackend.SenseVoice ? "sense_voice" : "whisper",
            },
        };

        switch (request.Backend)
        {
            case SherpaOnnxBackend.SenseVoice:
                config.ModelConfig.SenseVoice = new OfflineSenseVoiceModelConfig
                {
                    Model = request.ModelPath ?? throw new InvalidOperationException("SherpaOnnx SenseVoice worker request is missing ModelPath."),
                    Language = normalizedLanguage,
                    UseInverseTextNormalization = request.UseInverseTextNormalization ? 1 : 0,
                };
                break;
            case SherpaOnnxBackend.Whisper:
                config.ModelConfig.Whisper = new OfflineWhisperModelConfig
                {
                    Encoder = request.EncoderPath ?? throw new InvalidOperationException("SherpaOnnx Whisper worker request is missing EncoderPath."),
                    Decoder = request.DecoderPath ?? throw new InvalidOperationException("SherpaOnnx Whisper worker request is missing DecoderPath."),
                    Language = normalizedLanguage == "auto" ? string.Empty : normalizedLanguage,
                    Task = request.Task,
                    TailPaddings = Math.Max(1000, WhisperChunkDurationMs / 10),
                    EnableSegmentTimestamps = 1,
                    EnableTokenTimestamps = 1,
                };
                break;
            default:
                throw new InvalidOperationException($"Unsupported SherpaOnnx backend '{request.Backend}'.");
        }

        return config;
    }
}

internal sealed record SherpaOnnxDecodedTranscript(string PlainText, TranscriptSegmentDto[] Segments);

internal static class SherpaOnnxSinglePassProcessor
{
    public static SherpaOnnxDecodedTranscript Process(OfflineRecognizerConfig config, WaveFileData wave)
    {
        Console.Error.WriteLine("SherpaOnnx single-pass decode starting");
        using var recognizer = new OfflineRecognizer(config);
        var decoded = DecodeWave(recognizer, wave, startOffsetMs: 0);
        Console.Error.WriteLine($"SherpaOnnx single-pass decode finished. segments={decoded.Segments.Length}");
        return decoded;
    }

    public static SherpaOnnxDecodedTranscript DecodeWave(OfflineRecognizer recognizer, WaveFileData wave, long startOffsetMs)
    {
        using var stream = recognizer.CreateStream();
        stream.AcceptWaveform(wave.SampleRate, wave.Samples);
        recognizer.Decode(stream);

        var result = stream.Result;
        var segments = SherpaOnnxSegmentBuilder.Build(result.Text, result.Timestamps, wave.DurationMs, startOffsetMs);
        var plainText = string.IsNullOrWhiteSpace(result.Text)
            ? string.Join(" ", segments.Select(segment => segment.Text))
            : result.Text.Trim();

        return new SherpaOnnxDecodedTranscript(plainText, segments);
    }
}

internal sealed record SherpaOnnxAudioChunk(float[] Samples, int SampleRate, long StartOffsetMs, long DurationMs);

internal static class SherpaOnnxWhisperChunkProcessor
{
    internal const int MaxChunkDurationMs = 28_000;

    public static SherpaOnnxDecodedTranscript Process(OfflineRecognizerConfig config, WaveFileData wave, bool logSegments = false)
    {
        var chunks = CreateChunks(wave);
        if (chunks.Count == 0)
            return new SherpaOnnxDecodedTranscript(string.Empty, []);

        Console.Error.WriteLine($"SherpaOnnx whisper chunked decode starting. chunkCount={chunks.Count}");
        using var recognizer = new OfflineRecognizer(config);
        var plainTextParts = new List<string>(chunks.Count);
        var segments = new List<TranscriptSegmentDto>();

        for (var chunkIndex = 0; chunkIndex < chunks.Count; chunkIndex++)
        {
            var chunk = chunks[chunkIndex];
            if (chunkIndex == 0 || chunkIndex == chunks.Count - 1 || (chunkIndex + 1) % 10 == 0)
            {
                Console.Error.WriteLine(
                    $"SherpaOnnx decoding chunk {chunkIndex + 1}/{chunks.Count}. startOffsetMs={chunk.StartOffsetMs}, durationMs={chunk.DurationMs}");
            }

            var decoded = SherpaOnnxSinglePassProcessor.DecodeWave(
                recognizer,
                new WaveFileData(chunk.Samples, chunk.SampleRate, chunk.DurationMs),
                chunk.StartOffsetMs);

            if (!string.IsNullOrWhiteSpace(decoded.PlainText))
                plainTextParts.Add(decoded.PlainText.Trim());

            segments.AddRange(decoded.Segments);
            if (logSegments)
                SherpaOnnxSegmentLogger.LogSegments(decoded.Segments, $"SherpaOnnx chunk {chunkIndex + 1}/{chunks.Count}");
        }

        Console.Error.WriteLine($"SherpaOnnx whisper chunked decode finished. segments={segments.Count}");
        return new SherpaOnnxDecodedTranscript(
            string.Join(" ", plainTextParts.Where(part => part.Length > 0)),
            segments.ToArray());
    }

    internal static IReadOnlyList<SherpaOnnxAudioChunk> CreateChunks(WaveFileData wave, int maxChunkDurationMs = MaxChunkDurationMs)
    {
        if (wave.SampleRate <= 0)
            throw new InvalidOperationException("Wave sample rate must be positive.");

        if (maxChunkDurationMs <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxChunkDurationMs));

        if (wave.Samples.Length == 0)
            return [];

        var samplesPerChunk = Math.Max(1, (int)Math.Floor(wave.SampleRate * (maxChunkDurationMs / 1000d)));
        var chunks = new List<SherpaOnnxAudioChunk>();

        for (var sampleOffset = 0; sampleOffset < wave.Samples.Length; sampleOffset += samplesPerChunk)
        {
            var sampleCount = Math.Min(samplesPerChunk, wave.Samples.Length - sampleOffset);
            var chunkSamples = new float[sampleCount];
            Array.Copy(wave.Samples, sampleOffset, chunkSamples, 0, sampleCount);

            var startOffsetMs = (long)Math.Round(sampleOffset * 1000d / wave.SampleRate);
            var durationMs = (long)Math.Round(sampleCount * 1000d / wave.SampleRate);
            chunks.Add(new SherpaOnnxAudioChunk(chunkSamples, wave.SampleRate, startOffsetMs, durationMs));
        }

        return chunks;
    }
}

internal static class SherpaOnnxSegmentLogger
{
    public static void LogSegments(IReadOnlyList<TranscriptSegmentDto> segments, string scope)
    {
        for (var index = 0; index < segments.Count; index++)
        {
            var segment = segments[index];
            Console.Error.WriteLine(
                $"{scope} segment {index + 1}: startMs={segment.StartMs}, endMs={segment.EndMs}, text={FormatSegmentText(segment.Text)}");
        }
    }

    private static string FormatSegmentText(string text)
        => text.Replace('\r', ' ').Replace('\n', ' ').Trim();
}

internal sealed record WaveFileData(float[] Samples, int SampleRate, long DurationMs);

internal static class WaveFileReader
{
    public static WaveFileData ReadMonoPcm(string path)
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

        if (channels == 0 || sampleRate <= 0 || dataChunk is null)
            throw new InvalidOperationException($"Unsupported WAV file at {path}: missing format or data chunk.");

        var samples = formatTag switch
        {
            1 when bitsPerSample == 16 => ConvertInt16Pcm(dataChunk, channels),
            3 when bitsPerSample == 32 => ConvertFloatPcm(dataChunk, channels),
            _ => throw new InvalidOperationException($"Unsupported WAV encoding in {path}: formatTag={formatTag}, bitsPerSample={bitsPerSample}."),
        };

        var durationMs = sampleRate > 0
            ? (long)Math.Round(samples.Length * 1000d / sampleRate)
            : 0L;

        return new WaveFileData(samples, sampleRate, durationMs);
    }

    private static float[] ConvertInt16Pcm(byte[] buffer, ushort channels)
    {
        var frameCount = buffer.Length / 2 / channels;
        var samples = new float[frameCount];

        for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            var byteIndex = frameIndex * channels * 2;
            var sample = BitConverter.ToInt16(buffer, byteIndex);
            samples[frameIndex] = sample / 32768f;
        }

        return samples;
    }

    private static float[] ConvertFloatPcm(byte[] buffer, ushort channels)
    {
        var frameCount = buffer.Length / 4 / channels;
        var samples = new float[frameCount];

        for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            var byteIndex = frameIndex * channels * 4;
            samples[frameIndex] = BitConverter.ToSingle(buffer, byteIndex);
        }

        return samples;
    }
}

internal static class SherpaOnnxSegmentBuilder
{
    public static TranscriptSegmentDto[] Build(string? text, IReadOnlyList<float>? timestamps, long durationMs, long startOffsetMs = 0)
    {
        var normalizedText = text?.Trim() ?? string.Empty;
        if (normalizedText.Length == 0)
            return [];

        var words = normalizedText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var millis = (timestamps ?? [])
            .Select(value => Math.Max(0L, (long)Math.Round(value * 1000)))
            .ToArray();

        if (millis.Length == words.Length + 1)
        {
            return words.Select((word, index) => new TranscriptSegmentDto
            {
                StartMs = startOffsetMs + millis[index],
                EndMs = startOffsetMs + Math.Max(millis[index], millis[index + 1]),
                Text = word,
                Speaker = null,
            }).ToArray();
        }

        if (millis.Length == words.Length)
        {
            return words.Select((word, index) => new TranscriptSegmentDto
            {
                StartMs = startOffsetMs + millis[index],
                EndMs = startOffsetMs + (index + 1 < millis.Length ? Math.Max(millis[index], millis[index + 1]) : Math.Max(millis[index], durationMs)),
                Text = word,
                Speaker = null,
            }).ToArray();
        }

        return
        [
            new TranscriptSegmentDto
            {
                StartMs = startOffsetMs,
                EndMs = startOffsetMs + durationMs,
                Text = normalizedText,
                Speaker = null,
            }
        ];
    }
}

internal enum SherpaOnnxBackend
{
    SenseVoice,
    Whisper,
}

internal sealed record SherpaOnnxWorkerRequest
{
    public required string AudioPath { get; init; }
    public required SherpaOnnxBackend Backend { get; init; }
    public required string TokensPath { get; init; }
    public string? ModelPath { get; init; }
    public string? EncoderPath { get; init; }
    public string? DecoderPath { get; init; }
    public required bool UseInverseTextNormalization { get; init; }
    public required string Task { get; init; }
    public required string Provider { get; init; }
    public required int NumThreads { get; init; }
    public required string LanguageMode { get; init; }
    public string? LanguageCode { get; init; }
    public required bool LogSegments { get; init; }
}

internal sealed record SherpaOnnxWorkerResponse
{
    public required string PlainText { get; init; }
    public required TranscriptSegmentDto[] Segments { get; init; }
    public string? DetectedLanguage { get; init; }
    public long? DurationMs { get; init; }
}

internal sealed record TranscriptSegmentDto
{
    public required long StartMs { get; init; }
    public required long EndMs { get; init; }
    public required string Text { get; init; }
    public string? Speaker { get; init; }
}
