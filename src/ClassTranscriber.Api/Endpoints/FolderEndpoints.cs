using ClassTranscriber.Api.Contracts;
using ClassTranscriber.Api.Services;

namespace ClassTranscriber.Api.Endpoints;

public static class FolderEndpoints
{
    public static void MapFolderEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/folders").WithTags("Folders");

        group.MapGet("/", async (IFolderService service, CancellationToken ct) =>
        {
            var folders = await service.ListAsync(ct);
            return Results.Ok(folders);
        })
        .WithName("ListFolders");

        group.MapGet("/{id:guid}", async (Guid id, IFolderService service, CancellationToken ct) =>
        {
            var folder = await service.GetByIdAsync(id, ct);
            return folder is null ? Results.NotFound() : Results.Ok(folder);
        })
        .WithName("GetFolder");

        group.MapPost("/", async (CreateFolderRequest request, IFolderService service, CancellationToken ct) =>
        {
            var name = request.Name?.Trim();
            if (string.IsNullOrWhiteSpace(name) || name.Length > 120)
            {
                return Results.BadRequest(new ErrorResponse(
                    "validation_error",
                    "Folder name is required and must be between 1 and 120 characters."));
            }

            if (!FolderAppearance.TryResolveIconKey(request.IconKey, out var iconKey))
            {
                return Results.BadRequest(new ErrorResponse(
                    "validation_error",
                    "Folder icon is invalid and must use a valid MUI icon component name."));
            }

            if (!FolderAppearance.TryResolveColorHex(request.ColorHex, out var colorHex))
            {
                return Results.BadRequest(new ErrorResponse(
                    "validation_error",
                    "Folder color is invalid and must use #RRGGBB format."));
            }

            var normalizedRequest = request with { Name = name, IconKey = iconKey, ColorHex = colorHex };
            var folder = await service.CreateAsync(normalizedRequest, ct);
            return Results.Created($"/api/folders/{folder.Id}", folder);
        })
        .WithName("CreateFolder");

        group.MapPut("/{id:guid}", async (Guid id, UpdateFolderRequest request, IFolderService service, CancellationToken ct) =>
        {
            var name = request.Name?.Trim();
            if (string.IsNullOrWhiteSpace(name) || name.Length > 120)
            {
                return Results.BadRequest(new ErrorResponse(
                    "validation_error",
                    "Folder name is required and must be between 1 and 120 characters."));
            }

            if (!FolderAppearance.TryResolveIconKey(request.IconKey, out var iconKey))
            {
                return Results.BadRequest(new ErrorResponse(
                    "validation_error",
                    "Folder icon is invalid and must use a valid MUI icon component name."));
            }

            if (!FolderAppearance.TryResolveColorHex(request.ColorHex, out var colorHex))
            {
                return Results.BadRequest(new ErrorResponse(
                    "validation_error",
                    "Folder color is invalid and must use #RRGGBB format."));
            }

            var normalizedRequest = request with { Name = name, IconKey = iconKey, ColorHex = colorHex };
            var folder = await service.UpdateAsync(id, normalizedRequest, ct);
            return folder is null ? Results.NotFound() : Results.Ok(folder);
        })
        .WithName("UpdateFolder");

        group.MapDelete("/{id:guid}", async (Guid id, IFolderService service, CancellationToken ct) =>
        {
            var (success, error) = await service.DeleteAsync(id, ct);

            if (!success && error == "not_found")
                return Results.NotFound();

            if (!success && error == "folder_not_empty")
                return Results.Conflict(new ErrorResponse(
                    "folder_not_empty",
                    "Cannot delete a folder that contains projects."));

            return Results.NoContent();
        })
        .WithName("DeleteFolder");
    }
}
