using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClassTranscriber.Api.Contracts;
using ClassTranscriber.Api.Domain;

namespace ClassTranscriber.Api.Transcription;

internal sealed class XaiResponseMapper(decimal estimatedCostPerHourUsd)
{
    public TranscriptionResult MapTranscription(string json, ProjectSettings settings)
    {
        var response = Deserialize(json, "xAI returned an invalid transcription response.");
        var words = response.Words ?? [];
        var durationSeconds = response.Duration > 0
            ? response.Duration
            : words.Count > 0 ? words.Max(word => word.End) : 0;
        var durationMs = durationSeconds > 0 ? (long?)Math.Round(durationSeconds * 1000) : null;
        var nativeDiarization = settings.DiarizationEnabled
            && string.Equals(settings.DiarizationSource, "Provider", StringComparison.OrdinalIgnoreCase)
            && words.Any(word => word.Speaker is not null);
        var segments = nativeDiarization ? BuildSpeakerTurns(words) : BuildReadableSegments(words);
        if (segments.Length == 0 && !string.IsNullOrWhiteSpace(response.Text))
            segments = [new TranscriptSegmentDto { StartMs = 0, EndMs = durationMs ?? 0, Text = response.Text }];

        var plainText = string.IsNullOrWhiteSpace(response.Text) ? JoinWords(words.Select(word => word.Token)) : response.Text;
        var rateMicroUsd = ToRateMicroUsd();
        var estimatedCost = EstimateCost((decimal)durationSeconds, rateMicroUsd);
        return new TranscriptionResult(
            plainText,
            segments,
            response.Language,
            durationMs,
            new TranscriptionProcessingMetadata(
                "xAI",
                settings.Model,
                1,
                nativeDiarization,
                estimatedCost,
                rateMicroUsd,
                "Estimated"))
        {
            Words = words.Select(word => new TranscriptionWord(
                word.Token,
                (long)Math.Round(word.Start * 1000),
                (long)Math.Round(word.End * 1000))).ToArray(),
        };
    }

    public XaiDiarizationResult MapDiarization(string json, long? fallbackDurationMs)
    {
        const string invalidMessage = "xAI returned an invalid diarization response.";
        var response = Deserialize(json, invalidMessage);
        var words = response.Words;
        if (words is null
            || words.Count == 0
            || words.Any(word => word.Speaker is null
                || !double.IsFinite(word.Start)
                || !double.IsFinite(word.End)
                || word.Start < 0
                || word.End < word.Start))
        {
            throw new InvalidOperationException(invalidMessage);
        }

        XaiSpeakerInterval[] intervals;
        try
        {
            intervals = words.Select(word => new XaiSpeakerInterval(
                word.Speaker!.Value.ToString(CultureInfo.InvariantCulture),
                checked((long)Math.Round(word.Start * 1000, MidpointRounding.AwayFromZero)),
                checked((long)Math.Round(word.End * 1000, MidpointRounding.AwayFromZero))))
                .ToArray();
        }
        catch (OverflowException)
        {
            throw new InvalidOperationException(invalidMessage);
        }

        var durationSeconds = double.IsFinite(response.Duration) && response.Duration > 0
            ? (decimal)response.Duration
            : fallbackDurationMs is > 0 ? fallbackDurationMs.Value / 1000m : 0m;
        var rateMicroUsd = ToRateMicroUsd();
        return new XaiDiarizationResult(
            intervals,
            XaiTranscriptionEngine.PreferredModel,
            1,
            EstimateCost(durationSeconds, rateMicroUsd),
            rateMicroUsd,
            "Estimated");
    }

    private static XaiResponse Deserialize(string json, string invalidMessage)
    {
        try
        {
            return JsonSerializer.Deserialize<XaiResponse>(json) ?? throw new JsonException();
        }
        catch (JsonException)
        {
            throw new InvalidOperationException(invalidMessage);
        }
    }

    private long ToRateMicroUsd() => checked((long)Math.Round(
        estimatedCostPerHourUsd * 1_000_000m,
        MidpointRounding.AwayFromZero));

    private static long? EstimateCost(decimal durationSeconds, long rateMicroUsd) =>
        durationSeconds > 0
            ? checked((long)Math.Round(durationSeconds / 3600m * rateMicroUsd, MidpointRounding.AwayFromZero))
            : null;

    private static TranscriptSegmentDto[] BuildSpeakerTurns(IReadOnlyList<XaiWord> words)
    {
        var labels = new Dictionary<int, string>();
        var segments = new List<TranscriptSegmentDto>();
        var current = new List<XaiWord>();
        int? currentSpeaker = null;
        foreach (var word in words)
        {
            if (current.Count > 0 && word.Speaker != currentSpeaker)
            {
                segments.Add(CreateSegment(current, labels, currentSpeaker));
                current.Clear();
            }
            currentSpeaker = word.Speaker;
            current.Add(word);
        }
        if (current.Count > 0)
            segments.Add(CreateSegment(current, labels, currentSpeaker));
        return [.. segments];
    }

    private static TranscriptSegmentDto[] BuildReadableSegments(IReadOnlyList<XaiWord> words)
    {
        var segments = new List<TranscriptSegmentDto>();
        var current = new List<XaiWord>();
        foreach (var word in words)
        {
            if (current.Count > 0 && (word.Start - current[^1].End >= 0.8 || word.End - current[0].Start >= 15))
            {
                segments.Add(CreateSegment(current, null, null));
                current.Clear();
            }
            current.Add(word);
            if (word.Token.TrimEnd().EndsWith('.') || word.Token.TrimEnd().EndsWith('?') || word.Token.TrimEnd().EndsWith('!'))
            {
                segments.Add(CreateSegment(current, null, null));
                current.Clear();
            }
        }
        if (current.Count > 0)
            segments.Add(CreateSegment(current, null, null));
        return [.. segments];
    }

    private static TranscriptSegmentDto CreateSegment(
        IReadOnlyList<XaiWord> words,
        Dictionary<int, string>? labels,
        int? speaker)
    {
        string? label = null;
        if (speaker is not null && labels is not null && !labels.TryGetValue(speaker.Value, out label))
        {
            label = $"Speaker {labels.Count + 1}";
            labels[speaker.Value] = label;
        }
        return new TranscriptSegmentDto
        {
            StartMs = (long)Math.Round(words[0].Start * 1000),
            EndMs = (long)Math.Round(words[^1].End * 1000),
            Text = JoinWords(words.Select(word => word.Token)),
            Speaker = label,
        };
    }

    private static string JoinWords(IEnumerable<string> words)
    {
        var builder = new StringBuilder();
        foreach (var value in words)
        {
            var word = value.Trim();
            if (word.Length == 0)
                continue;
            var noLeadingSpace = builder.Length == 0 || IsClosingPunctuation(word[0]) || IsOpeningPunctuation(builder[^1]);
            if (!noLeadingSpace)
                builder.Append(' ');
            builder.Append(word);
        }
        return builder.ToString();
    }

    private static bool IsClosingPunctuation(char value) => ".,!?;:%)]}".Contains(value);
    private static bool IsOpeningPunctuation(char value) => "¿¡([{".Contains(value);
}

internal sealed class XaiResponse
{
    [JsonPropertyName("text")] public string Text { get; set; } = string.Empty;
    [JsonPropertyName("language")] public string? Language { get; set; }
    [JsonPropertyName("duration")] public double Duration { get; set; }
    [JsonPropertyName("words")] public List<XaiWord>? Words { get; set; }
}

internal sealed class XaiWord
{
    [JsonPropertyName("word")] public string? Word { get; set; }
    [JsonPropertyName("text")] public string? Text { get; set; }
    [JsonPropertyName("start")] public double Start { get; set; }
    [JsonPropertyName("end")] public double End { get; set; }
    [JsonPropertyName("confidence")] public double? Confidence { get; set; }
    [JsonPropertyName("speaker")] public int? Speaker { get; set; }
    [JsonIgnore] public string Token => Word ?? Text ?? string.Empty;
}
