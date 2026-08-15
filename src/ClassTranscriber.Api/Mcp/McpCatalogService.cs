using ClassTranscriber.Api;
using ClassTranscriber.Api.Contracts;
using ClassTranscriber.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClassTranscriber.Api.Mcp;

public interface IMcpCatalogService
{
    Task<McpFolderPage> ListFoldersAsync(int offset = 0, int limit = 50, CancellationToken cancellationToken = default);

    Task<McpProjectPage> ListProjectsAsync(
        Guid? folderId = null,
        string? nameQuery = null,
        int offset = 0,
        int limit = 20,
        CancellationToken cancellationToken = default);
}

public sealed class McpCatalogService : IMcpCatalogService
{
    private readonly AppDbContext _db;
    private readonly Uri? _applicationBaseUri;

    public McpCatalogService(AppDbContext db, McpOptions options)
    {
        _db = db;
        McpOptions.TryNormalizeApplicationBaseUrl(options.ApplicationBaseUrl, out _applicationBaseUri);
    }

    public async Task<McpFolderPage> ListFoldersAsync(
        int offset = 0,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var page = NormalizePage(offset, limit, 100);
        var folders = await _db.Folders
            .AsNoTracking()
            .OrderBy(folder => EF.Functions.Collate(folder.Name, "NOCASE"))
            .ThenBy(folder => folder.Id)
            .Select(folder => new McpFolderCatalog
            {
                FolderId = folder.Id,
                FolderName = folder.Name,
                ProjectCount = folder.Projects.Count,
                UpdatedAtUtc = folder.UpdatedAtUtc,
            })
            .Skip(page.Offset)
            .Take(page.Limit + 1)
            .ToListAsync(cancellationToken);

        return CreateFolderPage(folders, page.Offset, page.Limit);
    }

    public async Task<McpProjectPage> ListProjectsAsync(
        Guid? folderId = null,
        string? nameQuery = null,
        int offset = 0,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        if (folderId == Guid.Empty)
            throw new ArgumentException("Folder ID must not be empty.", nameof(folderId));

        var page = NormalizePage(offset, limit, 50);
        var normalizedNameQuery = NormalizeNameQuery(nameQuery);
        var query = _db.Projects
            .AsNoTracking()
            .Where(project => project.Status == ProjectStatus.Completed && project.Transcript != null);

        if (folderId.HasValue)
            query = query.Where(project => project.FolderId == folderId.Value);

        if (normalizedNameQuery is not null)
        {
            var pattern = $"%{EscapeLikePattern(normalizedNameQuery)}%";
            query = query.Where(project =>
                EF.Functions.Like(project.Name, pattern, "\\")
                || EF.Functions.Like(project.OriginalFileName, pattern, "\\"));
        }

        var projects = await query
            .OrderByDescending(project => project.CompletedAtUtc)
            .ThenBy(project => project.Id)
            .Select(project => new McpProjectCatalog
            {
                FolderId = project.FolderId,
                FolderName = project.Folder.Name,
                ProjectId = project.Id,
                ProjectName = project.Name,
                OriginalFileName = project.OriginalFileName,
                DetectedLanguage = project.Transcript!.DetectedLanguage,
                DurationMs = project.Transcript.DurationMs ?? project.DurationMs,
                SegmentCount = project.Transcript.SegmentCount,
                CompletedAtUtc = project.CompletedAtUtc!.Value,
                TranscriptUpdatedAtUtc = project.Transcript.UpdatedAtUtc,
                SourcePath = $"/projects/{project.Id}",
            })
            .Skip(page.Offset)
            .Take(page.Limit + 1)
            .ToListAsync(cancellationToken);

        if (_applicationBaseUri is not null)
        {
            for (var index = 0; index < projects.Count; index++)
            {
                var relativeProjectPath = projects[index].SourcePath.TrimStart('/');
                projects[index] = projects[index] with
                {
                    SourceUrl = new Uri(_applicationBaseUri, relativeProjectPath).AbsoluteUri,
                };
            }
        }

        return CreateProjectPage(projects, page.Offset, page.Limit);
    }

    private static McpFolderPage CreateFolderPage(
        List<McpFolderCatalog> folders, int offset, int limit)
    {
        var hasMore = folders.Count > limit;
        if (hasMore)
            folders.RemoveAt(limit);
        return new McpFolderPage
        {
            Folders = folders,
            Offset = offset,
            Limit = limit,
            HasMore = hasMore,
            NextOffset = hasMore ? offset + limit : null,
        };
    }

    private static McpProjectPage CreateProjectPage(
        List<McpProjectCatalog> projects, int offset, int limit)
    {
        var hasMore = projects.Count > limit;
        if (hasMore)
            projects.RemoveAt(limit);
        return new McpProjectPage
        {
            Projects = projects,
            Offset = offset,
            Limit = limit,
            HasMore = hasMore,
            NextOffset = hasMore ? offset + limit : null,
        };
    }

    private static (int Offset, int Limit) NormalizePage(int offset, int limit, int maximumLimit)
        => (Math.Max(offset, 0), Math.Clamp(limit, 1, maximumLimit));

    private static string? NormalizeNameQuery(string? nameQuery)
    {
        var value = nameQuery?.Trim();
        return string.IsNullOrEmpty(value) ? null : value[..Math.Min(value.Length, 200)];
    }

    private static string EscapeLikePattern(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

}
