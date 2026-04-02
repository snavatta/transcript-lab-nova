namespace ClassTranscriber.Api.Contracts;

public sealed record FolderSummaryDto
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string IconKey { get; init; }
    public required string ColorHex { get; init; }
    public required int ProjectCount { get; init; }
    public long? TotalSizeBytes { get; init; }
    public required string CreatedAtUtc { get; init; }
    public required string UpdatedAtUtc { get; init; }
}

public sealed record FolderDetailDto
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string IconKey { get; init; }
    public required string ColorHex { get; init; }
    public required int ProjectCount { get; init; }
    public long? TotalSizeBytes { get; init; }
    public required string CreatedAtUtc { get; init; }
    public required string UpdatedAtUtc { get; init; }
}

public sealed record CreateFolderRequest
{
    public required string Name { get; init; }
    public string? IconKey { get; init; }
    public string? ColorHex { get; init; }
}

public sealed record UpdateFolderRequest
{
    public required string Name { get; init; }
    public string? IconKey { get; init; }
    public string? ColorHex { get; init; }
}
