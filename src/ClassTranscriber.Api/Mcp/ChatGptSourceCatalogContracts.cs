namespace ClassTranscriber.Api.Mcp;

public sealed record ChatGptSourceFolderCatalog
{
    public required Guid FolderId { get; init; }
    public required string FolderName { get; init; }
    public required int ProjectCount { get; init; }
    public required DateTime UpdatedAtUtc { get; init; }
}

public sealed record ChatGptSourceProjectCatalog
{
    public required Guid FolderId { get; init; }
    public required string FolderName { get; init; }
    public required Guid ProjectId { get; init; }
    public required string ProjectName { get; init; }
    public required string OriginalFileName { get; init; }
    public string? DetectedLanguage { get; init; }
    public long? DurationMs { get; init; }
    public required int SegmentCount { get; init; }
    public required DateTime CompletedAtUtc { get; init; }
    public required DateTime TranscriptUpdatedAtUtc { get; init; }
    public required string SourcePath { get; init; }
    public string? SourceUrl { get; init; }
}

public sealed record ChatGptSourceFolderPage
{
    public required IReadOnlyList<ChatGptSourceFolderCatalog> Folders { get; init; }
    public required int Offset { get; init; }
    public required int Limit { get; init; }
    public required bool HasMore { get; init; }
    public int? NextOffset { get; init; }
}

public sealed record ChatGptSourceProjectPage
{
    public required IReadOnlyList<ChatGptSourceProjectCatalog> Projects { get; init; }
    public required int Offset { get; init; }
    public required int Limit { get; init; }
    public required bool HasMore { get; init; }
    public int? NextOffset { get; init; }
}
