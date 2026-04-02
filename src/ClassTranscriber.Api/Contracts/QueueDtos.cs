using System.Text.Json.Serialization;

namespace ClassTranscriber.Api.Contracts;

public sealed record QueueItemDto
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
    public long? TotalSizeBytes { get; init; }
    public required string Engine { get; init; }
    public required string Model { get; init; }
    public required string CreatedAtUtc { get; init; }
    public required string UpdatedAtUtc { get; init; }
}

public sealed record QueueOverviewDto
{
    public required QueueItemDto[] Drafts { get; init; }
    public required QueueItemDto[] Queued { get; init; }
    public required QueueItemDto[] Processing { get; init; }
    public required QueueItemDto[] Completed { get; init; }
    public required QueueItemDto[] Failed { get; init; }
}
