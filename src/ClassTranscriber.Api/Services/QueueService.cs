using ClassTranscriber.Api.Contracts;
using ClassTranscriber.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClassTranscriber.Api.Services;

public class QueueService : IQueueService
{
    private readonly AppDbContext _db;

    public QueueService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<QueueOverviewDto> GetOverviewAsync(CancellationToken ct = default)
    {
        var projects = await _db.Projects
            .Include(p => p.Folder)
            .Where(p => p.Status == ProjectStatus.Draft
                     || p.Status == ProjectStatus.Queued
                     || p.Status == ProjectStatus.PreparingMedia
                     || p.Status == ProjectStatus.Transcribing
                     || p.Status == ProjectStatus.Completed
                     || p.Status == ProjectStatus.Failed)
            .OrderByDescending(p => p.UpdatedAtUtc)
            .ToListAsync(ct);

        QueueItemDto MapToQueueItem(Domain.Project p) => new()
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
            Engine = p.Settings.Engine,
            Model = p.Settings.Model,
            CreatedAtUtc = p.CreatedAtUtc.ToString("O"),
            UpdatedAtUtc = p.UpdatedAtUtc.ToString("O"),
        };

        return new QueueOverviewDto
        {
            Drafts = projects.Where(p => p.Status == ProjectStatus.Draft).Select(MapToQueueItem).ToArray(),
            Queued = projects.Where(p => p.Status == ProjectStatus.Queued).Select(MapToQueueItem).ToArray(),
            Processing = projects.Where(p => p.Status is ProjectStatus.PreparingMedia or ProjectStatus.Transcribing).Select(MapToQueueItem).ToArray(),
            Completed = projects.Where(p => p.Status == ProjectStatus.Completed).Take(20).Select(MapToQueueItem).ToArray(),
            Failed = projects.Where(p => p.Status == ProjectStatus.Failed).Select(MapToQueueItem).ToArray(),
        };
    }
}
