using System.Net;
using System.Net.Http.Json;
using ClassTranscriber.Api.Contracts;
using FluentAssertions;

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
        settings!.DefaultEngine.Should().Be("Whisper");
        settings.DefaultModel.Should().Be("small");
    }

    [Fact]
    public async Task UpdateSettings_PersistsChanges()
    {
        var update = new
        {
            defaultEngine = "Whisper",
            defaultModel = "medium",
            defaultLanguageMode = "Fixed",
            defaultLanguageCode = "es",
            defaultAudioNormalizationEnabled = false,
            defaultDiarizationEnabled = true,
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
        options!.Engines.Should().ContainSingle(engine => engine.Engine == "Whisper");
        options.Engines.Should().ContainSingle(engine => engine.Engine == "SherpaOnnx");
        options.Engines.Single(engine => engine.Engine == "SherpaOnnx").Models.Should().Contain(new[] { "small", "medium" });
    }
}
