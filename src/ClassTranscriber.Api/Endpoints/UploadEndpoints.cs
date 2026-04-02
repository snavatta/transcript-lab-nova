using System.Text.Json;
using ClassTranscriber.Api.Contracts;
using ClassTranscriber.Api.Services;

namespace ClassTranscriber.Api.Endpoints;

public static class UploadEndpoints
{
    public static void MapUploadEndpoints(this WebApplication app)
    {
        app.MapPost("/api/uploads/batch", async (HttpRequest request, IUploadService uploadService, CancellationToken ct) =>
        {
            if (!request.HasFormContentType)
                return Results.BadRequest(new ErrorResponse("invalid_request", "Expected multipart/form-data."));

            var form = await request.ReadFormAsync(ct);

            var folderIdStr = form["folderId"].ToString();
            if (!Guid.TryParse(folderIdStr, out var folderId))
                return Results.BadRequest(new ErrorResponse("validation_error", "folderId is required."));

            var autoQueue = bool.TryParse(form["autoQueue"].ToString(), out var aq) && aq;

            ProjectSettingsDto? settingsOverride = null;
            var settingsJson = form["settings"].ToString();
            if (!string.IsNullOrWhiteSpace(settingsJson))
            {
                try
                {
                    settingsOverride = JsonSerializer.Deserialize<ProjectSettingsDto>(settingsJson, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                    });
                }
                catch
                {
                    return Results.BadRequest(new ErrorResponse("validation_error", "Invalid settings JSON."));
                }
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
                            i.OriginalFileName,
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
                var result = await uploadService.BatchUploadAsync(folderId, autoQueue, settingsOverride, files, items, ct);
                return Results.Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new ErrorResponse("not_found", ex.Message));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new ErrorResponse("validation_error", ex.Message));
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
