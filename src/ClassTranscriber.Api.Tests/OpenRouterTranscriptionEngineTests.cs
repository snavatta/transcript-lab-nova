using System.Net;
using System.Net.Http.Json;
using System.Collections.Concurrent;
using ClassTranscriber.Api.Domain;
using ClassTranscriber.Api.Transcription;
using FluentAssertions;
using Microsoft.Extensions.Logging;

namespace ClassTranscriber.Api.Tests;

public sealed class OpenRouterTranscriptionEngineTests : OpenRouterTestFixture
{
    [Fact]
    public void GetAvailabilityError_ReturnsConfigurationError_WhenApiKeyIsMissing()
    {
        var factory = new RecordingHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var engine = CreateEngine(factory, apiKey: string.Empty);

        engine.GetAvailabilityError().Should().Contain("Transcription:OpenRouter:ApiKey");
    }

    [Fact]
    public void GetAvailabilityError_RequiresHttpsBaseUrl()
    {
        var factory = new RecordingHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var engine = CreateEngine(factory, baseUrl: "http://openrouter.ai/api/v1");

        engine.GetAvailabilityError().Should().Contain("absolute HTTPS URL");
    }

    [Fact]
    public void SupportedModels_FiltersOpenRouterCatalogToTranscriptionModels()
    {
        var factory = new RecordingHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                data = new[]
                {
                    new { id = "openai/whisper-large-v3" },
                    new { id = "openai/gpt-4o-mini-transcribe" },
                },
            }),
        });
        var engine = CreateEngine(factory);

        engine.SupportedModels.Should().Equal("openai/whisper-large-v3", "openai/gpt-4o-mini-transcribe");
        factory.Requests.Should().ContainSingle();
        factory.Requests[0].RequestUri.Should().Be("https://openrouter.ai/api/v1/models?output_modalities=transcription");
    }

    [Fact]
    public void SupportedModels_UsesConfiguredFallback_WhenCatalogIsUnavailable()
    {
        var factory = new RecordingHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var engine = CreateEngine(factory);

        engine.SupportedModels.Should().Equal("openai/whisper-large-v3");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SupportedModels_OversizedCatalog_IsBoundedAndUsesFallback(bool declareLength)
    {
        var oversized = new OversizedProviderResponseContent(declareLength);
        var factory = new RecordingHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = oversized,
        });
        var engine = CreateEngine(factory);

        engine.SupportedModels.Should().Equal("openai/whisper-large-v3");
        oversized.Source.BytesRead.Should().Be(
            declareLength ? 0 : OversizedProviderResponseContent.ResponseLimitBytes + 1);
        factory.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task TranscribeAsync_StartLog_DoesNotExposeAbsoluteAudioPath()
    {
        var sensitivePath = Path.Combine(CreateTempDirectory(), "private-customer-recording.wav");
        await File.WriteAllBytesAsync(sensitivePath, [1, 2, 3]);
        var entries = new ConcurrentQueue<CapturedLogEntry>();
        using var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddProvider(new CapturingLoggerProvider(entries)));
        var factory = new RecordingHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { text = "ok", duration = 1.0 }),
        });
        var engine = CreateEngine(
            factory,
            logger: loggerFactory.CreateLogger<OpenRouterTranscriptionEngine>());

        await engine.TranscribeAsync(sensitivePath, new ProjectSettings
        {
            Engine = "OpenRouter",
            Model = "openai/gpt-4o-mini-transcribe",
            LanguageMode = "Auto",
        });

        var start = entries.Should().ContainSingle(entry => entry.Message.StartsWith("Starting OpenRouter"))
            .Subject;
        start.Message.Should().NotContain(sensitivePath).And.NotContain("private-customer-recording.wav");
        start.Properties.Should().NotContainKey("AudioPath");
        start.Properties.Should().Contain("Engine", "OpenRouter");
        start.Properties.Should().Contain("Model", "openai/gpt-4o-mini-transcribe");
    }

    [Fact]
    public async Task TranscribeAsync_UsesProjectModelAndFixedLanguage_AndMapsVerboseSegments()
    {
        var audioPath = Path.Combine(CreateTempDirectory(), "lecture.wav");
        await File.WriteAllBytesAsync(audioPath, [1, 2, 3]);
        var factory = new RecordingHttpClientFactory(request =>
        {
            request.Method.Should().Be(HttpMethod.Post);
            request.RequestUri.Should().Be("https://openrouter.ai/api/v1/audio/transcriptions");
            request.Headers.Authorization?.Scheme.Should().Be("Bearer");
            request.Headers.Authorization?.Parameter.Should().Be("test-openrouter-key");
            var multipart = request.Content.Should().BeOfType<MultipartFormDataContent>().Subject;
            ReadPartAsync(multipart, "model").GetAwaiter().GetResult().Should().Be("openai/gpt-4o-mini-transcribe");
            ReadPartAsync(multipart, "language").GetAwaiter().GetResult().Should().Be("es");
            ReadPartAsync(multipart, "response_format").GetAwaiter().GetResult().Should().Be("verbose_json");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    task = "transcribe",
                    language = "es",
                    duration = 2.5,
                    text = "hola mundo",
                    segments = new[] { new { id = 0, start = 0.25, end = 2.5, text = "hola mundo" } },
                    words = new object[]
                    {
                        new { word = "hola", start = 0.25, end = 0.75 },
                        new { text = "mundo", start = 0.80, end = 1.40 },
                        new { word = "missing-start", end = 1.50 },
                        new { word = "null-end", start = 1.50, end = (double?)null },
                        new { word = "negative", start = -1.0, end = 0.25 },
                    },
                }),
            };
        });
        var engine = CreateEngine(factory);

        var result = await engine.TranscribeAsync(audioPath, new ProjectSettings
        {
            Engine = "OpenRouter",
            Model = "openai/gpt-4o-mini-transcribe",
            LanguageMode = "Fixed",
            LanguageCode = "es",
        });

        result.PlainText.Should().Be("hola mundo");
        result.DetectedLanguage.Should().Be("es");
        result.DurationMs.Should().Be(2500);
        result.Segments.Should().ContainSingle();
        result.Segments[0].StartMs.Should().Be(250);
        result.Segments[0].EndMs.Should().Be(2500);
        result.Words.Should().Equal(
            new TranscriptionWord("hola", 250, 750),
            new TranscriptionWord("mundo", 800, 1400));
    }

    [Fact]
    public async Task MissingWordsRemainEmpty()
    {
        var audioPath = Path.Combine(CreateTempDirectory(), "lecture.wav");
        await File.WriteAllBytesAsync(audioPath, [1, 2, 3]);
        var factory = new RecordingHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { text = "plain response", duration = 2.0 }),
        });
        var engine = CreateEngine(factory);

        var result = await engine.TranscribeAsync(audioPath, new ProjectSettings
        {
            Engine = "OpenRouter",
            Model = "openai/gpt-4o-mini-transcribe",
            LanguageMode = "Auto",
        });

        result.Words.Should().BeEmpty();
    }

    [Fact]
    public async Task SttCostOverflowFails()
    {
        var audioPath = Path.Combine(CreateTempDirectory(), "lecture.wav");
        await File.WriteAllBytesAsync(audioPath, [1, 2, 3]);
        var factory = new RecordingHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                text = "cost overflow",
                duration = 2.0,
                usage = new { cost = decimal.MaxValue },
            }),
        });
        var engine = CreateEngine(factory);

        var act = () => engine.TranscribeAsync(audioPath, new ProjectSettings
        {
            Engine = "OpenRouter",
            Model = "openai/gpt-4o-mini-transcribe",
            LanguageMode = "Auto",
        });

        await act.Should().ThrowAsync<OverflowException>();
    }
}
