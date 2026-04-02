using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace ClassTranscriber.Api.Tests;

/// <summary>
/// Tests the upload -> queue -> cancel/retry pipeline using the test host.
/// Uses no-op media/transcription services to verify the workflow independently
/// of external tools.
/// </summary>
public class TranscriptionPipelineTests : IAsyncLifetime
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
    public async Task Upload_Creates_Project_WithUploadedStatus()
    {
        var folderId = await CreateFolder("Upload Test Folder");

        var (projectId, _) = await UploadTestFile(folderId);

        var project = await GetProject(projectId);
        project.GetProperty("name").GetString().Should().NotBeNullOrEmpty();
        project.GetProperty("status").GetString().Should().Be("Draft");
    }

    [Fact]
    public async Task Upload_WithAutoQueue_SetsQueuedStatus()
    {
        var folderId = await CreateFolder("AutoQueue Folder");

        var (projectId, _) = await UploadTestFile(folderId, autoQueue: true);

        var project = await GetProject(projectId);
        project.GetProperty("status").GetString().Should().Be("Queued");
    }

    [Fact]
    public async Task Cancel_QueuedProject_SetsCancelledStatus()
    {
        var folderId = await CreateFolder("Cancel Test Folder");
        var (projectId, _) = await UploadTestFile(folderId, autoQueue: true);

        var cancelResponse = await _client.PostAsync($"/api/projects/{projectId}/cancel", null);
        cancelResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var project = await GetProject(projectId);
        project.GetProperty("status").GetString().Should().Be("Cancelled");
    }

    [Fact]
    public async Task Cancel_NonQueuedProject_ReturnsConflict()
    {
        var folderId = await CreateFolder("Cancel Conflict Folder");
        var (projectId, _) = await UploadTestFile(folderId); // Uploaded, not Queued

        var cancelResponse = await _client.PostAsync($"/api/projects/{projectId}/cancel", null);
        cancelResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task QueueOverview_IncludesQueuedProject()
    {
        var folderId = await CreateFolder("Queue Overview Folder");
        await UploadTestFile(folderId, autoQueue: true);

        var response = await _client.GetAsync("/api/queue");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var queue = await response.Content.ReadFromJsonAsync<JsonElement>();
        queue.GetProperty("queued").GetArrayLength().Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Upload_WithSettingsOverride_AppliesSettings()
    {
        var folderId = await CreateFolder("Override Test Folder");

        var settings = new { engine = "Whisper", model = "base", languageMode = "Fixed", languageCode = "en", audioNormalizationEnabled = false, diarizationEnabled = false };
        var settingsJson = JsonSerializer.Serialize(settings);

        using var content = new MultipartFormDataContent();
        var fileBytes = CreateMinimalWavBytes();
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(fileContent, "files", "test-override.wav");
        content.Add(new StringContent(folderId.ToString()), "folderId");
        content.Add(new StringContent(settingsJson), "settings");

        var response = await _client.PostAsync("/api/uploads/batch", content);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        var projectId = Guid.Parse(result.GetProperty("createdProjects")[0].GetProperty("id").GetString()!);

        var project = await GetProject(projectId);
        var projectSettings = project.GetProperty("settings");
        projectSettings.GetProperty("model").GetString().Should().Be("base");
        projectSettings.GetProperty("languageMode").GetString().Should().Be("Fixed");
        projectSettings.GetProperty("languageCode").GetString().Should().Be("en");
    }

    [Fact]
    public async Task Upload_WithSherpaOnnxSettings_AcceptsSupportedModel()
    {
        var folderId = await CreateFolder("Sherpa Upload Folder");

        var settings = new { engine = "SherpaOnnx", model = "small", languageMode = "Auto", languageCode = (string?)null, audioNormalizationEnabled = true, diarizationEnabled = false };
        var settingsJson = JsonSerializer.Serialize(settings);

        using var content = new MultipartFormDataContent();
        var fileBytes = CreateMinimalWavBytes();
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(fileContent, "files", "test-sherpa.wav");
        content.Add(new StringContent(folderId.ToString()), "folderId");
        content.Add(new StringContent(settingsJson), "settings");

        var response = await _client.PostAsync("/api/uploads/batch", content);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        var projectId = Guid.Parse(result.GetProperty("createdProjects")[0].GetProperty("id").GetString()!);

        var project = await GetProject(projectId);
        project.GetProperty("settings").GetProperty("engine").GetString().Should().Be("SherpaOnnx");
        project.GetProperty("settings").GetProperty("model").GetString().Should().Be("small");
    }

    [Fact]
    public async Task Upload_WithUnsupportedSherpaOnnxModel_ReturnsBadRequest()
    {
        var folderId = await CreateFolder("Bad Sherpa Upload Folder");

        var settings = new { engine = "SherpaOnnx", model = "tiny", languageMode = "Auto", languageCode = (string?)null, audioNormalizationEnabled = true, diarizationEnabled = false };
        var settingsJson = JsonSerializer.Serialize(settings);

        using var content = new MultipartFormDataContent();
        var fileBytes = CreateMinimalWavBytes();
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(fileContent, "files", "test-sherpa-invalid.wav");
        content.Add(new StringContent(folderId.ToString()), "folderId");
        content.Add(new StringContent(settingsJson), "settings");

        var response = await _client.PostAsync("/api/uploads/batch", content);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Delete_Project_RemovesIt()
    {
        var folderId = await CreateFolder("Delete Test Folder");
        var (projectId, _) = await UploadTestFile(folderId);

        var deleteResponse = await _client.DeleteAsync($"/api/projects/{projectId}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getResponse = await _client.GetAsync($"/api/projects/{projectId}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ListProjects_ByFolder_ReturnsCorrectCount()
    {
        var folderId = await CreateFolder("List Test Folder");
        await UploadTestFile(folderId);
        await UploadTestFile(folderId, fileName: "second.wav");

        var response = await _client.GetAsync($"/api/projects?folderId={folderId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var projects = await response.Content.ReadFromJsonAsync<JsonElement>();
        projects.GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task BatchUpload_MultipleFiles_CreatesMultipleProjects()
    {
        var folderId = await CreateFolder("Batch Test Folder");

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(folderId.ToString()), "folderId");

        var file1 = new ByteArrayContent(CreateMinimalWavBytes());
        file1.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(file1, "files", "file1.wav");

        var file2 = new ByteArrayContent(CreateMinimalWavBytes());
        file2.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(file2, "files", "file2.wav");

        var response = await _client.PostAsync("/api/uploads/batch", content);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("createdProjects").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task Retry_OnlyWorksForFailed_ReturnsConflictForUploaded()
    {
        var folderId = await CreateFolder("Retry Conflict Folder");
        var (projectId, _) = await UploadTestFile(folderId);

        // Retry on Uploaded status should fail (only works for Failed)
        var retryResponse = await _client.PostAsync($"/api/projects/{projectId}/retry", null);
        retryResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Upload_WithoutFiles_ReturnsBadRequest()
    {
        var folderId = await CreateFolder("No File Folder");

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(folderId.ToString()), "folderId");

        var response = await _client.PostAsync("/api/uploads/batch", content);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Upload_WithInvalidFolderId_ReturnsError()
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent("not-a-guid"), "folderId");

        var file = new ByteArrayContent(CreateMinimalWavBytes());
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(file, "files", "test.wav");

        var response = await _client.PostAsync("/api/uploads/batch", content);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // --- Helpers ---

    private async Task<Guid> CreateFolder(string name)
    {
        var response = await _client.PostAsJsonAsync("/api/folders", new { name });
        response.EnsureSuccessStatusCode();
        var folder = await response.Content.ReadFromJsonAsync<JsonElement>();
        return folder.GetProperty("id").GetGuid();
    }

    private async Task<(Guid ProjectId, JsonElement Result)> UploadTestFile(Guid folderId, string fileName = "test.wav", bool autoQueue = false)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(folderId.ToString()), "folderId");
        if (autoQueue)
            content.Add(new StringContent("true"), "autoQueue");

        var fileBytes = CreateMinimalWavBytes();
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(fileContent, "files", fileName);

        var response = await _client.PostAsync("/api/uploads/batch", content);
        response.StatusCode.Should().Be(HttpStatusCode.OK, "Upload should succeed");

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        var projectId = Guid.Parse(result.GetProperty("createdProjects")[0].GetProperty("id").GetString()!);
        return (projectId, result);
    }

    private async Task<JsonElement> GetProject(Guid projectId)
    {
        var response = await _client.GetAsync($"/api/projects/{projectId}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>
    /// Creates a minimal valid WAV file header (44 bytes, 0 samples).
    /// </summary>
    private static byte[] CreateMinimalWavBytes()
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        // RIFF header
        bw.Write("RIFF"u8);
        bw.Write(36);           // ChunkSize (36 + 0 data bytes)
        bw.Write("WAVE"u8);

        // fmt sub-chunk
        bw.Write("fmt "u8);
        bw.Write(16);           // SubChunk1Size (PCM)
        bw.Write((short)1);     // AudioFormat (PCM)
        bw.Write((short)1);     // NumChannels (mono)
        bw.Write(16000);        // SampleRate
        bw.Write(32000);        // ByteRate
        bw.Write((short)2);     // BlockAlign
        bw.Write((short)16);    // BitsPerSample

        // data sub-chunk
        bw.Write("data"u8);
        bw.Write(0);            // SubChunk2Size (0 samples)

        return ms.ToArray();
    }
}
