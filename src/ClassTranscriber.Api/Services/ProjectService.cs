using ClassTranscriber.Api.Contracts;
using ClassTranscriber.Api.Domain;
using ClassTranscriber.Api.Jobs;
using ClassTranscriber.Api.Persistence;
using ClassTranscriber.Api.Storage;
using ClassTranscriber.Api.Transcription;
using Microsoft.EntityFrameworkCore;

namespace ClassTranscriber.Api.Services;

public class ProjectService : IProjectService
{
    private readonly AppDbContext _db;
    private readonly IFileStorage _fileStorage;
    private readonly IActiveJobCancellation _activeJobCancellation;
    private readonly ITranscriptionEngineRegistry _engineRegistry;
    private readonly ILogger<ProjectService> _logger;

    public ProjectService(
        AppDbContext db,
        IFileStorage fileStorage,
        IActiveJobCancellation activeJobCancellation,
        ITranscriptionEngineRegistry engineRegistry,
        ILogger<ProjectService> logger)
    {
        _db = db;
        _fileStorage = fileStorage;
        _activeJobCancellation = activeJobCancellation;
        _engineRegistry = engineRegistry;
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

    public async Task<ProjectDetailDto?> UpdateAsync(Guid id, UpdateProjectRequest request, CancellationToken ct = default)
    {
        var project = await _db.Projects
            .Include(p => p.Folder)
            .Include(p => p.Transcript)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (project is null)
            return null;

        project.Name = request.Name;
        project.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Updated project {ProjectId}", id);
        return MapToDetail(project);
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
        foreach (var relativePath in ProjectAudioFileResolver.GetExistingWorkspaceAudioRelativePaths(_fileStorage, project))
            await _fileStorage.DeleteFileAsync(relativePath, ct);

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

    public async Task<ProjectDetailDto?> RetryAsync(Guid id, RetryProjectRequest? request, CancellationToken ct = default)
    {
        var project = await _db.Projects.Include(p => p.Folder).Include(p => p.Transcript).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (project is null)
            return null;

        if (project.Status != ProjectStatus.Failed)
            return null;

        if (request?.Settings is not null)
        {
            var normalizedSettings = NormalizeAndValidateSettings(request.Settings);
            project.Settings = new ProjectSettings
            {
                Engine = normalizedSettings.Engine,
                Model = normalizedSettings.Model,
                LanguageMode = normalizedSettings.LanguageMode,
                LanguageCode = normalizedSettings.LanguageCode,
                AudioNormalizationEnabled = normalizedSettings.AudioNormalizationEnabled,
                DiarizationEnabled = normalizedSettings.DiarizationEnabled,
            };
        }

        project.Status = ProjectStatus.Queued;
        project.Progress = 0;
        project.ErrorMessage = null;
        project.FailedAtUtc = null;
        project.TranscriptionElapsedMs = null;
        project.TotalProcessingElapsedMs = null;
        project.MediaInspectionElapsedMs = null;
        project.AudioExtractionElapsedMs = null;
        project.AudioNormalizationElapsedMs = null;
        project.ResultPersistenceElapsedMs = null;
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

        if (project.Status == ProjectStatus.Queued)
        {
            project.Status = ProjectStatus.Cancelled;
            project.UpdatedAtUtc = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Cancelled queued project {ProjectId}", id);
            return MapToDetail(project);
        }

        if (project.Status is ProjectStatus.PreparingMedia or ProjectStatus.Transcribing)
        {
            // Signal the worker to cancel the active job; the worker will
            // set the final Cancelled status when the processing loop exits.
            if (_activeJobCancellation.TryCancel(id))
            {
                _logger.LogInformation("Requested cancellation of active project {ProjectId}", id);

                // Return the current state — the worker will transition to
                // Cancelled asynchronously.  Give it a brief moment so the
                // caller ideally gets back the updated status.
                await Task.Delay(250, ct);

                // Re-read to pick up any status change the worker already committed.
                await _db.Entry(project).ReloadAsync(ct);
                return MapToDetail(project);
            }

            // Active job not tracked (race / already finishing) — force status.
            project.Status = ProjectStatus.Cancelled;
            project.UpdatedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Force-cancelled project {ProjectId} (not found in active jobs)", id);
            return MapToDetail(project);
        }

        // Not in a cancellable state
        return null;
    }

    internal static ProjectSummaryDto MapToSummary(Project p) => new()
    {
        Id = p.Id.ToString(),
        FolderId = p.FolderId.ToString(),
        FolderName = p.Folder?.Name ?? "",
        Name = p.Name,
        OriginalFileName = p.OriginalFileName,
        Status = p.Status,
        Progress = MapProgress(p),
        MediaType = p.MediaType,
        DurationMs = p.DurationMs,
        TranscriptionElapsedMs = p.TranscriptionElapsedMs,
        TotalSizeBytes = p.TotalSizeBytes,
        CreatedAtUtc = p.CreatedAtUtc.ToString("O"),
        UpdatedAtUtc = p.UpdatedAtUtc.ToString("O"),
    };

    internal ProjectDetailDto MapToDetail(Project p)
    {
        var audioPreviewRelativePath = ProjectAudioFileResolver.TryGetAudioPreviewRelativePath(_fileStorage, p);

        return new ProjectDetailDto
        {
            Id = p.Id.ToString(),
            FolderId = p.FolderId.ToString(),
            FolderName = p.Folder?.Name ?? "",
            Name = p.Name,
            OriginalFileName = p.OriginalFileName,
            Status = p.Status,
            Progress = MapProgress(p),
            MediaType = p.MediaType,
            DurationMs = p.DurationMs,
            TranscriptionElapsedMs = p.TranscriptionElapsedMs,
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
            AudioPreviewUrl = audioPreviewRelativePath is not null ? $"/api/projects/{p.Id}/audio" : null,
            TranscriptAvailable = p.Transcript is not null,
            AvailableExports = p.Transcript is not null ? ["txt", "md", "html", "pdf"] : [],
            OriginalFileSizeBytes = p.OriginalFileSizeBytes,
            WorkspaceSizeBytes = p.WorkspaceSizeBytes,
            DebugTimings = MapDebugTimings(p),
        };
    }

    private static int? MapProgress(Project project)
        => project.Status is ProjectStatus.PreparingMedia or ProjectStatus.Transcribing
            ? null
            : project.Progress;

    private static ProjectDebugTimingsDto? MapDebugTimings(Project project)
    {
        if (project.TotalProcessingElapsedMs is null
            && project.MediaInspectionElapsedMs is null
            && project.AudioExtractionElapsedMs is null
            && project.AudioNormalizationElapsedMs is null
            && project.TranscriptionElapsedMs is null
            && project.ResultPersistenceElapsedMs is null)
            return null;

        var preparationElapsedMs =
            (project.MediaInspectionElapsedMs ?? 0)
            + (project.AudioExtractionElapsedMs ?? 0)
            + (project.AudioNormalizationElapsedMs ?? 0);

        return new ProjectDebugTimingsDto
        {
            TotalElapsedMs = project.TotalProcessingElapsedMs,
            PreparationElapsedMs = preparationElapsedMs == 0 ? null : preparationElapsedMs,
            InspectElapsedMs = project.MediaInspectionElapsedMs,
            ExtractElapsedMs = project.AudioExtractionElapsedMs,
            NormalizeElapsedMs = project.AudioNormalizationElapsedMs,
            TranscriptionElapsedMs = project.TranscriptionElapsedMs,
            PersistElapsedMs = project.ResultPersistenceElapsedMs,
            TranscriptionRealtimeFactor = CalculateRealtimeFactor(project.TranscriptionElapsedMs, project.DurationMs),
            TotalRealtimeFactor = CalculateRealtimeFactor(project.TotalProcessingElapsedMs, project.DurationMs),
        };
    }

    private static double? CalculateRealtimeFactor(long? elapsedMs, long? audioDurationMs)
    {
        if (elapsedMs is null || audioDurationMs is null || audioDurationMs <= 0)
            return null;

        return Math.Round((double)elapsedMs.Value / audioDurationMs.Value, 2);
    }

    private ProjectSettingsDto NormalizeAndValidateSettings(ProjectSettingsDto settings)
    {
        var normalized = new ProjectSettingsDto
        {
            Engine = settings.Engine.Trim(),
            Model = settings.Model.Trim(),
            LanguageMode = NormalizeLanguageMode(settings.LanguageMode),
            LanguageCode = NormalizeLanguageCode(settings.LanguageCode),
            AudioNormalizationEnabled = settings.AudioNormalizationEnabled,
            DiarizationEnabled = settings.DiarizationEnabled,
        };

        if (string.IsNullOrWhiteSpace(normalized.Engine) || !_engineRegistry.IsSupportedEngine(normalized.Engine))
            throw new ArgumentException("Unsupported engine.");

        if (string.IsNullOrWhiteSpace(normalized.Model) || !_engineRegistry.IsSupportedModel(normalized.Engine, normalized.Model))
        {
            var supportedModels = string.Join(", ", _engineRegistry.GetSupportedModels(normalized.Engine));
            throw new ArgumentException($"Unsupported model for engine {normalized.Engine}. Supported models: {supportedModels}.");
        }

        if (normalized.LanguageMode is not ("Auto" or "Fixed"))
            throw new ArgumentException("Invalid language mode.");

        if (normalized.LanguageMode == "Fixed" && string.IsNullOrWhiteSpace(normalized.LanguageCode))
            throw new ArgumentException("Language code is required when language mode is Fixed.");

        if (normalized.LanguageMode == "Fixed"
            && !TranscriptionLanguageCatalog.IsSupportedFixedLanguage(normalized.Engine, normalized.LanguageCode))
        {
            var supportedLanguages = string.Join(", ", TranscriptionLanguageCatalog.GetSupportedFixedLanguages(normalized.Engine));
            throw new ArgumentException(
                supportedLanguages.Length == 0
                    ? "Unsupported fixed language for engine."
                    : $"Unsupported fixed language for engine {normalized.Engine}. Supported fixed languages: {supportedLanguages}.");
        }

        return normalized with
        {
            LanguageCode = normalized.LanguageMode == "Fixed" ? normalized.LanguageCode : null,
        };
    }

    private static string NormalizeLanguageMode(string? languageMode)
        => string.Equals(languageMode?.Trim(), "Fixed", StringComparison.OrdinalIgnoreCase) ? "Fixed" : "Auto";

    private static string? NormalizeLanguageCode(string? languageCode)
    {
        var trimmed = languageCode?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
