using System.Text.Json.Serialization;

namespace ClassTranscriber.Api.Contracts;

public sealed record ProjectSettingsDto
{
    public required string Engine { get; init; }
    public required string Model { get; init; }
    public required string LanguageMode { get; init; }
    public string? LanguageCode { get; init; }
    public required bool AudioNormalizationEnabled { get; init; }
    public required bool DiarizationEnabled { get; init; }
}

public sealed record UpdateProjectRequest
{
    public required string Name { get; init; }
}

public sealed record RetryProjectRequest
{
    public ProjectSettingsDto? Settings { get; init; }
}

public sealed record ProjectDebugTimingsDto
{
    public long? TotalElapsedMs { get; init; }
    public long? PreparationElapsedMs { get; init; }
    public long? InspectElapsedMs { get; init; }
    public long? ExtractElapsedMs { get; init; }
    public long? NormalizeElapsedMs { get; init; }
    public long? TranscriptionElapsedMs { get; init; }
    public long? PersistElapsedMs { get; init; }
    public double? TranscriptionRealtimeFactor { get; init; }
    public double? TotalRealtimeFactor { get; init; }
}

public sealed record ProjectSummaryDto
{
    public required string Id { get; init; }
    public required string FolderId { get; init; }
    public required string FolderName { get; init; }
    public required string Name { get; init; }
    public required string OriginalFileName { get; init; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required ProjectStatus Status { get; init; }
    public int? Progress { get; init; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required MediaType MediaType { get; init; }
    public long? DurationMs { get; init; }
    public long? TranscriptionElapsedMs { get; init; }
    public long? TotalSizeBytes { get; init; }
    public required string CreatedAtUtc { get; init; }
    public required string UpdatedAtUtc { get; init; }
}

public sealed record ProjectDetailDto
{
    public required string Id { get; init; }
    public required string FolderId { get; init; }
    public required string FolderName { get; init; }
    public required string Name { get; init; }
    public required string OriginalFileName { get; init; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required ProjectStatus Status { get; init; }
    public int? Progress { get; init; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required MediaType MediaType { get; init; }
    public long? DurationMs { get; init; }
    public long? TranscriptionElapsedMs { get; init; }
    public long? TotalSizeBytes { get; init; }
    public required string CreatedAtUtc { get; init; }
    public required string UpdatedAtUtc { get; init; }
    public string? QueuedAtUtc { get; init; }
    public string? StartedAtUtc { get; init; }
    public string? CompletedAtUtc { get; init; }
    public string? FailedAtUtc { get; init; }
    public string? ErrorMessage { get; init; }
    public required ProjectSettingsDto Settings { get; init; }
    public required string MediaUrl { get; init; }
    public string? AudioPreviewUrl { get; init; }
    public required bool TranscriptAvailable { get; init; }
    public required string[] AvailableExports { get; init; }
    public long? OriginalFileSizeBytes { get; init; }
    public long? WorkspaceSizeBytes { get; init; }
    public ProjectDebugTimingsDto? DebugTimings { get; init; }
}
