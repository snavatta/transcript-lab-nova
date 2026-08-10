namespace ClassTranscriber.Api.Mcp;

public static class ContentErrorCodes
{
    public const string ValidationError = "validation_error";
    public const string NotFound = "not_found";
    public const string TranscriptNotReady = "transcript_not_ready";
    public const string CorruptTranscript = "corrupt_transcript";
    public const string InternalError = "internal_error";
}

public sealed record ContentQueryError(string Code, string Message);

public sealed record ContentQueryResult<T> where T : class
{
    private ContentQueryResult(T? value, ContentQueryError? error)
    {
        Value = value;
        Error = error;
    }

    public T? Value { get; }
    public ContentQueryError? Error { get; }
    public bool IsSuccess => Error is null;

    public static ContentQueryResult<T> Success(T value) => new(value, null);

    public static ContentQueryResult<T> Failure(string code, string message) =>
        new(null, new ContentQueryError(code, message));
}

public sealed record TranscriptSourceProject
{
    public required Guid FolderId { get; init; }
    public required string FolderName { get; init; }
    public required Guid ProjectId { get; init; }
    public required string ProjectName { get; init; }
    public required string OriginalFileName { get; init; }
    public string? DetectedLanguage { get; init; }
    public long? DurationMs { get; init; }
    public required int SegmentCount { get; init; }
    public DateTime? CompletedAtUtc { get; init; }
    public required DateTime TranscriptUpdatedAtUtc { get; init; }
    public required string SourcePath { get; init; }
    public string? SourceUrl { get; init; }
}

public sealed record TranscriptSearchOccurrence
{
    public int? SegmentIndex { get; init; }
    public long? StartMs { get; init; }
    public long? EndMs { get; init; }
    public string? Speaker { get; init; }
    public required string Excerpt { get; init; }
    public required bool ExcerptTruncated { get; init; }
}

public sealed record TranscriptSearchWarnings
{
    public required bool PlainTextFallback { get; init; }
    public required bool StructuredSegmentsAbsent { get; init; }
    public required bool StructuredSegmentsEmpty { get; init; }
    public required bool StructuredSegmentsInvalid { get; init; }
}

public sealed record TranscriptSearchMatch
{
    public required TranscriptSourceProject Project { get; init; }
    public required IReadOnlyList<TranscriptSearchOccurrence> Occurrences { get; init; }
    public required TranscriptSearchWarnings Warnings { get; init; }

    public string ProjectName => Project.ProjectName;
}

public sealed record TranscriptSearchPage
{
    public required IReadOnlyList<TranscriptSearchMatch> Matches { get; init; }
    public required int Offset { get; init; }
    public required int Limit { get; init; }
    public required bool HasMore { get; init; }
    public int? NextOffset { get; init; }
    public required string SearchSemantics { get; init; }
}

public sealed record TranscriptChunk
{
    public int? SegmentIndex { get; init; }
    public long? StartMs { get; init; }
    public long? EndMs { get; init; }
    public string? Speaker { get; init; }
    public required string Text { get; init; }
    public required int TextStartCharacter { get; init; }
    public required bool TextComplete { get; init; }
}

public sealed record TranscriptContentPage
{
    public required TranscriptSourceProject Project { get; init; }
    public required IReadOnlyList<TranscriptChunk> Chunks { get; init; }
    public string? NextCursor { get; init; }
    public required bool HasMore { get; init; }
}
