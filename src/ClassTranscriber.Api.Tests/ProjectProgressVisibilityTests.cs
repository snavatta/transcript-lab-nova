using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClassTranscriber.Api.Contracts;
using ClassTranscriber.Api.Persistence;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace ClassTranscriber.Api.Tests;

public sealed class ProjectProgressVisibilityTests : IAsyncLifetime
{
    private TestWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        _factory = new TestWebApplicationFactory();
        _client = _factory.Client;
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task ProjectAndQueueEndpoints_HideProgressForProcessingStates()
    {
        var folderResponse = await _client.PostAsJsonAsync("/api/folders", new
        {
            name = "Progress Visibility Folder",
            iconKey = "Folder",
            colorHex = "#336699",
        });
        folderResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var folder = await folderResponse.Content.ReadFromJsonAsync<JsonElement>();
        var folderId = Guid.Parse(folder.GetProperty("id").GetString()!);

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(folderId.ToString()), "folderId");
        content.Add(new StringContent("true"), "autoQueue");

        var fileContent = new ByteArrayContent(TranscriptionPipelineTests.CreateMinimalWavBytesForTests());
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/wav");
        content.Add(fileContent, "files", "progress.wav");

        var uploadResponse = await _client.PostAsync("/api/uploads/batch", content);
        uploadResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var upload = await uploadResponse.Content.ReadFromJsonAsync<JsonElement>();
        var projectId = Guid.Parse(upload.GetProperty("createdProjects")[0].GetProperty("id").GetString()!);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var project = await db.Projects.FindAsync(projectId);
            project.Should().NotBeNull();
            project!.Status = ProjectStatus.Transcribing;
            project.Progress = 40;
            project.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        var projectResponse = await _client.GetAsync($"/api/projects/{projectId}");
        projectResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var projectJson = await projectResponse.Content.ReadFromJsonAsync<JsonElement>();
        projectJson.GetProperty("status").GetString().Should().Be("Transcribing");
        projectJson.TryGetProperty("progress", out _).Should().BeFalse();

        var queueResponse = await _client.GetAsync("/api/queue");
        queueResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var queueJson = await queueResponse.Content.ReadFromJsonAsync<JsonElement>();
        var processingItem = queueJson.GetProperty("processing")
            .EnumerateArray()
            .First(item => item.GetProperty("id").GetString() == projectId.ToString());
        processingItem.TryGetProperty("progress", out _).Should().BeFalse();
    }
}
