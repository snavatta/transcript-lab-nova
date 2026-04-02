using ClassTranscriber.Api.Contracts;
using ClassTranscriber.Api.Services;
using ClassTranscriber.Api.Transcription;

namespace ClassTranscriber.Api.Endpoints;

public static class SettingsEndpoints
{
    public static void MapSettingsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/settings").WithTags("Settings");

        group.MapGet("/options", (ITranscriptionEngineRegistry engineRegistry) =>
        {
            var options = new TranscriptionOptionsDto
            {
                Engines = engineRegistry
                    .GetSupportedEngines()
                    .Select(engine => new TranscriptionEngineOptionDto
                    {
                        Engine = engine,
                        Models = engineRegistry.GetSupportedModels(engine).ToArray(),
                    })
                    .ToArray(),
            };

            return Results.Ok(options);
        })
        .WithName("GetSettingsOptions");

        group.MapGet("/", async (ISettingsService service, CancellationToken ct) =>
        {
            var settings = await service.GetAsync(ct);
            return Results.Ok(settings);
        })
        .WithName("GetSettings");

        group.MapGet("/models", async (ITranscriptionModelManagerService service, CancellationToken ct) =>
        {
            var catalog = await service.GetCatalogAsync(ct);
            return Results.Ok(catalog);
        })
        .WithName("GetTranscriptionModels");

        group.MapPost("/models/manage", async (ManageTranscriptionModelRequest request, ITranscriptionModelManagerService service, CancellationToken ct) =>
        {
            try
            {
                var result = await service.ManageAsync(request, ct);
                return Results.Ok(result);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new ErrorResponse("validation_error", ex.Message));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new ErrorResponse("validation_error", ex.Message));
            }
        })
        .WithName("ManageTranscriptionModel");

        group.MapPut("/", async (UpdateGlobalSettingsRequest request, ISettingsService service, ITranscriptionEngineRegistry engineRegistry, CancellationToken ct) =>
        {
            var validLanguageModes = new[] { "Auto", "Fixed" };
            var validViewModes = new[] { "Readable", "Timestamped" };

            if (!engineRegistry.IsSupportedEngine(request.DefaultEngine))
                return Results.BadRequest(new ErrorResponse("validation_error", "Unsupported engine."));

            if (!engineRegistry.IsSupportedModel(request.DefaultEngine, request.DefaultModel))
            {
                var supportedModels = string.Join(", ", engineRegistry.GetSupportedModels(request.DefaultEngine));
                return Results.BadRequest(new ErrorResponse("validation_error", $"Unsupported model for engine {request.DefaultEngine}. Supported models: {supportedModels}."));
            }

            if (!validLanguageModes.Contains(request.DefaultLanguageMode))
                return Results.BadRequest(new ErrorResponse("validation_error", "Invalid language mode."));

            if (request.DefaultLanguageMode == "Fixed" && string.IsNullOrWhiteSpace(request.DefaultLanguageCode))
                return Results.BadRequest(new ErrorResponse("validation_error", "Language code is required when language mode is Fixed."));

            if (request.DefaultLanguageMode == "Fixed"
                && !TranscriptionLanguageCatalog.IsSupportedFixedLanguage(request.DefaultEngine, request.DefaultLanguageCode))
            {
                var supportedLanguages = string.Join(", ", TranscriptionLanguageCatalog.GetSupportedFixedLanguages(request.DefaultEngine));
                return Results.BadRequest(new ErrorResponse(
                    "validation_error",
                    supportedLanguages.Length == 0
                        ? "Unsupported fixed language for engine."
                        : $"Unsupported fixed language for engine {request.DefaultEngine}. Supported fixed languages: {supportedLanguages}."));
            }

            if (!validViewModes.Contains(request.DefaultTranscriptViewMode))
                return Results.BadRequest(new ErrorResponse("validation_error", "Invalid transcript view mode."));

            var settings = await service.UpdateAsync(request, ct);
            return Results.Ok(settings);
        })
        .WithName("UpdateSettings");
    }
}
