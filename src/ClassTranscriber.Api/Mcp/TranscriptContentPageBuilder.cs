using ClassTranscriber.Api.Contracts;

namespace ClassTranscriber.Api.Mcp;

internal static class TranscriptContentPageBuilder
{
    private const int CursorVersion = 1;
    private const string SegmentMode = "segments";
    private const string PlainTextMode = "plainText";

    public static TranscriptContentPage? BuildSegmentPage(
        TranscriptSourceProject project,
        IReadOnlyList<TranscriptSegmentDto> segments,
        TranscriptCursorPayload position,
        int segmentLimit,
        int characterLimit,
        TranscriptCursorCodec cursorCodec)
    {
        if (position.SegmentIndex > segments.Count ||
            (position.SegmentIndex == segments.Count && position.CharacterOffset != 0))
        {
            return null;
        }

        var chunks = new List<TranscriptChunk>(segmentLimit);
        var segmentIndex = position.SegmentIndex;
        var characterOffset = position.CharacterOffset;
        var remainingCharacters = characterLimit;
        while (segmentIndex < segments.Count && chunks.Count < segmentLimit && remainingCharacters > 0)
        {
            var segment = segments[segmentIndex];
            if (characterOffset > segment.Text.Length || StartsInsideSurrogatePair(segment.Text, characterOffset))
                return null;

            var take = Math.Min(segment.Text.Length - characterOffset, remainingCharacters);
            take = AvoidSplitAtEnd(segment.Text, characterOffset, take);
            if (take == 0 && characterOffset < segment.Text.Length)
                break;

            var complete = characterOffset + take == segment.Text.Length;
            chunks.Add(new TranscriptChunk
            {
                SegmentIndex = segmentIndex,
                StartMs = segment.StartMs,
                EndMs = segment.EndMs,
                Speaker = segment.Speaker,
                Text = segment.Text.Substring(characterOffset, take),
                TextStartCharacter = characterOffset,
                TextComplete = complete,
            });
            remainingCharacters -= take;
            if (complete)
            {
                segmentIndex++;
                characterOffset = 0;
            }
            else
            {
                characterOffset += take;
            }
        }

        var hasMore = segmentIndex < segments.Count;
        return new TranscriptContentPage
        {
            Project = project,
            Chunks = chunks,
            HasMore = hasMore,
            NextCursor = hasMore
                ? cursorCodec.Encode(new TranscriptCursorPayload(
                    CursorVersion,
                    SegmentMode,
                    project.ProjectId,
                    project.TranscriptUpdatedAtUtc.Ticks,
                    segmentIndex,
                    characterOffset))
                : null,
        };
    }

    public static TranscriptContentPage? BuildPlainTextPage(
        TranscriptSourceProject project,
        string plainText,
        TranscriptCursorPayload position,
        int characterLimit,
        TranscriptCursorCodec cursorCodec)
    {
        if (position.SegmentIndex != 0 || position.CharacterOffset > plainText.Length ||
            StartsInsideSurrogatePair(plainText, position.CharacterOffset))
        {
            return null;
        }

        var take = Math.Min(plainText.Length - position.CharacterOffset, characterLimit);
        take = AvoidSplitAtEnd(plainText, position.CharacterOffset, take);
        var complete = position.CharacterOffset + take == plainText.Length;
        IReadOnlyList<TranscriptChunk> chunks = take == 0
            ? []
            :
            [
                new TranscriptChunk
                {
                    SegmentIndex = null,
                    Text = plainText.Substring(position.CharacterOffset, take),
                    TextStartCharacter = position.CharacterOffset,
                    TextComplete = complete,
                },
            ];

        return new TranscriptContentPage
        {
            Project = project,
            Chunks = chunks,
            HasMore = !complete,
            NextCursor = complete
                ? null
                : cursorCodec.Encode(new TranscriptCursorPayload(
                    CursorVersion,
                    PlainTextMode,
                    project.ProjectId,
                    project.TranscriptUpdatedAtUtc.Ticks,
                    0,
                    position.CharacterOffset + take)),
        };
    }

    private static bool StartsInsideSurrogatePair(string text, int offset) =>
        offset > 0 && offset < text.Length && char.IsLowSurrogate(text[offset]) &&
        char.IsHighSurrogate(text[offset - 1]);

    private static int AvoidSplitAtEnd(string text, int offset, int length)
    {
        var end = offset + length;
        return end > offset && end < text.Length && char.IsHighSurrogate(text[end - 1]) &&
               char.IsLowSurrogate(text[end])
            ? length - 1
            : length;
    }
}
