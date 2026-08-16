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
    public HostedProcessingMetadataDto? HostedProcessing { get; init; }
}

public sealed record HostedProcessingMetadataDto
{
    public required string SttProvider { get; init; }
    public required string SttModel { get; init; }
    public long? AudioDurationMs { get; init; }
    public required int RequestCount { get; init; }
    public required bool NativeDiarizationUsed { get; init; }
    public decimal? SttCostUsd { get; init; }
    public decimal? SttRateUsdPerHour { get; init; }
    public string? SttCostClassification { get; init; }
    public string? DiarizationSource { get; init; }
    public string? DiarizationProvider { get; init; }
    public string? DiarizationModel { get; init; }
    public int DiarizationRequestCount { get; init; }
    public decimal? DiarizationCostUsd { get; init; }
    public decimal? DiarizationRateUsdPerHour { get; init; }
    public string? DiarizationCostClassification { get; init; }
    public string? RoleAttributionModel { get; init; }
    public string? RoleAttributionStatus { get; init; }
    public int? RoleAttributionPromptTokens { get; init; }
    public int? RoleAttributionOutputTokens { get; init; }
    public decimal? RoleAttributionCostUsd { get; init; }
    public decimal? TotalCostUsd { get; init; }
    public required bool TotalContainsEstimate { get; init; }
}
