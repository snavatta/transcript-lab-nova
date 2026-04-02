using ClassTranscriber.Api.Persistence;
using ClassTranscriber.Api.Services;
using ClassTranscriber.Api.Storage;
using Microsoft.EntityFrameworkCore;

namespace ClassTranscriber.Api.Endpoints;

public static class MediaEndpoints
{
    public static void MapMediaEndpoints(this WebApplication app)
    {
        app.MapGet("/api/projects/{id:guid}/media", async (Guid id, HttpContext httpContext, AppDbContext db, IFileStorage fileStorage, CancellationToken ct) =>
        {
            var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct);
            if (project is null)
                return Results.NotFound();

            if (string.IsNullOrEmpty(project.MediaPath) || !fileStorage.FileExists(project.MediaPath))
                return Results.NotFound();

            var fullPath = fileStorage.GetFullPath(project.MediaPath);
            var contentType = GetContentType(project.FileExtension);

            return Results.File(fullPath, contentType, enableRangeProcessing: true);
        })
        .WithName("GetMedia")
        .WithTags("Media");

        app.MapGet("/api/projects/{id:guid}/audio", async (Guid id, AppDbContext db, IFileStorage fileStorage, CancellationToken ct) =>
        {
            var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct);
            if (project is null)
                return Results.NotFound();

            var audioPreviewRelativePath = ProjectAudioFileResolver.TryGetAudioPreviewRelativePath(fileStorage, project);
            if (audioPreviewRelativePath is null)
                return Results.NotFound();

            var fullPath = fileStorage.GetFullPath(audioPreviewRelativePath);
            return Results.File(fullPath, "audio/wav", enableRangeProcessing: true);
        })
        .WithName("GetProjectAudioPreview")
        .WithTags("Media");
    }

    private static string GetContentType(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".mp3" => "audio/mpeg",
            ".wav" => "audio/wav",
            ".m4a" => "audio/mp4",
            ".flac" => "audio/flac",
            ".ogg" => "audio/ogg",
            ".mp4" => "video/mp4",
            ".mkv" => "video/x-matroska",
            ".mov" => "video/quicktime",
            ".webm" => "video/webm",
            _ => "application/octet-stream",
        };
    }
}
