using ClassTranscriber.Api.Services;

namespace ClassTranscriber.Api.Endpoints;

public static class QueueEndpoints
{
    public static void MapQueueEndpoints(this WebApplication app)
    {
        app.MapGet("/api/queue", async (IQueueService service, CancellationToken ct) =>
        {
            var overview = await service.GetOverviewAsync(ct);
            return Results.Ok(overview);
        })
        .WithName("GetQueueOverview")
        .WithTags("Queue");
    }
}
