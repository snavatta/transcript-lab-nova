using ClassTranscriber.Api.Contracts;
using ClassTranscriber.Api.Domain;
using ClassTranscriber.Api.Persistence;
using ClassTranscriber.Api.Storage;
using Microsoft.EntityFrameworkCore;

namespace ClassTranscriber.Api.Services;

public class ProjectService : IProjectService
{
    private readonly AppDbContext _db;
    private readonly IFileStorage _fileStorage;
    private readonly ILogger<ProjectService> _logger;

    public ProjectService(AppDbContext db, IFileStorage fileStorage, ILogger<ProjectService> logger)
    {
        _db = db;
        _fileStorage = fileStorage;
        _logger = logger;
    }

    public async Task<ProjectSummaryDto[]> ListAsync(string? folderId, string? status, string? search, string? sort, CancellationToken ct = default)
    {
        var query = _db.Projects.Include(p => p.Folder).AsQueryable();

        if (Guid.TryParse(folderId, out var fid))
            query = query.Where(p => p.FolderId == fid);

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<ProjectStatus>(status, true, out var ps))
            query = query.Where(p => p.Status == ps);

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(p => p.Name.Contains(search) || p.OriginalFileName.Contains(search));

        query = sort?.ToLowerInvariant() switch
        {
            "name" => query.OrderBy(p => p.Name),
            "status" => query.OrderBy(p => p.Status),
            _ => query.OrderByDescending(p => p.CreatedAtUtc),
        };

        return await query.Select(p => MapToSummary(p)).ToArrayAsync(ct);
    }

    public async Task<ProjectDetailDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var project = await _db.Projects
            .Include(p => p.Folder)
            .Include(p => p.Transcript)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

        return project is null ? null : MapToDetail(project);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var project = await _db.Projects.Include(p => p.Transcript).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (project is null)
            return false;

        // Clean up files
        if (!string.IsNullOrEmpty(project.MediaPath))
            await _fileStorage.DeleteFileAsync(project.MediaPath, ct);

        // Clean up prepared audio if exists
        var audioPath = Path.Combine(_fileStorage.GetAudioPath(), project.StoredFileName);
        if (!string.IsNullOrEmpty(project.StoredFileName))
        {
            var wavPath = Path.ChangeExtension(audioPath, ".wav");
            await _fileStorage.DeleteFileAsync(wavPath, ct);
        }

        if (project.Transcript is not null)
            _db.Transcripts.Remove(project.Transcript);

        _db.Projects.Remove(project);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Deleted project {ProjectId}", id);
        return true;
    }

    public async Task<ProjectDetailDto?> QueueAsync(Guid id, CancellationToken ct = default)
    {
        var project = await _db.Projects.Include(p => p.Folder).Include(p => p.Transcript).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (project is null)
            return null;

        if (project.Status != ProjectStatus.Draft)
            return null;

        project.Status = ProjectStatus.Queued;
        project.QueuedAtUtc = DateTime.UtcNow;
        project.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Queued project {ProjectId}", id);
        return MapToDetail(project);
    }

    public async Task<ProjectDetailDto?> RetryAsync(Guid id, CancellationToken ct = default)
    {
        var project = await _db.Projects.Include(p => p.Folder).Include(p => p.Transcript).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (project is null)
            return null;

        if (project.Status != ProjectStatus.Failed)
            return null;

        project.Status = ProjectStatus.Queued;
        project.Progress = 0;
        project.ErrorMessage = null;
        project.FailedAtUtc = null;
        project.QueuedAtUtc = DateTime.UtcNow;
        project.UpdatedAtUtc = DateTime.UtcNow;

        if (project.Transcript is not null)
        {
            _db.Transcripts.Remove(project.Transcript);
            project.Transcript = null;
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Retrying project {ProjectId}", id);
        return MapToDetail(project);
    }

    public async Task<ProjectDetailDto?> CancelAsync(Guid id, CancellationToken ct = default)
    {
        var project = await _db.Projects.Include(p => p.Folder).Include(p => p.Transcript).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (project is null)
            return null;

        if (project.Status != ProjectStatus.Queued)
            return null;

        project.Status = ProjectStatus.Cancelled;
        project.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Cancelled project {ProjectId}", id);
        return MapToDetail(project);
    }

    internal static ProjectSummaryDto MapToSummary(Project p) => new()
    {
        Id = p.Id.ToString(),
        FolderId = p.FolderId.ToString(),
        FolderName = p.Folder?.Name ?? "",
        Name = p.Name,
        OriginalFileName = p.OriginalFileName,
        Status = p.Status,
        Progress = p.Progress,
        MediaType = p.MediaType,
        DurationMs = p.DurationMs,
        TotalSizeBytes = p.TotalSizeBytes,
        CreatedAtUtc = p.CreatedAtUtc.ToString("O"),
        UpdatedAtUtc = p.UpdatedAtUtc.ToString("O"),
    };

    internal static ProjectDetailDto MapToDetail(Project p) => new()
    {
        Id = p.Id.ToString(),
        FolderId = p.FolderId.ToString(),
        FolderName = p.Folder?.Name ?? "",
        Name = p.Name,
        OriginalFileName = p.OriginalFileName,
        Status = p.Status,
        Progress = p.Progress,
        MediaType = p.MediaType,
        DurationMs = p.DurationMs,
        TotalSizeBytes = p.TotalSizeBytes,
        CreatedAtUtc = p.CreatedAtUtc.ToString("O"),
        UpdatedAtUtc = p.UpdatedAtUtc.ToString("O"),
        QueuedAtUtc = p.QueuedAtUtc?.ToString("O"),
        StartedAtUtc = p.StartedAtUtc?.ToString("O"),
        CompletedAtUtc = p.CompletedAtUtc?.ToString("O"),
        FailedAtUtc = p.FailedAtUtc?.ToString("O"),
        ErrorMessage = p.ErrorMessage,
        Settings = new ProjectSettingsDto
        {
            Engine = p.Settings.Engine,
            Model = p.Settings.Model,
            LanguageMode = p.Settings.LanguageMode,
            LanguageCode = p.Settings.LanguageCode,
            AudioNormalizationEnabled = p.Settings.AudioNormalizationEnabled,
            DiarizationEnabled = p.Settings.DiarizationEnabled,
        },
        MediaUrl = $"/api/projects/{p.Id}/media",
        TranscriptAvailable = p.Transcript is not null,
        AvailableExports = p.Transcript is not null ? ["txt", "md", "html", "pdf"] : [],
        OriginalFileSizeBytes = p.OriginalFileSizeBytes,
        WorkspaceSizeBytes = p.WorkspaceSizeBytes,
    };
}
