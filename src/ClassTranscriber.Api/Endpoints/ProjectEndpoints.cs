using ClassTranscriber.Api.Contracts;
using ClassTranscriber.Api.Services;
using System.Text.Json;

namespace ClassTranscriber.Api.Endpoints;

public static class ProjectEndpoints
{
    public static void MapProjectEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/projects").WithTags("Projects");

        group.MapGet("/", async (string? folderId, string? status, string? search, string? sort,
            IProjectService service, CancellationToken ct) =>
        {
            var projects = await service.ListAsync(folderId, status, search, sort, ct);
            return Results.Ok(projects);
        })
        .WithName("ListProjects");

        group.MapGet("/{id:guid}", async (Guid id, IProjectService service, CancellationToken ct) =>
        {
            var project = await service.GetByIdAsync(id, ct);
            return project is null ? Results.NotFound() : Results.Ok(project);
        })
        .WithName("GetProject");

        group.MapPut("/{id:guid}", async (Guid id, UpdateProjectRequest request, IProjectService service, CancellationToken ct) =>
        {
            var name = request.Name?.Trim();
            if (string.IsNullOrWhiteSpace(name) || name.Length > 120)
            {
                return Results.BadRequest(new ErrorResponse(
                    "validation_error",
                    "Project name is required and must be between 1 and 120 characters."));
            }

            var normalizedRequest = request with { Name = name };
            var project = await service.UpdateAsync(id, normalizedRequest, ct);
            return project is null ? Results.NotFound() : Results.Ok(project);
        })
        .WithName("UpdateProject");

        group.MapDelete("/{id:guid}", async (Guid id, IProjectService service, CancellationToken ct) =>
        {
            var deleted = await service.DeleteAsync(id, ct);
            return deleted ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteProject");

        group.MapPost("/{id:guid}/queue", async (Guid id, IProjectService service, CancellationToken ct) =>
        {
            if (await service.GetByIdAsync(id, ct) is null)
                return Results.NotFound();

            var project = await service.QueueAsync(id, ct);
            if (project is null)
                return Results.Conflict(new ErrorResponse("invalid_state", "Project cannot be queued in its current state."));
            return Results.Ok(project);
        })
        .WithName("QueueProject");

        group.MapPost("/{id:guid}/retry", async (Guid id, HttpRequest httpRequest, IProjectService service, CancellationToken ct) =>
        {
            if (await service.GetByIdAsync(id, ct) is null)
                return Results.NotFound();

            RetryProjectRequest? request = null;
            var hasRequestBody = httpRequest.ContentLength.GetValueOrDefault() > 0
                || !string.IsNullOrWhiteSpace(httpRequest.ContentType);
            if (hasRequestBody)
            {
                try
                {
                    request = await httpRequest.ReadFromJsonAsync<RetryProjectRequest>(cancellationToken: ct);
                }
                catch (JsonException)
                {
                    return Results.BadRequest(new ErrorResponse("validation_error", "Invalid retry settings JSON."));
                }
            }

            ProjectDetailDto? project;
            try
            {
                project = await service.RetryAsync(id, request, ct);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new ErrorResponse("validation_error", ex.Message));
            }

            if (project is null)
                return Results.Conflict(new ErrorResponse("invalid_state", "Project cannot be retried in its current state."));
            return Results.Ok(project);
        })
        .WithName("RetryProject");

        group.MapPost("/{id:guid}/cancel", async (Guid id, IProjectService service, CancellationToken ct) =>
        {
            if (await service.GetByIdAsync(id, ct) is null)
                return Results.NotFound();

            var project = await service.CancelAsync(id, ct);
            if (project is null)
                return Results.Conflict(new ErrorResponse("invalid_state", "Project cannot be cancelled in its current state."));
            return Results.Ok(project);
        })
        .WithName("CancelProject");
    }
}
