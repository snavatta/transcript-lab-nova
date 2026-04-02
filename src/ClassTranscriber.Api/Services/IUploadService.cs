using ClassTranscriber.Api.Contracts;

namespace ClassTranscriber.Api.Services;

public interface IUploadService
{
    Task<BatchUploadResultDto> BatchUploadAsync(
        Guid folderId,
        bool autoQueue,
        ProjectSettingsDto? settingsOverride,
        IReadOnlyList<IFormFile> files,
        IReadOnlyList<UploadItemMetadata> items,
        CancellationToken ct = default);
}

public record UploadItemMetadata(string? OriginalFileName, string? ProjectName);
