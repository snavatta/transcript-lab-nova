using ClassTranscriber.Api.Contracts;
using ClassTranscriber.Api.Domain;
using ClassTranscriber.Api.Persistence;
using ClassTranscriber.Api.Storage;
using ClassTranscriber.Api.Transcription;
using Microsoft.EntityFrameworkCore;

namespace ClassTranscriber.Api.Services;

public class UploadService : IUploadService
{
    private readonly AppDbContext _db;
    private readonly IFileStorage _fileStorage;
    private readonly ITranscriptionEngineRegistry _engineRegistry;
    private readonly ILogger<UploadService> _logger;

    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".mp3", ".wav", ".m4a", ".flac", ".ogg" };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".mp4", ".mkv", ".mov", ".webm" };

    private static readonly HashSet<string> ValidLanguageModes = new(StringComparer.OrdinalIgnoreCase)
        { "Auto", "Fixed" };

    public UploadService(
        AppDbContext db,
        IFileStorage fileStorage,
        ITranscriptionEngineRegistry engineRegistry,
        ILogger<UploadService> logger)
    {
        _db = db;
        _fileStorage = fileStorage;
        _engineRegistry = engineRegistry;
        _logger = logger;
    }

    public async Task<BatchUploadResultDto> BatchUploadAsync(
        Guid folderId,
        bool autoQueue,
        ProjectSettingsDto? settingsOverride,
        IReadOnlyList<IFormFile> files,
        IReadOnlyList<UploadItemMetadata> items,
        CancellationToken ct = default)
    {
        var folder = await _db.Folders.FirstOrDefaultAsync(f => f.Id == folderId, ct)
            ?? throw new KeyNotFoundException("Folder not found.");
        var effectiveSettings = await ResolveEffectiveSettingsAsync(settingsOverride, ct);

        var projects = new List<Project>();
        var now = DateTime.UtcNow;

        for (var i = 0; i < files.Count; i++)
        {
            var file = files[i];
            if (file.Length <= 0)
                throw new ArgumentException($"File '{file.FileName}' is empty.");

            var itemMeta = i < items.Count ? items[i] : new UploadItemMetadata(file.FileName, null);
            var originalFileName = SanitizeFileName(
                string.IsNullOrWhiteSpace(itemMeta.OriginalFileName)
                    ? file.FileName
                    : itemMeta.OriginalFileName);
            var extension = Path.GetExtension(originalFileName).ToLowerInvariant();
            var storedFileName = _fileStorage.GenerateSafeFileName(originalFileName);
            var mediaType = DetectMediaType(extension);
            var projectName = ResolveProjectName(itemMeta.ProjectName, originalFileName);

            var relativePath = Path.Combine(_fileStorage.GetUploadsPath(), storedFileName);
            await using var stream = file.OpenReadStream();
            await _fileStorage.SaveFileAsync(relativePath, stream, ct);

            var project = new Project
            {
                Id = Guid.NewGuid(),
                FolderId = folderId,
                Name = projectName,
                OriginalFileName = originalFileName,
                StoredFileName = storedFileName,
                MediaType = mediaType,
                FileExtension = extension,
                MediaPath = relativePath,
                Status = autoQueue ? ProjectStatus.Queued : ProjectStatus.Draft,
                Progress = 0,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                QueuedAtUtc = autoQueue ? now : null,
                OriginalFileSizeBytes = file.Length,
                TotalSizeBytes = file.Length,
                Settings = new ProjectSettings
                {
                    Engine = effectiveSettings.Engine,
                    Model = effectiveSettings.Model,
                    LanguageMode = effectiveSettings.LanguageMode,
                    LanguageCode = effectiveSettings.LanguageCode,
                    AudioNormalizationEnabled = effectiveSettings.AudioNormalizationEnabled,
                    DiarizationEnabled = effectiveSettings.DiarizationEnabled,
                },
            };

            _db.Projects.Add(project);
            projects.Add(project);
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Batch uploaded {FileCount} files into folder {FolderId}", files.Count, folderId);

        // Reload with folder for mapping
        foreach (var p in projects)
            p.Folder = folder;

        return new BatchUploadResultDto
        {
            FolderId = folderId.ToString(),
            CreatedProjects = projects.Select(ProjectService.MapToSummary).ToArray(),
        };
    }

    private static MediaType DetectMediaType(string extension)
    {
        if (AudioExtensions.Contains(extension)) return Contracts.MediaType.Audio;
        if (VideoExtensions.Contains(extension)) return Contracts.MediaType.Video;
        return Contracts.MediaType.Unknown;
    }

    private static string SanitizeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(name))
            name = "unnamed";
        return name;
    }

    private async Task<ProjectSettingsDto> ResolveEffectiveSettingsAsync(ProjectSettingsDto? settingsOverride, CancellationToken ct)
    {
        var effectiveSettings = settingsOverride ?? await GetDefaultProjectSettingsAsync(ct);
        ValidateSettings(effectiveSettings);

        return new ProjectSettingsDto
        {
            Engine = effectiveSettings.Engine.Trim(),
            Model = effectiveSettings.Model.Trim(),
            LanguageMode = NormalizeLanguageMode(effectiveSettings.LanguageMode),
            LanguageCode = string.IsNullOrWhiteSpace(effectiveSettings.LanguageCode) ? null : effectiveSettings.LanguageCode.Trim(),
            AudioNormalizationEnabled = effectiveSettings.AudioNormalizationEnabled,
            DiarizationEnabled = effectiveSettings.DiarizationEnabled,
        };
    }

    private async Task<ProjectSettingsDto> GetDefaultProjectSettingsAsync(CancellationToken ct)
    {
        var defaults = await _db.GlobalSettings.AsNoTracking().SingleAsync(ct);
        var engine = TranscriptionSettingsDefaults.ResolveSupportedEngine(_engineRegistry, defaults.DefaultEngine);
        var model = TranscriptionSettingsDefaults.ResolveSupportedModel(_engineRegistry, engine, defaults.DefaultModel);
        var (languageMode, languageCode) = TranscriptionSettingsDefaults.ResolveSupportedLanguage(
            engine,
            defaults.DefaultLanguageMode,
            defaults.DefaultLanguageCode);

        return new ProjectSettingsDto
        {
            Engine = engine,
            Model = model,
            LanguageMode = languageMode,
            LanguageCode = languageCode,
            AudioNormalizationEnabled = defaults.DefaultAudioNormalizationEnabled,
            DiarizationEnabled = defaults.DefaultDiarizationEnabled,
        };
    }

    private void ValidateSettings(ProjectSettingsDto settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Engine) || !_engineRegistry.IsSupportedEngine(settings.Engine.Trim()))
            throw new ArgumentException("Unsupported engine.");

        if (string.IsNullOrWhiteSpace(settings.Model) || !_engineRegistry.IsSupportedModel(settings.Engine.Trim(), settings.Model.Trim()))
        {
            var supportedModels = string.Join(", ", _engineRegistry.GetSupportedModels(settings.Engine.Trim()));
            throw new ArgumentException($"Unsupported model for engine {settings.Engine.Trim()}. Supported models: {supportedModels}.");
        }

        if (string.IsNullOrWhiteSpace(settings.LanguageMode) || !ValidLanguageModes.Contains(settings.LanguageMode.Trim()))
            throw new ArgumentException("Invalid language mode.");

        if (NormalizeLanguageMode(settings.LanguageMode) == "Fixed" && string.IsNullOrWhiteSpace(settings.LanguageCode))
            throw new ArgumentException("Language code is required when language mode is Fixed.");

        if (NormalizeLanguageMode(settings.LanguageMode) == "Fixed"
            && !TranscriptionLanguageCatalog.IsSupportedFixedLanguage(settings.Engine.Trim(), settings.LanguageCode))
        {
            var supportedLanguages = string.Join(", ", TranscriptionLanguageCatalog.GetSupportedFixedLanguages(settings.Engine.Trim()));
            throw new ArgumentException(
                supportedLanguages.Length == 0
                    ? "Unsupported fixed language for engine."
                    : $"Unsupported fixed language for engine {settings.Engine.Trim()}. Supported fixed languages: {supportedLanguages}.");
        }
    }

    private static string NormalizeLanguageMode(string languageMode)
        => languageMode.Trim().Equals("Fixed", StringComparison.OrdinalIgnoreCase) ? "Fixed" : "Auto";

    private static string ResolveProjectName(string? requestedProjectName, string originalFileName)
    {
        if (requestedProjectName is null)
            return Path.GetFileNameWithoutExtension(originalFileName);

        var trimmed = requestedProjectName.Trim();
        if (trimmed.Length == 0)
            throw new ArgumentException("Project name overrides must be non-empty.");

        return trimmed;
    }
}
