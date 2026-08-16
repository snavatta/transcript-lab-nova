using ClassTranscriber.Api.Services;

namespace ClassTranscriber.Api.Endpoints;

public static class SystemCapabilitiesEndpoints
{
    public static void MapSystemCapabilitiesEndpoints(this WebApplication app)
    {
        app.MapGet("/api/settings/capabilities", async (
            ISystemCapabilitiesService service,
            CancellationToken ct) => Results.Ok(await service.GetAsync(ct)))
            .WithName("GetSystemCapabilities")
            .WithTags("Settings");
    }
}
