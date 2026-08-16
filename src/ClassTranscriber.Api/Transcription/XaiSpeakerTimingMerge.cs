using ClassTranscriber.Api.Contracts;

namespace ClassTranscriber.Api.Transcription;

public sealed record XaiSpeakerInterval(string SpeakerId, long StartMs, long EndMs);

public static class XaiSpeakerTimingMerge
{
    public const long NearestSpeakerToleranceMs = 1_000;

    public static TranscriptionResult Apply(
        TranscriptionResult openRouterResult,
        IReadOnlyList<XaiSpeakerInterval> xaiIntervals)
    {
        ArgumentNullException.ThrowIfNull(openRouterResult);
        ArgumentNullException.ThrowIfNull(xaiIntervals);

        var orderedIntervals = xaiIntervals
            .Where(IsUsable)
            .Select((interval, index) => new IndexedInterval(interval, index))
            .OrderBy(candidate => candidate.Interval.StartMs)
            .ThenBy(candidate => candidate.Index)
            .ToArray();
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        var labeledWords = openRouterResult.Words
            .Select(word => new LabeledWord(word, ResolveSpeaker(word, orderedIntervals, labels)))
            .ToArray();

        return openRouterResult with
        {
            Segments = GroupConsecutiveWords(labeledWords),
        };
    }

    private static string? ResolveSpeaker(
        TranscriptionWord word,
        IReadOnlyList<IndexedInterval> intervals,
        Dictionary<string, string> labels)
    {
        var selected = intervals
            .Select(candidate => new ScoredInterval(
                candidate,
                PositiveOverlap(word, candidate.Interval),
                Distance(word, candidate.Interval)))
            .Where(candidate => candidate.OverlapMs > 0)
            .OrderByDescending(candidate => candidate.OverlapMs)
            .ThenBy(candidate => candidate.Candidate.Interval.StartMs)
            .ThenBy(candidate => candidate.Candidate.Index)
            .FirstOrDefault();

        if (selected is null)
        {
            selected = intervals
                .Select(candidate => new ScoredInterval(candidate, 0, Distance(word, candidate.Interval)))
                .Where(candidate => candidate.DistanceMs <= NearestSpeakerToleranceMs)
                .OrderBy(candidate => candidate.DistanceMs)
                .ThenBy(candidate => candidate.Candidate.Interval.StartMs)
                .ThenBy(candidate => candidate.Candidate.Index)
                .FirstOrDefault();
        }

        if (selected is null)
            return null;

        var speakerId = selected.Candidate.Interval.SpeakerId;
        if (!labels.TryGetValue(speakerId, out var label))
        {
            label = $"Speaker {labels.Count + 1}";
            labels.Add(speakerId, label);
        }

        return label;
    }

    private static TranscriptSegmentDto[] GroupConsecutiveWords(IReadOnlyList<LabeledWord> words)
    {
        if (words.Count == 0)
            return [];

        var segments = new List<TranscriptSegmentDto>();
        var groupStart = 0;
        for (var index = 1; index <= words.Count; index += 1)
        {
            if (index < words.Count
                && string.Equals(words[index].Speaker, words[groupStart].Speaker, StringComparison.Ordinal))
            {
                continue;
            }

            var group = words.Skip(groupStart).Take(index - groupStart).ToArray();
            segments.Add(new TranscriptSegmentDto
            {
                StartMs = group[0].Word.StartMs,
                EndMs = group[^1].Word.EndMs,
                Text = string.Concat(group.Select(item => item.Word.Text)),
                Speaker = group[0].Speaker,
            });
            groupStart = index;
        }

        return [.. segments];
    }

    private static bool IsUsable(XaiSpeakerInterval interval) =>
        !string.IsNullOrWhiteSpace(interval.SpeakerId)
        && interval.StartMs >= 0
        && interval.EndMs >= interval.StartMs;

    private static long PositiveOverlap(TranscriptionWord word, XaiSpeakerInterval interval) =>
        Math.Max(0, Math.Min(word.EndMs, interval.EndMs) - Math.Max(word.StartMs, interval.StartMs));

    private static long Distance(TranscriptionWord word, XaiSpeakerInterval interval)
    {
        if (word.EndMs < interval.StartMs)
            return interval.StartMs - word.EndMs;
        if (interval.EndMs < word.StartMs)
            return word.StartMs - interval.EndMs;
        return 0;
    }

    private sealed record IndexedInterval(XaiSpeakerInterval Interval, int Index);
    private sealed record ScoredInterval(IndexedInterval Candidate, long OverlapMs, long DistanceMs);
    private sealed record LabeledWord(TranscriptionWord Word, string? Speaker);
}
