using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ClassTranscriber.Api.Contracts;
using ClassTranscriber.Api.Domain;
using ClassTranscriber.Api.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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
    public async Task UpdateProject_RenamesProject()
    {
        var folderId = await CreateFolder("Rename Test Folder");
        var (projectId, _) = await UploadTestFile(folderId);

        var response = await _client.PutAsJsonAsync($"/api/projects/{projectId}", new
        {
            name = "Renamed Project",
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var project = await GetProject(projectId);
        project.GetProperty("name").GetString().Should().Be("Renamed Project");
    }

    [Fact]
    public async Task UpdateProject_WithBlankName_ReturnsBadRequest()
    {
        var folderId = await CreateFolder("Rename Validation Folder");
        var (projectId, _) = await UploadTestFile(folderId);

        var response = await _client.PutAsJsonAsync($"/api/projects/{projectId}", new
        {
            name = "   ",
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
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
    public async Task QueueOverview_DoesNotExposeDraftsCollection()
    {
        var folderId = await CreateFolder("Queue Contract Folder");
        await UploadTestFile(folderId);

        var response = await _client.GetAsync("/api/queue");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var queue = await response.Content.ReadFromJsonAsync<JsonElement>();
        queue.TryGetProperty("drafts", out _).Should().BeFalse();
    }

    [Fact]
    public async Task ProjectAndQueueEndpoints_ExposeTranscriptionElapsedMs_WhenPresent()
    {
        var folderId = await CreateFolder("Elapsed Metric Folder");
        var (projectId, _) = await UploadTestFile(folderId, autoQueue: true);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var persistedProject = await db.Projects.FirstAsync(p => p.Id == projectId);
            persistedProject.Status = ProjectStatus.Completed;
            persistedProject.TranscriptionElapsedMs = 12_345;
            persistedProject.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        var projectJson = await GetProject(projectId);
        projectJson.GetProperty("transcriptionElapsedMs").GetInt64().Should().Be(12_345);

        var queueResponse = await _client.GetAsync("/api/queue");
        queueResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var queue = await queueResponse.Content.ReadFromJsonAsync<JsonElement>();
        var completedItem = queue.GetProperty("completed")
            .EnumerateArray()
            .First(item => item.GetProperty("id").GetString() == projectId.ToString());
        completedItem.GetProperty("transcriptionElapsedMs").GetInt64().Should().Be(12_345);
    }

    [Fact]
    public async Task ProjectEndpoint_ExposesDebugTimings_WhenPresent()
    {
        var folderId = await CreateFolder("Debug Timing Folder");
        var (projectId, _) = await UploadTestFile(folderId, autoQueue: true);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var persistedProject = await db.Projects.FirstAsync(p => p.Id == projectId);
            persistedProject.Status = ProjectStatus.Completed;
            persistedProject.DurationMs = 10_000;
            persistedProject.TranscriptionElapsedMs = 4_000;
            persistedProject.TotalProcessingElapsedMs = 6_000;
            persistedProject.MediaInspectionElapsedMs = 500;
            persistedProject.AudioExtractionElapsedMs = 900;
            persistedProject.AudioNormalizationElapsedMs = 600;
            persistedProject.ResultPersistenceElapsedMs = 250;
            persistedProject.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        var projectJson = await GetProject(projectId);
        var debugTimings = projectJson.GetProperty("debugTimings");

        debugTimings.GetProperty("totalElapsedMs").GetInt64().Should().Be(6_000);
        debugTimings.GetProperty("preparationElapsedMs").GetInt64().Should().Be(2_000);
        debugTimings.GetProperty("inspectElapsedMs").GetInt64().Should().Be(500);
        debugTimings.GetProperty("extractElapsedMs").GetInt64().Should().Be(900);
        debugTimings.GetProperty("normalizeElapsedMs").GetInt64().Should().Be(600);
        debugTimings.GetProperty("transcriptionElapsedMs").GetInt64().Should().Be(4_000);
        debugTimings.GetProperty("persistElapsedMs").GetInt64().Should().Be(250);
        debugTimings.GetProperty("transcriptionRealtimeFactor").GetDouble().Should().Be(0.4d);
        debugTimings.GetProperty("totalRealtimeFactor").GetDouble().Should().Be(0.6d);
    }

    [Fact]
    public async Task Upload_WithSettingsOverride_AppliesSettings()
    {
        var folderId = await CreateFolder("Override Test Folder");

        var settings = new { engine = "WhisperNet", model = "base", languageMode = "Fixed", languageCode = "en", audioNormalizationEnabled = false, diarizationEnabled = false };
        var settingsJson = JsonSerializer.Serialize(settings);

        using var content = new MultipartFormDataContent();
        var fileBytes = CreateMinimalWavBytesForTests();
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
    public async Task Upload_UsesPersistedDefaults_WhenSettingsOverrideIsOmitted()
    {
        var folderId = await CreateFolder("Defaults Test Folder");

        var updateResponse = await _client.PutAsJsonAsync("/api/settings", new
        {
            defaultEngine = "SherpaOnnx",
            defaultModel = "small",
            defaultLanguageMode = "Fixed",
            defaultLanguageCode = "es",
            defaultAudioNormalizationEnabled = false,
            defaultDiarizationEnabled = true,
            defaultTranscriptViewMode = "Timestamped",
        });
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var (projectId, _) = await UploadTestFile(folderId, fileName: "defaults.wav");
        var project = await GetProject(projectId);
        var settings = project.GetProperty("settings");

        settings.GetProperty("engine").GetString().Should().Be("SherpaOnnx");
        settings.GetProperty("model").GetString().Should().Be("small");
        settings.GetProperty("languageMode").GetString().Should().Be("Fixed");
        settings.GetProperty("languageCode").GetString().Should().Be("es");
        settings.GetProperty("audioNormalizationEnabled").GetBoolean().Should().BeFalse();
        settings.GetProperty("diarizationEnabled").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Upload_ItemsWithoutOriginalFileName_FallBackToUploadedFileName()
    {
        var folderId = await CreateFolder("Item Metadata Folder");

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(folderId.ToString()), "folderId");
        content.Add(new StringContent("""[{"projectName":"Renamed Lecture"}]"""), "items");

        var fileBytes = CreateMinimalWavBytesForTests();
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(fileContent, "files", "lecture-source.wav");

        var response = await _client.PostAsync("/api/uploads/batch", content);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        var projectId = Guid.Parse(result.GetProperty("createdProjects")[0].GetProperty("id").GetString()!);
        var project = await GetProject(projectId);

        project.GetProperty("name").GetString().Should().Be("Renamed Lecture");
        project.GetProperty("originalFileName").GetString().Should().Be("lecture-source.wav");
        project.GetProperty("mediaType").GetString().Should().Be("Audio");
    }

    [Fact]
    public async Task Upload_WithSherpaOnnxSettings_AcceptsSupportedModel()
    {
        var folderId = await CreateFolder("Sherpa Upload Folder");

        var settings = new { engine = "SherpaOnnx", model = "small", languageMode = "Auto", languageCode = (string?)null, audioNormalizationEnabled = true, diarizationEnabled = false };
        var settingsJson = JsonSerializer.Serialize(settings);

        using var content = new MultipartFormDataContent();
        var fileBytes = CreateMinimalWavBytesForTests();
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
    public async Task Upload_WithSherpaOnnxSenseVoiceSettings_AcceptsSupportedModel()
    {
        var folderId = await CreateFolder("SenseVoice Upload Folder");

        var settings = new { engine = "SherpaOnnxSenseVoice", model = "small", languageMode = "Auto", languageCode = (string?)null, audioNormalizationEnabled = true, diarizationEnabled = false };
        var settingsJson = JsonSerializer.Serialize(settings);

        using var content = new MultipartFormDataContent();
        var fileBytes = CreateMinimalWavBytesForTests();
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(fileContent, "files", "test-sensevoice.wav");
        content.Add(new StringContent(folderId.ToString()), "folderId");
        content.Add(new StringContent(settingsJson), "settings");

        var response = await _client.PostAsync("/api/uploads/batch", content);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        var projectId = Guid.Parse(result.GetProperty("createdProjects")[0].GetProperty("id").GetString()!);

        var project = await GetProject(projectId);
        project.GetProperty("settings").GetProperty("engine").GetString().Should().Be("SherpaOnnxSenseVoice");
        project.GetProperty("settings").GetProperty("model").GetString().Should().Be("small");
    }

    [Fact]
    public async Task Upload_WithWhisperNetCudaSettings_AcceptsSupportedModel()
    {
        var folderId = await CreateFolder("WhisperNet CUDA Upload Folder");

        var settings = new { engine = "WhisperNetCuda", model = "small", languageMode = "Auto", languageCode = (string?)null, audioNormalizationEnabled = true, diarizationEnabled = false };
        var response = await UploadWithSettingsAsync(folderId, settings);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        var projectId = Guid.Parse(result.GetProperty("createdProjects")[0].GetProperty("id").GetString()!);

        var project = await GetProject(projectId);
        project.GetProperty("settings").GetProperty("engine").GetString().Should().Be("WhisperNetCuda");
        project.GetProperty("settings").GetProperty("model").GetString().Should().Be("small");
    }

    [Fact]
    public async Task Upload_WithUnsupportedFixedLanguageForSherpaOnnxSenseVoice_ReturnsBadRequest()
    {
        var folderId = await CreateFolder("Bad SenseVoice Language Folder");

        var settings = new { engine = "SherpaOnnxSenseVoice", model = "small", languageMode = "Fixed", languageCode = "es", audioNormalizationEnabled = true, diarizationEnabled = false };
        var response = await UploadWithSettingsAsync(folderId, settings);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var payload = await response.Content.ReadAsStringAsync();
        payload.Should().Contain("Supported fixed languages: zh, en, ja, ko, yue");
    }

    [Fact]
    public async Task Upload_WithUnsupportedSherpaOnnxModel_ReturnsBadRequest()
    {
        var folderId = await CreateFolder("Bad Sherpa Upload Folder");

        var settings = new { engine = "SherpaOnnx", model = "tiny", languageMode = "Auto", languageCode = (string?)null, audioNormalizationEnabled = true, diarizationEnabled = false };
        var settingsJson = JsonSerializer.Serialize(settings);

        using var content = new MultipartFormDataContent();
        var fileBytes = CreateMinimalWavBytesForTests();
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(fileContent, "files", "test-sherpa-invalid.wav");
        content.Add(new StringContent(folderId.ToString()), "folderId");
        content.Add(new StringContent(settingsJson), "settings");

        var response = await _client.PostAsync("/api/uploads/batch", content);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Upload_WithInvalidLanguageMode_ReturnsBadRequest()
    {
        var folderId = await CreateFolder("Bad Language Mode Folder");

        var settings = new { engine = "WhisperNet", model = "small", languageMode = "Unknown", languageCode = (string?)null, audioNormalizationEnabled = true, diarizationEnabled = false };
        var response = await UploadWithSettingsAsync(folderId, settings);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Upload_WithFixedLanguageModeWithoutLanguageCode_ReturnsBadRequest()
    {
        var folderId = await CreateFolder("Missing Language Code Folder");

        var settings = new { engine = "WhisperNet", model = "small", languageMode = "Fixed", languageCode = (string?)null, audioNormalizationEnabled = true, diarizationEnabled = false };
        var response = await UploadWithSettingsAsync(folderId, settings);

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

        var file1 = new ByteArrayContent(CreateMinimalWavBytesForTests());
        file1.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(file1, "files", "file1.wav");

        var file2 = new ByteArrayContent(CreateMinimalWavBytesForTests());
        file2.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(file2, "files", "file2.wav");

        var response = await _client.PostAsync("/api/uploads/batch", content);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("createdProjects").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task Upload_LargerThanLegacyThirtyMegabyteLimit_Succeeds()
    {
        var folderId = await CreateFolder("Large Upload Folder");

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(folderId.ToString()), "folderId");

        var fileContent = new ByteArrayContent(new byte[32 * 1024 * 1024]);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("video/x-matroska");
        content.Add(fileContent, "files", "long-class-recording.mkv");

        var response = await _client.PostAsync("/api/uploads/batch", content);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        var projectId = Guid.Parse(result.GetProperty("createdProjects")[0].GetProperty("id").GetString()!);
        var project = await GetProject(projectId);

        project.GetProperty("mediaType").GetString().Should().Be("Video");
        project.GetProperty("totalSizeBytes").GetInt64().Should().Be(32L * 1024 * 1024);
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
    public async Task Retry_FailedProject_AcceptsSettingsOverride()
    {
        var folderId = await CreateFolder("Retry Override Folder");
        var (projectId, _) = await UploadTestFile(folderId);
        await MarkProjectFailed(projectId);

        var retryResponse = await _client.PostAsJsonAsync($"/api/projects/{projectId}/retry", new
        {
            settings = new
            {
                engine = "WhisperNet",
                model = "base",
                languageMode = "Fixed",
                languageCode = "en",
                audioNormalizationEnabled = false,
                diarizationEnabled = true,
            },
        });

        retryResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var project = await GetProject(projectId);
        project.GetProperty("status").GetString().Should().Be("Queued");

        var settings = project.GetProperty("settings");
        settings.GetProperty("engine").GetString().Should().Be("WhisperNet");
        settings.GetProperty("model").GetString().Should().Be("base");
        settings.GetProperty("languageMode").GetString().Should().Be("Fixed");
        settings.GetProperty("languageCode").GetString().Should().Be("en");
        settings.GetProperty("audioNormalizationEnabled").GetBoolean().Should().BeFalse();
        settings.GetProperty("diarizationEnabled").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Retry_FailedProject_WithUnsupportedEngine_ReturnsBadRequest()
    {
        var folderId = await CreateFolder("Retry Validation Folder");
        var (projectId, _) = await UploadTestFile(folderId);
        await MarkProjectFailed(projectId);

        var retryResponse = await _client.PostAsJsonAsync($"/api/projects/{projectId}/retry", new
        {
            settings = new
            {
                engine = "NotARealEngine",
                model = "base",
                languageMode = "Auto",
                languageCode = (string?)null,
                audioNormalizationEnabled = true,
                diarizationEnabled = false,
            },
        });

        retryResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("queue")]
    [InlineData("retry")]
    [InlineData("cancel")]
    public async Task ProjectActions_ReturnNotFound_WhenProjectDoesNotExist(string action)
    {
        var response = await _client.PostAsync($"/api/projects/{Guid.NewGuid()}/{action}", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
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

        var file = new ByteArrayContent(CreateMinimalWavBytesForTests());
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

        var fileBytes = CreateMinimalWavBytesForTests();
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(fileContent, "files", fileName);

        var response = await _client.PostAsync("/api/uploads/batch", content);
        response.StatusCode.Should().Be(HttpStatusCode.OK, "Upload should succeed");

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        var projectId = Guid.Parse(result.GetProperty("createdProjects")[0].GetProperty("id").GetString()!);
        return (projectId, result);
    }

    private async Task<HttpResponseMessage> UploadWithSettingsAsync(Guid folderId, object settings)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(folderId.ToString()), "folderId");
        content.Add(new StringContent(JsonSerializer.Serialize(settings)), "settings");

        var fileBytes = CreateMinimalWavBytesForTests();
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(fileContent, "files", "settings-test.wav");

        return await _client.PostAsync("/api/uploads/batch", content);
    }

    private async Task<JsonElement> GetProject(Guid projectId)
    {
        var response = await _client.GetAsync($"/api/projects/{projectId}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task MarkProjectFailed(Guid projectId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var project = await db.Projects.FirstAsync(p => p.Id == projectId);
        project.Status = ProjectStatus.Failed;
        project.ErrorMessage = "Simulated failure";
        project.FailedAtUtc = DateTime.UtcNow;
        project.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Creates a minimal valid WAV file header (44 bytes, 0 samples).
    /// </summary>
    internal static byte[] CreateMinimalWavBytesForTests()
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
