using ClassTranscriber.Api.Contracts;
using ClassTranscriber.Api.Services;

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

        group.MapDelete("/{id:guid}", async (Guid id, IProjectService service, CancellationToken ct) =>
        {
            var deleted = await service.DeleteAsync(id, ct);
            return deleted ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteProject");

        group.MapPost("/{id:guid}/queue", async (Guid id, IProjectService service, CancellationToken ct) =>
        {
            var project = await service.QueueAsync(id, ct);
            if (project is null)
                return Results.Conflict(new ErrorResponse("invalid_state", "Project cannot be queued in its current state."));
            return Results.Ok(project);
        })
        .WithName("QueueProject");

        group.MapPost("/{id:guid}/retry", async (Guid id, IProjectService service, CancellationToken ct) =>
        {
            var project = await service.RetryAsync(id, ct);
            if (project is null)
                return Results.Conflict(new ErrorResponse("invalid_state", "Project cannot be retried in its current state."));
            return Results.Ok(project);
        })
        .WithName("RetryProject");

        group.MapPost("/{id:guid}/cancel", async (Guid id, IProjectService service, CancellationToken ct) =>
        {
            var project = await service.CancelAsync(id, ct);
            if (project is null)
                return Results.Conflict(new ErrorResponse("invalid_state", "Project cannot be cancelled in its current state."));
            return Results.Ok(project);
        })
        .WithName("CancelProject");
    }
}
