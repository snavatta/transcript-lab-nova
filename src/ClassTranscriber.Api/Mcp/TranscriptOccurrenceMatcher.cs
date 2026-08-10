using System.Text.Json;
using ClassTranscriber.Api.Contracts;

namespace ClassTranscriber.Api.Mcp;

internal static class TranscriptOccurrenceMatcher
{
    private const int MaximumExcerptCharacters = 500;
    private const int MaximumOccurrences = 3;
    private static readonly JsonSerializerOptions SegmentJsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    public static TranscriptOccurrenceResult Match(string plainText, string structuredJson, string query)
    {
        var segmentState = DeserializeSegments(structuredJson);
        var occurrences = new List<TranscriptSearchOccurrence>(MaximumOccurrences);
        if (segmentState.Status == SegmentJsonStatus.Valid)
        {
            for (var segmentIndex = 0;
                 segmentIndex < segmentState.Segments.Count && occurrences.Count < MaximumOccurrences;
                 segmentIndex++)
            {
                var segment = segmentState.Segments[segmentIndex];
                foreach (var matchIndex in FindOccurrences(
                             segment.Text,
                             query,
                             MaximumOccurrences - occurrences.Count))
                {
                    occurrences.Add(CreateOccurrence(segment.Text, query.Length, matchIndex, segmentIndex, segment));
                }
            }
        }

        var plainTextFallback = occurrences.Count == 0;
        if (plainTextFallback)
        {
            foreach (var matchIndex in FindOccurrences(plainText, query, 1))
                occurrences.Add(CreateOccurrence(plainText, query.Length, matchIndex, null, null));
        }

        return new TranscriptOccurrenceResult(
            occurrences,
            new TranscriptSearchWarnings
            {
                PlainTextFallback = plainTextFallback,
                StructuredSegmentsAbsent = segmentState.Status == SegmentJsonStatus.Absent,
                StructuredSegmentsEmpty = segmentState.Status == SegmentJsonStatus.Empty,
                StructuredSegmentsInvalid = segmentState.Status == SegmentJsonStatus.Invalid,
            });
    }

    public static SegmentJsonState DeserializeSegments(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new SegmentJsonState(SegmentJsonStatus.Absent, []);

        try
        {
            var segments = JsonSerializer.Deserialize<TranscriptSegmentDto[]>(json, SegmentJsonOptions);
            if (segments is null)
                return new SegmentJsonState(SegmentJsonStatus.Absent, []);
            if (segments.Any(segment => segment.Text is null || !IsWellFormedUtf16(segment.Text)))
                return new SegmentJsonState(SegmentJsonStatus.Invalid, []);
            return segments.Length == 0
                ? new SegmentJsonState(SegmentJsonStatus.Empty, [])
                : new SegmentJsonState(SegmentJsonStatus.Valid, segments);
        }
        catch (JsonException)
        {
            return new SegmentJsonState(SegmentJsonStatus.Invalid, []);
        }
    }

    public static bool IsWellFormedUtf16(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (char.IsHighSurrogate(value[index]))
            {
                if (index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1]))
                    return false;
                index++;
            }
            else if (char.IsLowSurrogate(value[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static IEnumerable<int> FindOccurrences(string text, string query, int limit)
    {
        var start = 0;
        for (var found = 0; found < limit && start <= text.Length - query.Length; found++)
        {
            var index = IndexOfAsciiCaseInsensitive(text, query, start);
            if (index < 0)
                yield break;
            yield return index;
            start = index + query.Length;
        }
    }

    private static int IndexOfAsciiCaseInsensitive(string text, string query, int start)
    {
        for (var index = start; index <= text.Length - query.Length; index++)
        {
            var matches = true;
            for (var queryIndex = 0; queryIndex < query.Length; queryIndex++)
            {
                if (FoldAscii(text[index + queryIndex]) == FoldAscii(query[queryIndex]))
                    continue;
                matches = false;
                break;
            }

            if (matches)
                return index;
        }

        return -1;
    }

    private static char FoldAscii(char value) => value is >= 'A' and <= 'Z'
        ? (char)(value + ('a' - 'A'))
        : value;

    private static TranscriptSearchOccurrence CreateOccurrence(
        string text,
        int queryLength,
        int matchIndex,
        int? segmentIndex,
        TranscriptSegmentDto? segment)
    {
        var preferredPrefix = Math.Max(0, (MaximumExcerptCharacters - queryLength) / 2);
        var excerptStart = Math.Max(0, matchIndex - preferredPrefix);
        var excerptLength = Math.Min(MaximumExcerptCharacters, text.Length - excerptStart);
        if (excerptLength < MaximumExcerptCharacters && excerptStart > 0)
        {
            excerptStart = Math.Max(0, text.Length - MaximumExcerptCharacters);
            excerptLength = text.Length - excerptStart;
        }

        if (excerptStart > 0 && char.IsLowSurrogate(text[excerptStart]))
            excerptStart--;
        var excerptEnd = Math.Min(text.Length, excerptStart + MaximumExcerptCharacters);
        if (excerptEnd < text.Length && char.IsLowSurrogate(text[excerptEnd]))
            excerptEnd--;

        return new TranscriptSearchOccurrence
        {
            SegmentIndex = segmentIndex,
            StartMs = segment?.StartMs,
            EndMs = segment?.EndMs,
            Speaker = segment?.Speaker,
            Excerpt = text[excerptStart..excerptEnd],
            ExcerptTruncated = excerptStart > 0 || excerptEnd < text.Length,
        };
    }
}

internal sealed record TranscriptOccurrenceResult(
    IReadOnlyList<TranscriptSearchOccurrence> Occurrences,
    TranscriptSearchWarnings Warnings);

internal sealed record SegmentJsonState(SegmentJsonStatus Status, IReadOnlyList<TranscriptSegmentDto> Segments);

internal enum SegmentJsonStatus
{
    Valid,
    Absent,
    Empty,
    Invalid,
}
