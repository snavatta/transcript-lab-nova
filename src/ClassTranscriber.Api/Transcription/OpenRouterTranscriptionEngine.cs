using System.Text.Json;
using System.Text.Json.Serialization;
using ClassTranscriber.Api.Contracts;
using ClassTranscriber.Api.Domain;
using ClassTranscriber.Api.Transcription.SpeechToText;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ClassTranscriber.Api.Transcription;

public sealed class OpenRouterOptions
{
    public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1";
    public string ApiKey { get; set; } = string.Empty;
    public string[] FallbackModels { get; set; } = ["openai/whisper-large-v3"];
    public int TimeoutSeconds { get; set; } = 120;
}

public sealed class OpenRouterTranscriptionEngine : IRegisteredTranscriptionEngine
{
    public const string HttpClientName = "OpenRouter";

    private readonly OpenRouterOptions _options;
    private readonly ISpeechToTextClient _speechToTextClient;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<OpenRouterTranscriptionEngine> _logger;

    public OpenRouterTranscriptionEngine(
        IOptions<OpenRouterOptions> options,
        [FromKeyedServices("OpenRouter")] ISpeechToTextClient speechToTextClient,
        IHttpClientFactory httpClientFactory,
        ILogger<OpenRouterTranscriptionEngine> logger)
    {
        _options = options.Value;
        _speechToTextClient = speechToTextClient;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public string EngineId => "OpenRouter";

    public IReadOnlyCollection<string> SupportedModels
    {
        get
        {
            if (GetAvailabilityError() is not null)
                return [];

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var client = _httpClientFactory.CreateClient(HttpClientName);
                using var response = client.GetAsync("models?output_modalities=transcription", cts.Token)
                    .GetAwaiter().GetResult();

                if (response.IsSuccessStatusCode)
                {
                    var json = response.Content.ReadAsStringAsync(cts.Token).GetAwaiter().GetResult();
                    var catalog = JsonSerializer.Deserialize<OpenRouterModelListResponse>(json);
                    var models = catalog?.Data?
                        .Select(model => model.Id)
                        .Where(model => !string.IsNullOrWhiteSpace(model))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    if (models is { Length: > 0 })
                        return models;
                }
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException)
            {
            }
            catch (JsonException)
            {
            }

            return GetFallbackModels();
        }
    }

    public string? GetAvailabilityError()
    {
        if (string.IsNullOrWhiteSpace(_options.BaseUrl))
            return "OpenRouter engine requires Transcription:OpenRouter:BaseUrl to be set.";
        if (!Uri.TryCreate(_options.BaseUrl, UriKind.Absolute, out var baseUri)
            || !string.Equals(baseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return "OpenRouter engine requires Transcription:OpenRouter:BaseUrl to be an absolute HTTPS URL.";
        }
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            return "OpenRouter engine requires Transcription:OpenRouter:ApiKey to be set.";
        if (GetFallbackModels().Count == 0)
            return "OpenRouter engine requires at least one Transcription:OpenRouter:FallbackModels entry.";
        return null;
    }

    public string? GetProbeError()
    {
        var availabilityError = GetAvailabilityError();
        if (availabilityError is not null)
            return availabilityError;

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = client.GetAsync("models?output_modalities=transcription", cts.Token)
                .GetAwaiter().GetResult();
            return response.IsSuccessStatusCode
                ? null
                : $"OpenRouter models endpoint returned HTTP {(int)response.StatusCode}.";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return $"OpenRouter models endpoint is not reachable: {ex.Message}";
        }
    }

    public async Task<TranscriptionResult> TranscribeAsync(
        string audioPath,
        ProjectSettings settings,
        CancellationToken ct = default)
    {
        var availabilityError = GetAvailabilityError();
        if (availabilityError is not null)
            throw new InvalidOperationException(availabilityError);

        _logger.LogInformation(
            "Starting {Engine} transcription for {AudioPath} with model {Model}",
            EngineId,
            audioPath,
            settings.Model);

        await using var audioStream = File.OpenRead(audioPath);
        var speechOptions = new SpeechToTextOptions { ModelId = settings.Model };
        if (string.Equals(settings.LanguageMode, "Fixed", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(settings.LanguageCode))
        {
            speechOptions.SpeechLanguage = settings.LanguageCode;
        }

        var response = await _speechToTextClient.GetTextAsync(audioStream, speechOptions, ct);
        var result = MapResponse(response);

        _logger.LogInformation(
            "{Engine} transcription completed: {SegmentCount} segments",
            EngineId,
            result.Segments.Length);

        return result;
    }

    private IReadOnlyCollection<string> GetFallbackModels()
        => _options.FallbackModels
            .Where(model => !string.IsNullOrWhiteSpace(model))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static TranscriptionResult MapResponse(SpeechToTextResponse response)
    {
        if (response.RawRepresentation is not OpenAiVerboseTranscriptionResponse raw)
        {
            var fallbackSegments = string.IsNullOrWhiteSpace(response.Text)
                ? []
                : new[] { new TranscriptSegmentDto { StartMs = 0, EndMs = 0, Text = response.Text } };
            return new TranscriptionResult(response.Text, fallbackSegments, null, null);
        }

        var durationSeconds = raw.Duration > 0 ? raw.Duration : raw.Usage?.Seconds;
        var durationMs = durationSeconds is > 0 ? (long?)(durationSeconds.Value * 1000) : null;
        var segments = raw.Segments is { Length: > 0 }
            ? raw.Segments.Select(segment => new TranscriptSegmentDto
            {
                StartMs = (long)(segment.Start * 1000),
                EndMs = (long)(segment.End * 1000),
                Text = segment.Text,
            }).ToArray()
            : string.IsNullOrWhiteSpace(response.Text)
                ? []
                : [new TranscriptSegmentDto { StartMs = 0, EndMs = durationMs ?? 0, Text = response.Text }];

        return new TranscriptionResult(response.Text, segments, raw.Language, durationMs);
    }
}

file sealed class OpenRouterModelListResponse
{
    [JsonPropertyName("data")] public List<OpenRouterModelEntry>? Data { get; set; }
}

file sealed class OpenRouterModelEntry
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
}
