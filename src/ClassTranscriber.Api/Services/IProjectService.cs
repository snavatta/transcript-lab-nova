using ClassTranscriber.Api.Contracts;

namespace ClassTranscriber.Api.Services;

public interface IProjectService
{
    Task<ProjectSummaryDto[]> ListAsync(string? folderId, string? status, string? search, string? sort, CancellationToken ct = default);
    Task<ProjectDetailDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ProjectDetailDto?> UpdateAsync(Guid id, UpdateProjectRequest request, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    Task<ProjectDetailDto?> QueueAsync(Guid id, CancellationToken ct = default);
    Task<ProjectDetailDto?> RetryAsync(Guid id, RetryProjectRequest? request, CancellationToken ct = default);
    Task<ProjectDetailDto?> CancelAsync(Guid id, CancellationToken ct = default);
}
