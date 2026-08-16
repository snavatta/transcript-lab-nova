using System.Text;
using ClassTranscriber.Api.Contracts;

namespace ClassTranscriber.Api.Transcription.SpeechToText;

internal static class TranscriptionWordSegmenter
{
    private const long SilenceGapMs = 800;
    private const long MaximumSegmentDurationMs = 15_000;

    public static string Join(IReadOnlyList<TranscriptionWord> words)
    {
        var builder = new StringBuilder();
        foreach (var value in words.Select(word => word.Text))
        {
            var word = value.Trim();
            if (word.Length == 0)
                continue;

            var noLeadingSpace = builder.Length == 0
                || IsClosingPunctuation(word[0])
                || IsOpeningPunctuation(builder[^1]);
            if (!noLeadingSpace)
                builder.Append(' ');
            builder.Append(word);
        }

        return builder.ToString();
    }

    public static TranscriptSegmentDto[] BuildReadableSegments(IReadOnlyList<TranscriptionWord> words)
    {
        var segments = new List<TranscriptSegmentDto>();
        var current = new List<TranscriptionWord>();
        foreach (var word in words)
        {
            if (current.Count > 0
                && (word.StartMs - current[^1].EndMs >= SilenceGapMs
                    || word.EndMs - current[0].StartMs >= MaximumSegmentDurationMs))
            {
                segments.Add(CreateSegment(current));
                current.Clear();
            }

            current.Add(word);
            if (word.Text.TrimEnd().EndsWith('.')
                || word.Text.TrimEnd().EndsWith('?')
                || word.Text.TrimEnd().EndsWith('!'))
            {
                segments.Add(CreateSegment(current));
                current.Clear();
            }
        }

        if (current.Count > 0)
            segments.Add(CreateSegment(current));

        return [.. segments];
    }

    private static TranscriptSegmentDto CreateSegment(IReadOnlyList<TranscriptionWord> words) => new()
    {
        StartMs = words[0].StartMs,
        EndMs = words[^1].EndMs,
        Text = Join(words),
    };

    private static bool IsClosingPunctuation(char value) => ".,!?;:%)]}".Contains(value);
    private static bool IsOpeningPunctuation(char value) => "¿¡([{".Contains(value);
}
