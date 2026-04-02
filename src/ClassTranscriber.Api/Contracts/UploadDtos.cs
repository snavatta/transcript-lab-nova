namespace ClassTranscriber.Api.Contracts;

public sealed record BatchUploadResultDto
{
    public required string FolderId { get; init; }
    public required ProjectSummaryDto[] CreatedProjects { get; init; }
}
