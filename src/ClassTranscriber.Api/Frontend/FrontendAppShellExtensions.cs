using Microsoft.AspNetCore.Http;

namespace ClassTranscriber.Api.Frontend;

public static class FrontendAppShellExtensions
{
    public static void UseFrontendAppShellAssets(this WebApplication app)
    {
        app.UseDefaultFiles();
        app.UseStaticFiles();
    }

    public static void MapFrontendAppShellFallback(this WebApplication app)
    {
        app.MapFallback(async context =>
        {
            if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
                || context.Request.Path.StartsWithSegments("/swagger", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            var webRootPath = app.Environment.WebRootPath;
            if (string.IsNullOrWhiteSpace(webRootPath))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            var indexFilePath = Path.Combine(webRootPath, "index.html");
            if (!File.Exists(indexFilePath))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.SendFileAsync(indexFilePath);
        });
    }
}
