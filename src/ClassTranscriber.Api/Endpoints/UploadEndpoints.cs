using System.Text.Json;
using ClassTranscriber.Api.Contracts;
using ClassTranscriber.Api.Services;
using ClassTranscriber.Api.Transcription;

namespace ClassTranscriber.Api.Endpoints;

public static class UploadEndpoints
{
    public static void MapUploadEndpoints(this WebApplication app)
    {
        app.MapPost("/api/uploads/batch", async (HttpRequest request, IUploadService uploadService, ITranscriptionEngineRegistry engineRegistry, CancellationToken ct) =>
        {
            if (!request.HasFormContentType)
                return Results.BadRequest(new ErrorResponse("invalid_request", "Expected multipart/form-data."));

            var form = await request.ReadFormAsync(ct);

            var folderIdStr = form["folderId"].ToString();
            if (!Guid.TryParse(folderIdStr, out var folderId))
                return Results.BadRequest(new ErrorResponse("validation_error", "folderId is required."));

            var autoQueue = bool.TryParse(form["autoQueue"].ToString(), out var aq) && aq;

            ProjectSettingsDto settings;
            var settingsJson = form["settings"].ToString();
            if (!string.IsNullOrWhiteSpace(settingsJson))
            {
                try
                {
                    settings = JsonSerializer.Deserialize<ProjectSettingsDto>(settingsJson, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                    })!;
                }
                catch
                {
                    return Results.BadRequest(new ErrorResponse("validation_error", "Invalid settings JSON."));
                }
            }
            else
            {
                settings = new ProjectSettingsDto
                {
                    Engine = "Whisper",
                    Model = "small",
                    LanguageMode = "Auto",
                    LanguageCode = null,
                    AudioNormalizationEnabled = true,
                    DiarizationEnabled = false,
                };
            }

            if (!engineRegistry.IsSupportedEngine(settings.Engine))
                return Results.BadRequest(new ErrorResponse("validation_error", "Unsupported engine."));

            if (!engineRegistry.IsSupportedModel(settings.Engine, settings.Model))
            {
                var supportedModels = string.Join(", ", engineRegistry.GetSupportedModels(settings.Engine));
                return Results.BadRequest(new ErrorResponse("validation_error", $"Unsupported model for engine {settings.Engine}. Supported models: {supportedModels}."));
            }

            var files = form.Files.GetFiles("files");
            if (files.Count == 0)
                return Results.BadRequest(new ErrorResponse("validation_error", "At least one file is required."));

            var items = new List<UploadItemMetadata>();
            var itemsJson = form["items"].ToString();
            if (!string.IsNullOrWhiteSpace(itemsJson))
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<List<UploadItemMetadataInput>>(itemsJson, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                    });
                    if (parsed is not null)
                    {
                        items.AddRange(parsed.Select(i => new UploadItemMetadata(
                            i.OriginalFileName ?? "",
                            i.ProjectName)));
                    }
                }
                catch
                {
                    return Results.BadRequest(new ErrorResponse("validation_error", "Invalid items JSON."));
                }
            }

            try
            {
                var result = await uploadService.BatchUploadAsync(folderId, autoQueue, settings, files, items, ct);
                return Results.Ok(result);
            }
            catch (ArgumentException ex)
            {
                return Results.NotFound(new ErrorResponse("not_found", ex.Message));
            }
        })
        .WithName("BatchUpload")
        .WithTags("Uploads")
        .DisableAntiforgery();
    }

    private record UploadItemMetadataInput
    {
        public string? OriginalFileName { get; init; }
        public string? ProjectName { get; init; }
    }
}
