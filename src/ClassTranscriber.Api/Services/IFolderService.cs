using ClassTranscriber.Api.Contracts;

namespace ClassTranscriber.Api.Services;

public interface IFolderService
{
    Task<FolderSummaryDto[]> ListAsync(CancellationToken ct = default);
    Task<FolderDetailDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<FolderDetailDto> CreateAsync(CreateFolderRequest request, CancellationToken ct = default);
    Task<FolderSummaryDto?> UpdateAsync(Guid id, UpdateFolderRequest request, CancellationToken ct = default);
    Task<(bool Success, string? Error)> DeleteAsync(Guid id, CancellationToken ct = default);
}
