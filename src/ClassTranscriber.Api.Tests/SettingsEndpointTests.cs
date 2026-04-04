using System.Net;
using System.Net.Http.Json;
using ClassTranscriber.Api.Contracts;
using ClassTranscriber.Api.Transcription;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ClassTranscriber.Api.Tests;

public class SettingsEndpointTests : IAsyncLifetime
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
    public async Task GetSettings_ReturnsDefaults()
    {
        var response = await _client.GetAsync("/api/settings");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var settings = await response.Content.ReadFromJsonAsync<GlobalSettingsDto>();
        settings.Should().NotBeNull();
        settings!.DefaultEngine.Should().Be("WhisperNet");
        settings.DefaultModel.Should().Be("small");
    }

    [Fact]
    public async Task UpdateSettings_PersistsChanges()
    {
        var update = new
        {
            defaultEngine = "WhisperNet",
            defaultModel = "medium",
            defaultLanguageMode = "Fixed",
            defaultLanguageCode = "es",
            defaultAudioNormalizationEnabled = false,
            defaultDiarizationEnabled = true,
            defaultDiarizationMode = "Basic",
            defaultTranscriptViewMode = "Timestamped"
        };

        var response = await _client.PutAsJsonAsync("/api/settings", update);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var settings = await response.Content.ReadFromJsonAsync<GlobalSettingsDto>();
        settings!.DefaultModel.Should().Be("medium");
        settings.DefaultLanguageMode.Should().Be("Fixed");
        settings.DefaultLanguageCode.Should().Be("es");
        settings.DefaultDiarizationEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateSettings_AllowsSherpaOnnxWithSupportedModel()
    {
        var update = new
        {
            defaultEngine = "SherpaOnnx",
            defaultModel = "small",
            defaultLanguageMode = "Auto",
            defaultLanguageCode = (string?)null,
            defaultAudioNormalizationEnabled = true,
            defaultDiarizationEnabled = false,
            defaultDiarizationMode = "Basic",
            defaultTranscriptViewMode = "Readable"
        };

        var response = await _client.PutAsJsonAsync("/api/settings", update);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var settings = await response.Content.ReadFromJsonAsync<GlobalSettingsDto>();
        settings.Should().NotBeNull();
        settings!.DefaultEngine.Should().Be("SherpaOnnx");
        settings.DefaultModel.Should().Be("small");
    }

    [Fact]
    public async Task UpdateSettings_RejectsUnsupportedModelForEngine()
    {
        var update = new
        {
            defaultEngine = "SherpaOnnx",
            defaultModel = "tiny",
            defaultLanguageMode = "Auto",
            defaultLanguageCode = (string?)null,
            defaultAudioNormalizationEnabled = true,
            defaultDiarizationEnabled = false,
            defaultDiarizationMode = "Basic",
            defaultTranscriptViewMode = "Readable"
        };

        var response = await _client.PutAsJsonAsync("/api/settings", update);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetSettingsOptions_ReturnsRegisteredEnginesAndModels()
    {
        var response = await _client.GetAsync("/api/settings/options");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var options = await response.Content.ReadFromJsonAsync<TranscriptionOptionsDto>();
        options.Should().NotBeNull();
        options!.Engines.Should().ContainSingle(engine => engine.Engine == "WhisperNet");
        options.Engines.Should().ContainSingle(engine => engine.Engine == "SherpaOnnx");
        options.Engines.Should().ContainSingle(engine => engine.Engine == "SherpaOnnxSenseVoice");
        options.Engines.Should().ContainSingle(engine => engine.Engine == "WhisperNetCuda");
        options.Engines.Single(engine => engine.Engine == "SherpaOnnx").Models.Should().Contain(new[] { "small", "medium" });
        options.Engines.Single(engine => engine.Engine == "SherpaOnnxSenseVoice").Models.Should().ContainSingle().Which.Should().Be("small");
        options.Engines.Single(engine => engine.Engine == "WhisperNetCuda").Models.Should().Contain(new[] { "tiny", "base", "small", "medium", "large" });
    }

    [Fact]
    public async Task GetSettingsModels_ReturnsKnownCatalogEntries()
    {
        var response = await _client.GetAsync("/api/settings/models");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var catalog = await response.Content.ReadFromJsonAsync<TranscriptionModelCatalogDto>();
        catalog.Should().NotBeNull();
        catalog!.Models.Should().Contain(entry => entry.Engine == "WhisperNet" && entry.Model == "small");
        catalog.Models.Should().Contain(entry => entry.Engine == "SherpaOnnx" && entry.Model == "medium");
        catalog.Models.Should().Contain(entry => entry.Engine == "WhisperNetCuda" && entry.Model == "base");
    }

    [Fact]
    public async Task ManageTranscriptionModel_ProbeInstalledModel_ReturnsReady()
    {
        var whisperNetOptions = _factory.Services.GetRequiredService<IOptions<WhisperNetOptions>>().Value;
        var installPath = GgmlModelDownloads.GetModelPath(whisperNetOptions.ModelsPath, "small");
        Directory.CreateDirectory(Path.GetDirectoryName(installPath)!);
        await File.WriteAllBytesAsync(installPath, []);

        var response = await _client.PostAsJsonAsync("/api/settings/models/manage", new
        {
            engine = "WhisperNet",
            model = "small",
            action = "Probe",
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var entry = await response.Content.ReadFromJsonAsync<TranscriptionModelEntryDto>();
        entry.Should().NotBeNull();
        entry!.ProbeState.Should().Be("Ready");
    }

    [Fact]
    public async Task ManageTranscriptionModel_ProbeMissingModel_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/api/settings/models/manage", new
        {
            engine = "WhisperNet",
            model = "small",
            action = "Probe",
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateSettings_AllowsWhisperNetCudaWithSupportedModel()
    {
        var update = new
        {
            defaultEngine = "WhisperNetCuda",
            defaultModel = "small",
            defaultLanguageMode = "Auto",
            defaultLanguageCode = (string?)null,
            defaultAudioNormalizationEnabled = true,
            defaultDiarizationEnabled = false,
            defaultDiarizationMode = "Basic",
            defaultTranscriptViewMode = "Readable"
        };

        var response = await _client.PutAsJsonAsync("/api/settings", update);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var settings = await response.Content.ReadFromJsonAsync<GlobalSettingsDto>();
        settings.Should().NotBeNull();
        settings!.DefaultEngine.Should().Be("WhisperNetCuda");
        settings.DefaultModel.Should().Be("small");
    }

    [Fact]
    public async Task UpdateSettings_AllowsSherpaOnnxSenseVoiceWithSupportedModel()
    {
        var update = new
        {
            defaultEngine = "SherpaOnnxSenseVoice",
            defaultModel = "small",
            defaultLanguageMode = "Auto",
            defaultLanguageCode = (string?)null,
            defaultAudioNormalizationEnabled = true,
            defaultDiarizationEnabled = false,
            defaultDiarizationMode = "Basic",
            defaultTranscriptViewMode = "Readable"
        };

        var response = await _client.PutAsJsonAsync("/api/settings", update);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var settings = await response.Content.ReadFromJsonAsync<GlobalSettingsDto>();
        settings.Should().NotBeNull();
        settings!.DefaultEngine.Should().Be("SherpaOnnxSenseVoice");
        settings.DefaultModel.Should().Be("small");
    }

    [Fact]
    public async Task UpdateSettings_RejectsUnsupportedFixedLanguageForSherpaOnnxSenseVoice()
    {
        var response = await _client.PutAsJsonAsync("/api/settings", new
        {
            defaultEngine = "SherpaOnnxSenseVoice",
            defaultModel = "small",
            defaultLanguageMode = "Fixed",
            defaultLanguageCode = "es",
            defaultAudioNormalizationEnabled = true,
            defaultDiarizationEnabled = false,
            defaultDiarizationMode = "Basic",
            defaultTranscriptViewMode = "Readable",
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await response.Content.ReadAsStringAsync();
        error.Should().Contain("Supported fixed languages: zh, en, ja, ko, yue");
    }

    [Fact]
    public async Task GetSettingsOptions_HidesUnavailableEngines()
    {
        await using var unavailableFactory = new TestWebApplicationFactory(
        [
            new NoOpTranscriptionEngine("SherpaOnnx", ["small", "medium"]),
            new NoOpTranscriptionEngine("WhisperNet", ["tiny", "base"], "worker missing"),
        ]);

        var response = await unavailableFactory.Client.GetAsync("/api/settings/options");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var options = await response.Content.ReadFromJsonAsync<TranscriptionOptionsDto>();
        options.Should().NotBeNull();
        options!.Engines.Should().ContainSingle(engine => engine.Engine == "SherpaOnnx");
        options.Engines.Should().NotContain(engine => engine.Engine == "WhisperNet");
    }

    [Fact]
    public async Task UpdateSettings_RejectsUnavailableEngine()
    {
        await using var unavailableFactory = new TestWebApplicationFactory(
        [
            new NoOpTranscriptionEngine("WhisperNet", ["tiny", "base"], "worker missing"),
        ]);

        var response = await unavailableFactory.Client.PutAsJsonAsync("/api/settings", new
        {
            defaultEngine = "WhisperNet",
            defaultModel = "tiny",
            defaultLanguageMode = "Auto",
            defaultLanguageCode = (string?)null,
            defaultAudioNormalizationEnabled = true,
            defaultDiarizationEnabled = false,
            defaultDiarizationMode = "Basic",
            defaultTranscriptViewMode = "Readable",
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetSettings_NormalizesLegacyUnsupportedDefaultEngine()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Persistence.AppDbContext>();
        var settings = await db.GlobalSettings.SingleAsync();
        settings.DefaultEngine = "Whisper";
        settings.DefaultModel = "medium";
        await db.SaveChangesAsync();

        var response = await _client.GetAsync("/api/settings");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<GlobalSettingsDto>();
        payload.Should().NotBeNull();
        payload!.DefaultEngine.Should().Be("WhisperNet");
        payload.DefaultModel.Should().Be("medium");
    }
}
