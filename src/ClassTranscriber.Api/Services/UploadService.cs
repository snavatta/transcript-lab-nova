using System.Text.Json;
using ClassTranscriber.Api.Contracts;
using ClassTranscriber.Api.Domain;
using ClassTranscriber.Api.Persistence;
using ClassTranscriber.Api.Storage;
using Microsoft.EntityFrameworkCore;

namespace ClassTranscriber.Api.Services;

public class UploadService : IUploadService
{
    private readonly AppDbContext _db;
    private readonly IFileStorage _fileStorage;
    private readonly ILogger<UploadService> _logger;

    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".mp3", ".wav", ".m4a", ".flac", ".ogg" };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".mp4", ".mkv", ".mov", ".webm" };

    public UploadService(AppDbContext db, IFileStorage fileStorage, ILogger<UploadService> logger)
    {
        _db = db;
        _fileStorage = fileStorage;
        _logger = logger;
    }

    public async Task<BatchUploadResultDto> BatchUploadAsync(
        Guid folderId,
        bool autoQueue,
        ProjectSettingsDto settings,
        IReadOnlyList<IFormFile> files,
        IReadOnlyList<UploadItemMetadata> items,
        CancellationToken ct = default)
    {
        var folder = await _db.Folders.FirstOrDefaultAsync(f => f.Id == folderId, ct)
            ?? throw new ArgumentException("Folder not found.");

        var projects = new List<Project>();
        var now = DateTime.UtcNow;

        for (var i = 0; i < files.Count; i++)
        {
            var file = files[i];
            var itemMeta = i < items.Count ? items[i] : new UploadItemMetadata(file.FileName, null);

            var originalFileName = SanitizeFileName(itemMeta.OriginalFileName ?? file.FileName);
            var extension = Path.GetExtension(originalFileName).ToLowerInvariant();
            var storedFileName = _fileStorage.GenerateSafeFileName(originalFileName);
            var mediaType = DetectMediaType(extension);
            var projectName = !string.IsNullOrWhiteSpace(itemMeta.ProjectName)
                ? itemMeta.ProjectName.Trim()
                : Path.GetFileNameWithoutExtension(originalFileName);

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
                    Engine = settings.Engine,
                    Model = settings.Model,
                    LanguageMode = settings.LanguageMode,
                    LanguageCode = settings.LanguageCode,
                    AudioNormalizationEnabled = settings.AudioNormalizationEnabled,
                    DiarizationEnabled = settings.DiarizationEnabled,
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
}
