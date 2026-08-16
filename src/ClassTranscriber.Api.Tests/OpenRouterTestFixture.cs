using System.Net;
using System.Net.Http.Json;
using ClassTranscriber.Api.Contracts;
using ClassTranscriber.Api.Domain;
using ClassTranscriber.Api.Media;
using ClassTranscriber.Api.Transcription;
using ClassTranscriber.Api.Transcription.SpeechToText;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ClassTranscriber.Api.Tests;

public abstract class OpenRouterTestFixture : IDisposable
{
    private readonly List<string> _tempDirectories = [];

    protected OpenRouterTranscriptionEngine CreateEngine(
        RecordingHttpClientFactory factory,
        string apiKey = "test-openrouter-key",
        string baseUrl = "https://openrouter.ai/api/v1",
        IHostedAudioPreparationService? preparationService = null,
        IMediaInspector? mediaInspector = null,
        ILogger<OpenRouterTranscriptionEngine>? logger = null)
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
            preparationService ?? CreatePreparationService(),
            mediaInspector ?? new StubMediaInspector(2_000),
            logger ?? NullLogger<OpenRouterTranscriptionEngine>.Instance);
    }

    protected static ProjectSettings VerifiedSettings() => new()
    {
        Engine = "OpenRouter",
        Model = "openai/whisper-large-v3-turbo",
        LanguageMode = "Auto",
    };

    protected static HttpResponseMessage WordResponse(decimal cost, params object[] words) => new(HttpStatusCode.OK)
    {
        Content = JsonContent.Create(new { text = "ignored provider text", words, usage = new { cost } }),
    };

    protected HostedAudioPreparationService CreatePreparationService(
        long encodedLength = 1,
        IHostedFlacEncoder? encoder = null)
        => new(
            encoder ?? new IntervalLengthEncoder(_ => encodedLength),
            CreateTempDirectory(),
            NullLogger<HostedAudioPreparationService>.Instance);

    protected static async Task<string> ReadPartAsync(MultipartFormDataContent content, string name)
    {
        var part = content.Single(item => item.Headers.ContentDisposition?.Name?.Trim('"') == name);
        return await part.ReadAsStringAsync();
    }

    protected string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"transcriptlab-openrouter-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        _tempDirectories.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var path in _tempDirectories)
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
    }

    protected sealed class RecordingHttpClientFactory : IHttpClientFactory
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _createResponse;

        public RecordingHttpClientFactory(Func<HttpRequestMessage, HttpResponseMessage> createResponse)
            : this((request, _) => Task.FromResult(createResponse(request)))
        {
        }

        public RecordingHttpClientFactory(
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> createResponse)
        {
            _createResponse = createResponse;
        }

        public List<RecordedRequest> Requests { get; } = [];
        public string AuthorizationParameter { get; set; } = "test-openrouter-key";

        public HttpClient CreateClient(string name)
            => new(new RecordingHttpMessageHandler((request, cancellationToken) =>
            {
                Requests.Add(new RecordedRequest(
                    request.Method,
                    request.RequestUri?.ToString(),
                    request.Headers.Authorization?.Parameter));
                return _createResponse(request, cancellationToken);
            }))
            {
                BaseAddress = new Uri("https://openrouter.ai/api/v1/"),
                DefaultRequestHeaders =
                {
                    Authorization = new("Bearer", AuthorizationParameter),
                },
            };
    }

    protected sealed record RecordedRequest(
        HttpMethod Method,
        string? RequestUri,
        string? AuthorizationParameter);

    private sealed class RecordingHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> createResponse) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => createResponse(request, cancellationToken);
    }

    protected sealed class StubMediaInspector(long durationMs) : IMediaInspector
    {
        public Task<MediaInfo?> InspectAsync(string filePath, CancellationToken ct = default)
            => Task.FromResult<MediaInfo?>(new MediaInfo(durationMs, MediaType.Audio, "audio/wav"));
    }

    protected sealed class StubOpenVinoSidecarManager : IOpenVinoWhisperSidecarManager
    {
        public string BaseUrl => "http://127.0.0.1:9999";

        public Task EnsureStartedAsync(CancellationToken ct) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    protected sealed class IntervalLengthEncoder(Func<HostedAudioInterval, long> getLength) : IHostedFlacEncoder
    {
        public List<HostedAudioInterval> Intervals { get; } = [];

        public Task EncodeWholeAsync(string inputPath, string outputPath, CancellationToken ct)
            => throw new NotSupportedException();

        public Task EncodeIntervalAsync(
            string inputPath,
            string outputPath,
            HostedAudioInterval interval,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Intervals.Add(interval);
            using var stream = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            stream.SetLength(getLength(interval));
            return Task.CompletedTask;
        }
    }
}
