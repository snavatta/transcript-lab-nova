using ClassTranscriber.Api.Contracts;
using ClassTranscriber.Api.Services;

namespace ClassTranscriber.Api.Endpoints;

public static class ExportEndpoints
{
    public static void MapExportEndpoints(this WebApplication app)
    {
        app.MapGet("/api/projects/{id:guid}/export", async (Guid id, string? format, string? viewMode, bool? includeTimestamps,
            IExportService exportService, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(format))
                return Results.BadRequest(new ErrorResponse("validation_error", "format query parameter is required."));

            var validFormats = new[] { "txt", "md", "html", "pdf" };
            if (!validFormats.Contains(format.ToLowerInvariant()))
                return Results.BadRequest(new ErrorResponse("validation_error", $"Unsupported format: {format}"));

            var result = await exportService.GenerateAsync(id, format, viewMode, includeTimestamps, ct);
            if (result is null)
                return Results.Conflict(new ErrorResponse("transcript_not_available", "Transcript is not available for export."));

            return Results.File(result.Content, result.ContentType, result.FileName);
        })
        .WithName("ExportTranscript")
        .WithTags("Exports");
    }
}
