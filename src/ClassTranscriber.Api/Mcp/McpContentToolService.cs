namespace ClassTranscriber.Api.Mcp;

public interface IMcpContentToolService
{
    Task<ContentQueryResult<TranscriptSearchPage>> SearchAsync(
        string query,
        Guid? folderId = null,
        int offset = 0,
        int limit = 10,
        CancellationToken cancellationToken = default);

    Task<ContentQueryResult<TranscriptContentPage>> GetTranscriptAsync(
        Guid projectId,
        string? cursor = null,
        int segmentLimit = 100,
        int characterLimit = 12_000,
        CancellationToken cancellationToken = default);
}

public sealed class McpContentToolService(McpContentService contentService)
    : IMcpContentToolService
{
    public Task<ContentQueryResult<TranscriptSearchPage>> SearchAsync(
        string query,
        Guid? folderId = null,
        int offset = 0,
        int limit = 10,
        CancellationToken cancellationToken = default) =>
        contentService.SearchAsync(query, folderId, offset, limit, cancellationToken);

    public Task<ContentQueryResult<TranscriptContentPage>> GetTranscriptAsync(
        Guid projectId,
        string? cursor = null,
        int segmentLimit = 100,
        int characterLimit = 12_000,
        CancellationToken cancellationToken = default) =>
        contentService.GetTranscriptAsync(projectId, cursor, segmentLimit, characterLimit, cancellationToken);
}
