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
            defaultSpeakerRoleAttributionEnabled = true,
            defaultTranscriptViewMode = "Timestamped"
        };

        var response = await _client.PutAsJsonAsync("/api/settings", update);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var settings = await response.Content.ReadFromJsonAsync<GlobalSettingsDto>();
        settings!.DefaultModel.Should().Be("medium");
        settings.DefaultLanguageMode.Should().Be("Fixed");
        settings.DefaultLanguageCode.Should().Be("es");
        settings.DefaultDiarizationEnabled.Should().BeTrue();
        settings.DefaultSpeakerRoleAttributionEnabled.Should().BeTrue();
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
        options.Engines.Should().ContainSingle(engine => engine.Engine == "WhisperNetCoreML");
        options.Engines.Single(engine => engine.Engine == "SherpaOnnx").Models.Should().Contain(new[] { "small", "medium" });
        options.Engines.Single(engine => engine.Engine == "SherpaOnnxSenseVoice").Models.Should().ContainSingle().Which.Should().Be("small");
        options.Engines.Single(engine => engine.Engine == "WhisperNetCuda").Models.Should().Contain(new[] { "tiny", "base", "small", "medium", "large", "large-v3-turbo" });
        options.Engines.Single(engine => engine.Engine == "WhisperNetCoreML").Models.Should().Contain(new[] { "tiny", "base", "small", "medium", "large", "large-v3-turbo" });
        options.Engines.Should().OnlyContain(engine => engine.ProviderDiarizationModels.Length == 0);
    }

    [Fact]
    public async Task GetSettingsOptions_AdvertisesProviderDiarizationPerEngineModel()
    {
        await using var providerFactory = new TestWebApplicationFactory(
        [
            new NoOpTranscriptionEngine("WhisperNet", ["small"]),
            new NoOpTranscriptionEngine("Xai", ["grok-stt-1.0"], providerDiarizationModels: ["grok-stt-1.0"]),
        ]);

        var response = await providerFactory.Client.GetAsync("/api/settings/options");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var options = await response.Content.ReadFromJsonAsync<TranscriptionOptionsDto>();
        options.Should().NotBeNull();
        options!.Engines.Single(engine => engine.Engine == "Xai").ProviderDiarizationModels
            .Should().Equal("grok-stt-1.0");
        options.Engines.Single(engine => engine.Engine == "WhisperNet").ProviderDiarizationModels
            .Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateSettings_RejectsProviderDiarizationWhenModelDoesNotSupportIt()
    {
        var response = await _client.PutAsJsonAsync("/api/settings", new
        {
            defaultEngine = "WhisperNet",
            defaultModel = "small",
            defaultLanguageMode = "Auto",
            defaultLanguageCode = (string?)null,
            defaultAudioNormalizationEnabled = true,
            defaultDiarizationEnabled = true,
            defaultDiarizationSource = "Provider",
            defaultDiarizationMode = "Basic",
            defaultTranscriptViewMode = "Readable",
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("does not support provider diarization");
    }

    [Fact]
    public async Task UpdateSettings_AcceptsXaiForVerifiedOpenRouterModel()
    {
        await using var xaiFactory = CreateXaiDiarizationFactory();

        var response = await xaiFactory.Client.PutAsJsonAsync("/api/settings", new
        {
            defaultEngine = "OpenRouter",
            defaultModel = "openai/whisper-large-v3",
            defaultLanguageMode = "Auto",
            defaultLanguageCode = (string?)null,
            defaultAudioNormalizationEnabled = true,
            defaultDiarizationEnabled = true,
            defaultDiarizationSource = "Xai",
            defaultDiarizationMode = "Basic",
            defaultTranscriptViewMode = "Readable",
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var settings = await response.Content.ReadFromJsonAsync<GlobalSettingsDto>();
        settings.Should().NotBeNull();
        settings!.DefaultDiarizationSource.Should().Be("Xai");
    }

    [Fact]
    public async Task UpdateSettings_AcceptsProviderForDirectXai()
    {
        await using var xaiFactory = CreateXaiDiarizationFactory();

        var response = await xaiFactory.Client.PutAsJsonAsync("/api/settings", new
        {
            defaultEngine = "Xai",
            defaultModel = "grok-stt-1.0",
            defaultLanguageMode = "Auto",
            defaultLanguageCode = (string?)null,
            defaultAudioNormalizationEnabled = true,
            defaultDiarizationEnabled = true,
            defaultDiarizationSource = "Provider",
            defaultDiarizationMode = "Basic",
            defaultTranscriptViewMode = "Readable",
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var settings = await response.Content.ReadFromJsonAsync<GlobalSettingsDto>();
        settings!.DefaultDiarizationSource.Should().Be("Provider");
    }

    [Fact]
    public async Task UpdateSettings_RejectsXaiSourceForDirectXaiEngine()
    {
        await using var xaiFactory = CreateXaiDiarizationFactory();

        var response = await xaiFactory.Client.PutAsJsonAsync("/api/settings", new
        {
            defaultEngine = "Xai",
            defaultModel = "grok-stt-1.0",
            defaultLanguageMode = "Auto",
            defaultLanguageCode = (string?)null,
            defaultAudioNormalizationEnabled = true,
            defaultDiarizationEnabled = true,
            defaultDiarizationSource = "Xai",
            defaultDiarizationMode = "Basic",
            defaultTranscriptViewMode = "Readable",
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateSettings_RejectsXaiWhenDirectXaiIsUnavailable()
    {
        await using var openRouterOnlyFactory = new TestWebApplicationFactory(
        [
            new NoOpTranscriptionEngine(
                "OpenRouter",
                ["openai/whisper-large-v3"],
                wordTimestampModels: ["openai/whisper-large-v3"]),
        ]);

        var response = await openRouterOnlyFactory.Client.PutAsJsonAsync("/api/settings", new
        {
            defaultEngine = "OpenRouter",
            defaultModel = "openai/whisper-large-v3",
            defaultLanguageMode = "Auto",
            defaultLanguageCode = (string?)null,
            defaultAudioNormalizationEnabled = true,
            defaultDiarizationEnabled = true,
            defaultDiarizationSource = "Xai",
            defaultDiarizationMode = "Basic",
            defaultTranscriptViewMode = "Readable",
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetSettingsOptions_DoesNotAdvertiseXaiDiarizationWhenDirectXaiApiKeyIsEmpty()
    {
        await using var unavailableFactory = CreateUnavailableXaiDiarizationFactory();

        var response = await unavailableFactory.Client.GetAsync("/api/settings/options");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var options = await response.Content.ReadFromJsonAsync<TranscriptionOptionsDto>();
        options!.XaiDiarizationAvailable.Should().BeFalse();
        options.Engines.Should().NotContain(engine => engine.Engine == "Xai");
    }

    [Fact]
    public async Task UpdateSettings_RejectsXaiWhenDirectXaiApiKeyIsEmptyAndBaseUrlIsValidHttps()
    {
        await using var unavailableFactory = CreateUnavailableXaiDiarizationFactory();

        var response = await unavailableFactory.Client.PutAsJsonAsync("/api/settings", new
        {
            defaultEngine = "OpenRouter",
            defaultModel = "openai/whisper-large-v3",
            defaultLanguageMode = "Auto",
            defaultLanguageCode = (string?)null,
            defaultAudioNormalizationEnabled = true,
            defaultDiarizationEnabled = true,
            defaultDiarizationSource = "Xai",
            defaultDiarizationMode = "Basic",
            defaultTranscriptViewMode = "Readable",
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("openai/gpt-4o-mini-transcribe", "Xai")]
    [InlineData("deepgram/nova-3", "Xai")]
    [InlineData("openai/whisper-large-v3", "Bogus")]
    public async Task UpdateSettings_RejectsUnsupportedXaiSource(string model, string source)
    {
        await using var xaiFactory = CreateXaiDiarizationFactory();

        var response = await xaiFactory.Client.PutAsJsonAsync("/api/settings", new
        {
            defaultEngine = "OpenRouter",
            defaultModel = model,
            defaultLanguageMode = "Auto",
            defaultLanguageCode = (string?)null,
            defaultAudioNormalizationEnabled = true,
            defaultDiarizationEnabled = true,
            defaultDiarizationSource = source,
            defaultDiarizationMode = "Basic",
            defaultTranscriptViewMode = "Readable",
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("validation_error");
    }

    [Fact]
    public async Task UpdateSettings_RejectsXaiWhenOpenRouterModelAdvertisesNativeDiarization()
    {
        await using var xaiFactory = new TestWebApplicationFactory(
        [
            new NoOpTranscriptionEngine(
                "OpenRouter",
                ["openai/whisper-large-v3"],
                providerDiarizationModels: ["openai/whisper-large-v3"],
                wordTimestampModels: ["openai/whisper-large-v3"]),
            new NoOpTranscriptionEngine(
                "Xai",
                ["grok-stt-1.0"],
                providerDiarizationModels: ["grok-stt-1.0"],
                wordTimestampModels: ["grok-stt-1.0"]),
        ]);

        var response = await xaiFactory.Client.PutAsJsonAsync("/api/settings", new
        {
            defaultEngine = "OpenRouter",
            defaultModel = "openai/whisper-large-v3",
            defaultLanguageMode = "Auto",
            defaultLanguageCode = (string?)null,
            defaultAudioNormalizationEnabled = true,
            defaultDiarizationEnabled = true,
            defaultDiarizationSource = "Xai",
            defaultDiarizationMode = "Basic",
            defaultTranscriptViewMode = "Readable",
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetSettingsOptions_AdvertisesWordModelsAndXaiDiarizationAvailability()
    {
        await using var xaiFactory = CreateXaiDiarizationFactory();

        var response = await xaiFactory.Client.GetAsync("/api/settings/options");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var options = await response.Content.ReadFromJsonAsync<TranscriptionOptionsDto>();
        options.Should().NotBeNull();
        options!.Engines.Single(engine => engine.Engine == "OpenRouter").WordTimestampModels
            .Should().Equal("openai/whisper-large-v3", "openai/whisper-large-v3-turbo");
        options.XaiDiarizationAvailable.Should().BeTrue();
        options.XaiDiarizationModel.Should().Be("grok-stt-1.0");
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
        catalog.Models.Should().Contain(entry => entry.Engine == "WhisperNetCoreML" && entry.Model == "large-v3-turbo");
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
    public async Task ManageTranscriptionModel_ProbeInstalledCoreMLModelWithoutEncoder_ReturnsFailed()
    {
        var whisperNetOptions = _factory.Services.GetRequiredService<IOptions<WhisperNetOptions>>().Value;
        var installPath = GgmlModelDownloads.GetModelPath(whisperNetOptions.ModelsPath, "small");
        Directory.CreateDirectory(Path.GetDirectoryName(installPath)!);
        await File.WriteAllBytesAsync(installPath, []);

        var response = await _client.PostAsJsonAsync("/api/settings/models/manage", new
        {
            engine = "WhisperNetCoreML",
            model = "small",
            action = "Probe",
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var entry = await response.Content.ReadFromJsonAsync<TranscriptionModelEntryDto>();
        entry.Should().NotBeNull();
        entry!.ProbeState.Should().Be("Failed");
        entry.ProbeMessage.Should().Contain("ggml-small-encoder.mlmodelc");
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

    [Fact]
    public async Task GetSettings_CoercesStaleXaiSourceToLocal()
    {
        await using var openRouterOnlyFactory = new TestWebApplicationFactory(
        [
            new NoOpTranscriptionEngine(
                "OpenRouter",
                ["openai/whisper-large-v3"],
                wordTimestampModels: ["openai/whisper-large-v3"]),
        ]);
        await using (var scope = openRouterOnlyFactory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Persistence.AppDbContext>();
            var settings = await db.GlobalSettings.SingleAsync();
            settings.DefaultEngine = "OpenRouter";
            settings.DefaultModel = "openai/whisper-large-v3";
            settings.DefaultDiarizationSource = "Xai";
            await db.SaveChangesAsync();
        }

        var response = await openRouterOnlyFactory.Client.GetAsync("/api/settings");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var settingsPayload = await response.Content.ReadFromJsonAsync<GlobalSettingsDto>();
        settingsPayload!.DefaultDiarizationSource.Should().Be("Local");
    }

    private static TestWebApplicationFactory CreateXaiDiarizationFactory()
        => new(
        [
            new NoOpTranscriptionEngine(
                "OpenRouter",
                [
                    "openai/whisper-large-v3",
                    "openai/whisper-large-v3-turbo",
                    "openai/gpt-4o-mini-transcribe",
                    "deepgram/nova-3",
                ],
                wordTimestampModels: ["openai/whisper-large-v3", "openai/whisper-large-v3-turbo"]),
            new NoOpTranscriptionEngine(
                "Xai",
                ["grok-stt-1.0"],
                providerDiarizationModels: ["grok-stt-1.0"],
                wordTimestampModels: ["grok-stt-1.0"]),
        ]);

    internal static TestWebApplicationFactory CreateUnavailableXaiDiarizationFactory()
        => new(
        [
            new NoOpTranscriptionEngine(
                "OpenRouter",
                ["openai/whisper-large-v3", "openai/whisper-large-v3-turbo"],
                wordTimestampModels: ["openai/whisper-large-v3", "openai/whisper-large-v3-turbo"]),
            new NoOpTranscriptionEngine(
                "Xai",
                ["grok-stt-1.0"],
                "xAI engine requires Transcription:Xai:ApiKey to be set.",
                providerDiarizationModels: ["grok-stt-1.0"],
                wordTimestampModels: ["grok-stt-1.0"]),
        ]);
}
