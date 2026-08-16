using System.Text.Json;
using System.Text.Json.Serialization;
using ClassTranscriber.Api.Contracts;
using ClassTranscriber.Api.Domain;
using ClassTranscriber.Api.Media;
using ClassTranscriber.Api.Transcription.SpeechToText;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ClassTranscriber.Api.Transcription;

public sealed record OpenRouterChunkProgress(
    int ChunkIndex,
    int ChunkCount,
    long CoreStartMs,
    long CoreEndMs,
    TranscriptionResult CumulativeResult);

public interface IOpenRouterChunkProgressTranscriptionEngine : ITranscriptionEngine
{
    Task<TranscriptionResult> TranscribeAsync(
        string audioPath,
        ProjectSettings settings,
        Func<OpenRouterChunkProgress, CancellationToken, ValueTask>? onChunkSucceeded,
        CancellationToken ct = default);
}

public sealed class OpenRouterOptions
{
    public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1";
    public string ApiKey { get; set; } = string.Empty;
    public string[] FallbackModels { get; set; } = ["openai/whisper-large-v3"];
    public int TimeoutSeconds { get; set; } = 120;
    public string SpeakerRoleModel { get; set; } = OpenRouterSpeakerRoleAttributionService.DefaultModel;
}

public sealed class OpenRouterTranscriptionEngine :
    IRegisteredTranscriptionEngine,
    IOpenRouterChunkProgressTranscriptionEngine
{
    public const string HttpClientName = "OpenRouter";

    private readonly OpenRouterOptions _options;
    private readonly ISpeechToTextClient _speechToTextClient;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly OpenRouterLongFormTranscriber _longFormTranscriber;
    private readonly ILogger<OpenRouterTranscriptionEngine> _logger;

    public OpenRouterTranscriptionEngine(
        IOptions<OpenRouterOptions> options,
        [FromKeyedServices("OpenRouter")] ISpeechToTextClient speechToTextClient,
        IHttpClientFactory httpClientFactory,
        IHostedAudioPreparationService hostedAudioPreparationService,
        IMediaInspector mediaInspector,
        ILogger<OpenRouterTranscriptionEngine> logger)
    {
        _options = options.Value;
        _speechToTextClient = speechToTextClient;
        var openRouterSpeechToTextClient = speechToTextClient as OpenRouterSpeechToTextClient
            ?? throw new ArgumentException("The OpenRouter speech client is invalid.", nameof(speechToTextClient));
        _httpClientFactory = httpClientFactory;
        _longFormTranscriber = new OpenRouterLongFormTranscriber(
            openRouterSpeechToTextClient,
            hostedAudioPreparationService,
            mediaInspector);
        _logger = logger;
    }

    public string EngineId => "OpenRouter";

    public IReadOnlyCollection<string> WordTimestampModels =>
        ["openai/whisper-large-v3", "openai/whisper-large-v3-turbo"];

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
                using var request = new HttpRequestMessage(
                    HttpMethod.Get,
                    "models?output_modalities=transcription");
                using var response = client.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        cts.Token)
                    .GetAwaiter().GetResult();

                if (response.IsSuccessStatusCode)
                {
                    var catalog = BoundedHttpContentReader.ReadJsonAsync<OpenRouterModelListResponse>(
                            response.Content,
                            "OpenRouter model catalog response exceeded the maximum allowed size.",
                            cts.Token)
                        .GetAwaiter().GetResult();
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
            catch (ProviderResponseTooLargeException)
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
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                "models?output_modalities=transcription");
            using var response = client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cts.Token)
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

    public Task<TranscriptionResult> TranscribeAsync(
        string audioPath,
        ProjectSettings settings,
        CancellationToken ct = default) =>
        TranscribeAsync(audioPath, settings, onChunkSucceeded: null, ct);

    public async Task<TranscriptionResult> TranscribeAsync(
        string audioPath,
        ProjectSettings settings,
        Func<OpenRouterChunkProgress, CancellationToken, ValueTask>? onChunkSucceeded,
        CancellationToken ct = default)
    {
        var availabilityError = GetAvailabilityError();
        if (availabilityError is not null)
            throw new InvalidOperationException(availabilityError);

        _logger.LogInformation(
            "Starting {Engine} transcription with model {Model}",
            EngineId,
            settings.Model);

        if (settings.DiarizationEnabled
            && string.Equals(settings.DiarizationSource, "Xai", StringComparison.OrdinalIgnoreCase)
            && !IsVerifiedWordModel(settings.Model))
        {
            throw new InvalidOperationException("OpenRouter word timestamps require a verified model.");
        }

        if (IsVerifiedWordModel(settings.Model))
            return await _longFormTranscriber.TranscribeAsync(audioPath, settings, onChunkSucceeded, ct);

        await using var audioStream = File.OpenRead(audioPath);
        var speechOptions = new SpeechToTextOptions { ModelId = settings.Model };
        if (string.Equals(settings.LanguageMode, "Fixed", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(settings.LanguageCode))
        {
            speechOptions.SpeechLanguage = settings.LanguageCode;
        }

        var response = await _speechToTextClient.GetTextAsync(audioStream, speechOptions, ct);
        var result = OpenRouterTranscriptionResultMapper.Map(response, settings.Model);

        _logger.LogInformation(
            "{Engine} transcription completed: {SegmentCount} segments",
            EngineId,
            result.Segments.Length);

        return result;
    }

    private bool IsVerifiedWordModel(string model) =>
        WordTimestampModels.Contains(model, StringComparer.OrdinalIgnoreCase);

    private IReadOnlyCollection<string> GetFallbackModels()
        => _options.FallbackModels
            .Where(model => !string.IsNullOrWhiteSpace(model))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

}

file sealed class OpenRouterModelListResponse
{
    [JsonPropertyName("data")] public List<OpenRouterModelEntry>? Data { get; set; }
}

file sealed class OpenRouterModelEntry
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
}
