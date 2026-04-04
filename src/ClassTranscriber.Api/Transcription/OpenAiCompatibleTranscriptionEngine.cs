using System.Text.Json;
using System.Text.Json.Serialization;
using ClassTranscriber.Api.Contracts;
using ClassTranscriber.Api.Domain;
using ClassTranscriber.Api.Transcription.SpeechToText;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ClassTranscriber.Api.Transcription;

// ---------------------------------------------------------------------------
// Options
// ---------------------------------------------------------------------------

public sealed class OpenAiCompatibleOptions
{
    /// <summary>
    /// Base URL of the OpenAI-compatible API (e.g. http://localhost:11434/v1 for Ollama,
    /// or http://localhost:15432 for the local sidecar). Must NOT have a trailing slash.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Optional API key sent as Bearer token. Leave empty for local/unauthenticated endpoints.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Model name/ID to pass to the transcription endpoint.
    /// </summary>
    public string ModelName { get; set; } = string.Empty;

    /// <summary>
    /// HTTP timeout for transcription requests in seconds. Default is 120.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 120;
}

// ---------------------------------------------------------------------------
// Engine
// ---------------------------------------------------------------------------

public sealed class OpenAiCompatibleTranscriptionEngine : IRegisteredTranscriptionEngine
{
    public const string HttpClientName = "OpenAiCompatible";

    private readonly OpenAiCompatibleOptions _options;
    private readonly ISpeechToTextClient _speechToTextClient;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<OpenAiCompatibleTranscriptionEngine> _logger;

    public OpenAiCompatibleTranscriptionEngine(
        IOptions<OpenAiCompatibleOptions> options,
        [FromKeyedServices("OpenAiCompatible")] ISpeechToTextClient speechToTextClient,
        IHttpClientFactory httpClientFactory,
        ILogger<OpenAiCompatibleTranscriptionEngine> logger)
    {
        _options = options.Value;
        _speechToTextClient = speechToTextClient;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public string EngineId => "OpenAiCompatible";

    /// <summary>
    /// Attempts <c>GET {BaseUrl}/v1/models</c> and returns the model IDs from the response.
    /// Falls back to <c>[ModelName]</c> if the endpoint is not reachable or not configured.
    /// </summary>
    public IReadOnlyCollection<string> SupportedModels
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_options.BaseUrl) || string.IsNullOrWhiteSpace(_options.ModelName))
                return [];

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var client = _httpClientFactory.CreateClient(HttpClientName);
                var response = client.GetAsync(
                    $"{_options.BaseUrl.TrimEnd('/')}/v1/models", cts.Token).GetAwaiter().GetResult();

                if (!response.IsSuccessStatusCode)
                    return [_options.ModelName];

                var json = response.Content.ReadAsStringAsync(cts.Token).GetAwaiter().GetResult();
                var list = JsonSerializer.Deserialize<OpenAiModelListResponse>(json);
                if (list?.Data is { Count: > 0 } data)
                    return data.Select(m => m.Id).Where(id => !string.IsNullOrWhiteSpace(id)).ToArray();
            }
            catch
            {
                // Endpoint unavailable — fall back to configured model name.
            }

            return [_options.ModelName];
        }
    }

    public string? GetAvailabilityError()
    {
        if (string.IsNullOrWhiteSpace(_options.BaseUrl))
            return "OpenAiCompatible engine is not configured. Set BaseUrl and ModelName in appsettings.json.";
        if (string.IsNullOrWhiteSpace(_options.ModelName))
            return "OpenAiCompatible engine requires Transcription:OpenAiCompatible:ModelName to be set.";
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
            var response = client.GetAsync($"{_options.BaseUrl.TrimEnd('/')}/v1/models", cts.Token)
                .GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
                return $"OpenAiCompatible endpoint at {_options.BaseUrl}/v1/models returned {(int)response.StatusCode}.";
            return null;
        }
        catch (Exception ex)
        {
            return $"OpenAiCompatible endpoint at {_options.BaseUrl} is not reachable: {ex.Message}";
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
            _options.ModelName);

        await using var audioStream = File.OpenRead(audioPath);

        var speechOptions = new SpeechToTextOptions
        {
            ModelId = _options.ModelName,
        };

        if (string.Equals(settings.LanguageMode, "Fixed", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(settings.LanguageCode))
        {
            speechOptions.SpeechLanguage = settings.LanguageCode;
        }

        var speechResponse = await _speechToTextClient.GetTextAsync(audioStream, speechOptions, ct);

        TranscriptSegmentDto[] segments;
        string? detectedLanguage = null;
        double durationMs = 0;

        if (speechResponse.RawRepresentation is OpenAiVerboseTranscriptionResponse raw)
        {
            if (raw.Segments is { Length: > 0 } rawSegments)
            {
                segments = rawSegments
                    .Select(s => new TranscriptSegmentDto
                    {
                        StartMs = (long)(s.Start * 1000),
                        EndMs = (long)(s.End * 1000),
                        Text = s.Text ?? string.Empty,
                    })
                    .ToArray();
            }
            else
            {
                segments = string.IsNullOrWhiteSpace(speechResponse.Text)
                    ? []
                    : [new TranscriptSegmentDto { StartMs = 0, EndMs = (long)(raw.Duration * 1000), Text = speechResponse.Text }];
            }

            detectedLanguage = raw.Language;
            durationMs = raw.Duration * 1000;
        }
        else
        {
            // Fallback: single segment from text
            segments = string.IsNullOrWhiteSpace(speechResponse.Text)
                ? []
                : [new TranscriptSegmentDto { StartMs = 0, EndMs = 0, Text = speechResponse.Text }];
        }

        _logger.LogInformation(
            "{Engine} transcription completed: {SegmentCount} segments",
            EngineId,
            segments.Length);

        return new TranscriptionResult(
            speechResponse.Text,
            segments,
            detectedLanguage,
            (long)durationMs);
    }
}

// ---------------------------------------------------------------------------
// DTOs for parsing /v1/models response
// ---------------------------------------------------------------------------

file sealed class OpenAiModelListResponse
{
    [JsonPropertyName("data")] public List<OpenAiModelEntry>? Data { get; set; }
}

file sealed class OpenAiModelEntry
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
}
