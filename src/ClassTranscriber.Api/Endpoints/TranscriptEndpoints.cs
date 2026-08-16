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
                HostedProcessing = MapHostedProcessing(project.Transcript),
            });
        })
        .WithName("GetTranscript")
        .WithTags("Transcripts");
    }

    private static HostedProcessingMetadataDto? MapHostedProcessing(Domain.Transcript transcript)
    {
        if (transcript.HostedSttProvider is null
            && transcript.HostedDiarizationProvider is null
            && transcript.SpeakerRoleAttributionStatus is null)
            return null;

        var totalCostMicroUsd = CheckedTotalMicroUsd(
            transcript.HostedSttCostMicroUsd,
            transcript.HostedDiarizationCostMicroUsd,
            transcript.SpeakerRoleCostMicroUsd);
        return new HostedProcessingMetadataDto
        {
            SttProvider = transcript.HostedSttProvider ?? "Local",
            SttModel = transcript.HostedSttModel ?? string.Empty,
            AudioDurationMs = transcript.DurationMs,
            RequestCount = transcript.HostedRequestCount ?? 0,
            NativeDiarizationUsed = transcript.NativeDiarizationUsed == true,
            SttCostUsd = ToUsd(transcript.HostedSttCostMicroUsd),
            SttRateUsdPerHour = ToUsd(transcript.HostedSttRateMicroUsdPerHour),
            SttCostClassification = transcript.HostedSttCostClassification,
            DiarizationSource = transcript.DiarizationSource,
            DiarizationProvider = transcript.HostedDiarizationProvider,
            DiarizationModel = transcript.HostedDiarizationModel,
            DiarizationRequestCount = transcript.HostedDiarizationRequestCount ?? 0,
            DiarizationCostUsd = ToUsd(transcript.HostedDiarizationCostMicroUsd),
            DiarizationRateUsdPerHour = ToUsd(transcript.HostedDiarizationRateMicroUsdPerHour),
            DiarizationCostClassification = transcript.HostedDiarizationCostClassification,
            RoleAttributionModel = transcript.SpeakerRoleAttributionModel,
            RoleAttributionStatus = transcript.SpeakerRoleAttributionStatus,
            RoleAttributionPromptTokens = transcript.SpeakerRolePromptTokens,
            RoleAttributionOutputTokens = transcript.SpeakerRoleOutputTokens,
            RoleAttributionCostUsd = ToUsd(transcript.SpeakerRoleCostMicroUsd),
            TotalCostUsd = ToUsd(totalCostMicroUsd),
            TotalContainsEstimate = IsEstimated(transcript.HostedSttCostClassification)
                || IsEstimated(transcript.HostedDiarizationCostClassification),
        };
    }

    private static long? CheckedTotalMicroUsd(params long?[] costs)
    {
        if (costs.All(cost => cost is null))
            return null;

        long total = 0;
        foreach (var cost in costs)
            total = checked(total + (cost ?? 0));
        return total;
    }

    private static bool IsEstimated(string? classification)
        => string.Equals(classification, "Estimated", StringComparison.OrdinalIgnoreCase);

    private static decimal? ToUsd(long? microUsd) => microUsd is null ? null : microUsd.Value / 1_000_000m;
}
