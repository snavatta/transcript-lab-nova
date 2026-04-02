using ClassTranscriber.Api.Contracts;
using ClassTranscriber.Api.Persistence;
using ClassTranscriber.Api.Storage;
using Microsoft.EntityFrameworkCore;

namespace ClassTranscriber.Api.Endpoints;

public static class TranscriptEndpoints
{
    public static void MapTranscriptEndpoints(this WebApplication app)
    {
        app.MapGet("/api/projects/{id:guid}/transcript", async (Guid id, AppDbContext db, CancellationToken ct) =>
        {
            var project = await db.Projects.Include(p => p.Transcript).FirstOrDefaultAsync(p => p.Id == id, ct);
            if (project is null)
                return Results.NotFound();

            if (project.Transcript is null)
                return Results.Conflict(new ErrorResponse("transcript_not_available", "Transcript is not available yet."));

            var segments = System.Text.Json.JsonSerializer.Deserialize<TranscriptSegmentDto[]>(
                project.Transcript.StructuredSegmentsJson,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];

            return Results.Ok(new TranscriptDto
            {
                ProjectId = project.Id.ToString(),
                PlainText = project.Transcript.PlainText,
                DetectedLanguage = project.Transcript.DetectedLanguage,
                DurationMs = project.Transcript.DurationMs,
                SegmentCount = project.Transcript.SegmentCount,
                Segments = segments,
                CreatedAtUtc = project.Transcript.CreatedAtUtc.ToString("O"),
                UpdatedAtUtc = project.Transcript.UpdatedAtUtc.ToString("O"),
            });
        })
        .WithName("GetTranscript")
        .WithTags("Transcripts");
    }
}
