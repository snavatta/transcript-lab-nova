using System.Text.Json.Serialization;

namespace ClassTranscriber.Api.Contracts;

public sealed record RuntimeDiagnosticsDto
{
    public required string CollectedAtUtc { get; init; }
    public required int ProcessId { get; init; }
    public required int ProcessorCount { get; init; }
    public required long UptimeMs { get; init; }
    public required double CpuUsagePercent { get; init; }
    public required long WorkingSetBytes { get; init; }
    public required long PrivateMemoryBytes { get; init; }
    public required long ManagedHeapBytes { get; init; }
}

public sealed record DiagnosticsEngineDto
{
    public required string Engine { get; init; }
    public required bool IsAvailable { get; init; }
    public required string[] Models { get; init; }
    public string? AvailabilityError { get; init; }
}

public sealed record ProjectStorageDiagnosticsDto
{
    public required string ProjectId { get; init; }
    public required string FolderId { get; init; }
    public required string FolderName { get; init; }
    public required string ProjectName { get; init; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required ProjectStatus Status { get; init; }
    public long? OriginalFileSizeBytes { get; init; }
    public long? WorkspaceSizeBytes { get; init; }
    public long? TotalSizeBytes { get; init; }
    public required string UpdatedAtUtc { get; init; }
}

public sealed record DiagnosticsDto
{
    public required RuntimeDiagnosticsDto Runtime { get; init; }
    public required DiagnosticsEngineDto[] Engines { get; init; }
    public required ProjectStorageDiagnosticsDto[] Projects { get; init; }
}
