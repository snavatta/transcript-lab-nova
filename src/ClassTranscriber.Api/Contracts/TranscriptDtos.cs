namespace ClassTranscriber.Api.Contracts;

public sealed record TranscriptSegmentDto
{
    public required long StartMs { get; init; }
    public required long EndMs { get; init; }
    public required string Text { get; init; }
    public string? Speaker { get; init; }
}

public sealed record TranscriptDto
{
    public required string ProjectId { get; init; }
    public required string PlainText { get; init; }
    public string? DetectedLanguage { get; init; }
    public long? DurationMs { get; init; }
    public required int SegmentCount { get; init; }
    public required TranscriptSegmentDto[] Segments { get; init; }
    public required string CreatedAtUtc { get; init; }
    public required string UpdatedAtUtc { get; init; }
}
