using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace ClassTranscriber.Api.Tests;

public sealed class DiagnosticsEndpointTests : IAsyncLifetime
{
    private TestWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        _factory = new TestWebApplicationFactory(
            [
                new NoOpTranscriptionEngine("Whisper", ["small", "base"]),
                new NoOpTranscriptionEngine("WhisperNetCuda", ["small"], "CUDA runtime libraries are not available."),
            ]);
        _client = _factory.Client;
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Diagnostics_ReturnsRuntimeEngineAndProjectStorageData()
    {
        var folderId = await CreateFolderAsync("Diagnostics");
        await UploadFileAsync(folderId, "lecture.mkv");

        var response = await _client.GetAsync("/api/diagnostics");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var runtime = payload.GetProperty("runtime");
        runtime.GetProperty("processorCount").GetInt32().Should().BeGreaterThan(0);
        runtime.GetProperty("workingSetBytes").GetInt64().Should().BeGreaterThan(0);
        runtime.GetProperty("managedHeapBytes").GetInt64().Should().BeGreaterThanOrEqualTo(0);

        var engines = payload.GetProperty("engines").EnumerateArray().ToArray();
        engines.Should().Contain(engine => engine.GetProperty("engine").GetString() == "Whisper"
            && engine.GetProperty("isAvailable").GetBoolean());
        engines.Should().Contain(engine => engine.GetProperty("engine").GetString() == "WhisperNetCuda"
            && !engine.GetProperty("isAvailable").GetBoolean()
            && engine.GetProperty("availabilityError").GetString()!.Contains("CUDA runtime"));

        var projects = payload.GetProperty("projects").EnumerateArray().ToArray();
        projects.Should().Contain(project => project.GetProperty("projectName").GetString() == "lecture");
    }

    private async Task<Guid> CreateFolderAsync(string name)
    {
        var response = await _client.PostAsJsonAsync("/api/folders", new { name });
        response.EnsureSuccessStatusCode();
        var folder = await response.Content.ReadFromJsonAsync<JsonElement>();
        return folder.GetProperty("id").GetGuid();
    }

    private async Task UploadFileAsync(Guid folderId, string fileName)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(folderId.ToString()), "folderId");

        var file = new ByteArrayContent(TranscriptionPipelineTests.CreateMinimalWavBytesForTests());
        content.Add(file, "files", fileName);

        var response = await _client.PostAsync("/api/uploads/batch", content);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
