using System.Net;
using System.Net.Http.Json;
using ClassTranscriber.Api.Domain;
using ClassTranscriber.Api.Transcription;
using ClassTranscriber.Api.Transcription.SpeechToText;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ClassTranscriber.Api.Tests;

public sealed class OpenRouterTranscriptionEngineTests
{
    [Fact]
    public void GetAvailabilityError_ReturnsConfigurationError_WhenApiKeyIsMissing()
    {
        var factory = new RecordingHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var engine = CreateEngine(factory, apiKey: string.Empty);

        var error = engine.GetAvailabilityError();

        error.Should().Contain("Transcription:OpenRouter:ApiKey");
    }

    [Fact]
    public void GetAvailabilityError_RequiresHttpsBaseUrl()
    {
        var factory = new RecordingHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var engine = CreateEngine(factory, baseUrl: "http://openrouter.ai/api/v1");

        var error = engine.GetAvailabilityError();

        error.Should().Contain("absolute HTTPS URL");
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

        var models = engine.SupportedModels;

        models.Should().Equal("openai/whisper-large-v3", "openai/gpt-4o-mini-transcribe");
        factory.Requests.Should().ContainSingle();
        factory.Requests[0].RequestUri.Should().Be("https://openrouter.ai/api/v1/models?output_modalities=transcription");
    }

    [Fact]
    public void SupportedModels_UsesConfiguredFallback_WhenCatalogIsUnavailable()
    {
        var factory = new RecordingHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var engine = CreateEngine(factory);

        var models = engine.SupportedModels;

        models.Should().Equal("openai/whisper-large-v3");
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
                    segments = new[]
                    {
                        new { id = 0, start = 0.25, end = 2.5, text = "hola mundo" },
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
    }

    [Fact]
    public async Task TranscribeAsync_RetriesWithJson_WhenProviderRejectsVerboseJson()
    {
        var audioPath = Path.Combine(CreateTempDirectory(), "lecture.wav");
        await File.WriteAllBytesAsync(audioPath, [1, 2, 3]);
        var responseFormats = new List<string>();
        var factory = new RecordingHttpClientFactory(request =>
        {
            var multipart = request.Content.Should().BeOfType<MultipartFormDataContent>().Subject;
            var responseFormat = ReadPartAsync(multipart, "response_format").GetAwaiter().GetResult();
            responseFormats.Add(responseFormat);
            return responseFormat == "verbose_json"
                ? new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = JsonContent.Create(new { error = new { message = "response_format 'verbose_json' is unsupported" } }),
                }
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        text = "provider fallback",
                        usage = new { seconds = 4.2 },
                    }),
                };
        });
        var engine = CreateEngine(factory);

        var result = await engine.TranscribeAsync(audioPath, new ProjectSettings
        {
            Engine = "OpenRouter",
            Model = "deepgram/nova-3",
            LanguageMode = "Auto",
        });

        responseFormats.Should().Equal("verbose_json", "json");
        result.PlainText.Should().Be("provider fallback");
        result.Segments.Should().ContainSingle();
        result.Segments[0].EndMs.Should().Be(4200);
        result.DurationMs.Should().Be(4200);
    }

    [Fact]
    public async Task TranscribeAsync_DoesNotRetryUnrelatedBadRequests()
    {
        var audioPath = Path.Combine(CreateTempDirectory(), "lecture.wav");
        await File.WriteAllBytesAsync(audioPath, [1, 2, 3]);
        var factory = new RecordingHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = JsonContent.Create(new { error = new { message = "Invalid language" } }),
        });
        var engine = CreateEngine(factory);

        var act = () => engine.TranscribeAsync(audioPath, new ProjectSettings
        {
            Engine = "OpenRouter",
            Model = "deepgram/nova-3",
            LanguageMode = "Fixed",
            LanguageCode = "invalid",
        });

        await act.Should().ThrowAsync<InvalidOperationException>();
        factory.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task TranscribeAsync_DoesNotLeakApiKey_WhenOpenRouterReturnsAnError()
    {
        const string apiKey = "sensitive-openrouter-key";
        var audioPath = Path.Combine(CreateTempDirectory(), "lecture.wav");
        await File.WriteAllBytesAsync(audioPath, [1, 2, 3]);
        var factory = new RecordingHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = JsonContent.Create(new { error = new { message = $"Invalid API key: {apiKey}" } }),
        });
        var engine = CreateEngine(factory, apiKey);

        var act = () => engine.TranscribeAsync(audioPath, new ProjectSettings
        {
            Engine = "OpenRouter",
            Model = "openai/whisper-large-v3",
            LanguageMode = "Auto",
        });

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().NotContain(apiKey);
        factory.Requests.Should().ContainSingle();
        factory.Requests[0].AuthorizationParameter.Should().Be(apiKey);
    }

    private static OpenRouterTranscriptionEngine CreateEngine(
        RecordingHttpClientFactory factory,
        string apiKey = "test-openrouter-key",
        string baseUrl = "https://openrouter.ai/api/v1")
    {
        factory.AuthorizationParameter = apiKey;
        var options = Options.Create(new OpenRouterOptions
        {
            BaseUrl = baseUrl,
            ApiKey = apiKey,
            FallbackModels = ["openai/whisper-large-v3"],
        });
        var client = new OpenRouterSpeechToTextClient(factory);
        return new OpenRouterTranscriptionEngine(
            options,
            client,
            factory,
            NullLogger<OpenRouterTranscriptionEngine>.Instance);
    }

    private static async Task<string> ReadPartAsync(MultipartFormDataContent content, string name)
    {
        var part = content.Single(item => item.Headers.ContentDisposition?.Name?.Trim('"') == name);
        return await part.ReadAsStringAsync();
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"transcriptlab-openrouter-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class RecordingHttpClientFactory(
        Func<HttpRequestMessage, HttpResponseMessage> createResponse) : IHttpClientFactory
    {
        public List<RecordedRequest> Requests { get; } = [];
        public string AuthorizationParameter { get; set; } = "test-openrouter-key";

        public HttpClient CreateClient(string name)
            => new(new RecordingHttpMessageHandler(request =>
            {
                Requests.Add(new RecordedRequest(
                    request.Method,
                    request.RequestUri?.ToString(),
                    request.Headers.Authorization?.Parameter));
                return createResponse(request);
            }))
            {
                BaseAddress = new Uri("https://openrouter.ai/api/v1/"),
                DefaultRequestHeaders =
                {
                    Authorization = new("Bearer", AuthorizationParameter),
                },
            };
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        string? RequestUri,
        string? AuthorizationParameter);

    private sealed class RecordingHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> createResponse) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(createResponse(request));
    }
}
