using ClassTranscriber.Api.Services;

namespace ClassTranscriber.Api.Endpoints;

public static class DiagnosticsEndpoints
{
    public static void MapDiagnosticsEndpoints(this WebApplication app)
    {
        app.MapGet("/api/diagnostics", async (IDiagnosticsService service, CancellationToken ct) =>
        {
            var diagnostics = await service.GetAsync(ct);
            return Results.Ok(diagnostics);
        })
        .WithName("GetDiagnostics")
        .WithTags("Diagnostics");
    }
}
