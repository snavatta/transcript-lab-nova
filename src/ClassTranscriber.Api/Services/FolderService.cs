using ClassTranscriber.Api.Contracts;
using ClassTranscriber.Api.Domain;
using ClassTranscriber.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClassTranscriber.Api.Services;

public class FolderService : IFolderService
{
    private readonly AppDbContext _db;
    private readonly ILogger<FolderService> _logger;

    public FolderService(AppDbContext db, ILogger<FolderService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<FolderSummaryDto[]> ListAsync(CancellationToken ct = default)
    {
        return await _db.Folders
            .Select(f => new FolderSummaryDto
            {
                Id = f.Id.ToString(),
                Name = f.Name,
                IconKey = f.IconKey,
                ColorHex = f.ColorHex,
                ProjectCount = f.Projects.Count,
                TotalSizeBytes = f.Projects.Sum(p => p.TotalSizeBytes),
                CreatedAtUtc = f.CreatedAtUtc.ToString("O"),
                UpdatedAtUtc = f.UpdatedAtUtc.ToString("O"),
            })
            .ToArrayAsync(ct);
    }

    public async Task<FolderDetailDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.Folders
            .Where(f => f.Id == id)
            .Select(f => new FolderDetailDto
            {
                Id = f.Id.ToString(),
                Name = f.Name,
                IconKey = f.IconKey,
                ColorHex = f.ColorHex,
                ProjectCount = f.Projects.Count,
                TotalSizeBytes = f.Projects.Sum(p => p.TotalSizeBytes),
                CreatedAtUtc = f.CreatedAtUtc.ToString("O"),
                UpdatedAtUtc = f.UpdatedAtUtc.ToString("O"),
            })
            .FirstOrDefaultAsync(ct);
    }

    public async Task<FolderDetailDto> CreateAsync(CreateFolderRequest request, CancellationToken ct = default)
    {
        var folder = new Folder
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            IconKey = FolderAppearance.ResolveIconKeyOrDefault(request.IconKey),
            ColorHex = FolderAppearance.ResolveColorHexOrDefault(request.ColorHex),
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };

        _db.Folders.Add(folder);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Created folder {FolderId} with name {FolderName}", folder.Id, folder.Name);

        return new FolderDetailDto
        {
            Id = folder.Id.ToString(),
            Name = folder.Name,
            IconKey = folder.IconKey,
            ColorHex = folder.ColorHex,
            ProjectCount = 0,
            TotalSizeBytes = 0,
            CreatedAtUtc = folder.CreatedAtUtc.ToString("O"),
            UpdatedAtUtc = folder.UpdatedAtUtc.ToString("O"),
        };
    }

    public async Task<FolderSummaryDto?> UpdateAsync(Guid id, UpdateFolderRequest request, CancellationToken ct = default)
    {
        var folder = await _db.Folders.Include(f => f.Projects).FirstOrDefaultAsync(f => f.Id == id, ct);
        if (folder is null)
            return null;

        folder.Name = request.Name.Trim();
        folder.IconKey = FolderAppearance.ResolveIconKeyOrDefault(request.IconKey);
        folder.ColorHex = FolderAppearance.ResolveColorHexOrDefault(request.ColorHex);
        folder.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Updated folder {FolderId} to name {FolderName} with icon {FolderIconKey} and color {FolderColorHex}",
            folder.Id,
            folder.Name,
            folder.IconKey,
            folder.ColorHex);

        return new FolderSummaryDto
        {
            Id = folder.Id.ToString(),
            Name = folder.Name,
            IconKey = folder.IconKey,
            ColorHex = folder.ColorHex,
            ProjectCount = folder.Projects.Count,
            TotalSizeBytes = folder.Projects.Sum(p => p.TotalSizeBytes),
            CreatedAtUtc = folder.CreatedAtUtc.ToString("O"),
            UpdatedAtUtc = folder.UpdatedAtUtc.ToString("O"),
        };
    }

    public async Task<(bool Success, string? Error)> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var folder = await _db.Folders.Include(f => f.Projects).FirstOrDefaultAsync(f => f.Id == id, ct);
        if (folder is null)
            return (false, "not_found");

        if (folder.Projects.Count > 0)
            return (false, "folder_not_empty");

        _db.Folders.Remove(folder);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Deleted folder {FolderId}", folder.Id);
        return (true, null);
    }
}
