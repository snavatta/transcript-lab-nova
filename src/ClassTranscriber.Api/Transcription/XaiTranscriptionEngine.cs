using ClassTranscriber.Api.Domain;
using Microsoft.Extensions.Options;

namespace ClassTranscriber.Api.Transcription;

public sealed class XaiOptions
{
    public const string SectionName = "Transcription:Xai";
    public string BaseUrl { get; set; } = "https://api.x.ai/v1";
    public string ApiKey { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 1800;
    public double VadThreshold { get; set; } = 0.08;
    public decimal EstimatedCostPerHourUsd { get; set; } = 0.10m;
}

public sealed class XaiTranscriptionEngine : IRegisteredTranscriptionEngine, IXaiDiarizationService
{
    public const string HttpClientName = "Xai";
    public const string PreferredModel = "grok-stt-1.0";
    internal const long MaxFileSizeBytes = 500_000_000;

    private readonly XaiOptions _options;
    private readonly IHostedAudioPreparationService _audioPreparation;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<XaiTranscriptionEngine> _logger;
    private readonly XaiDirectClient _client;
    private readonly XaiResponseMapper _responseMapper;

    public XaiTranscriptionEngine(
        IOptions<XaiOptions> options,
        IHostedAudioPreparationService audioPreparation,
        IHttpClientFactory httpClientFactory,
        ILogger<XaiTranscriptionEngine> logger)
    {
        _options = options.Value;
        _audioPreparation = audioPreparation;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _client = new XaiDirectClient(_options, httpClientFactory);
        _responseMapper = new XaiResponseMapper(_options.EstimatedCostPerHourUsd);
    }

    public string EngineId => "Xai";
    public IReadOnlyCollection<string> SupportedModels => GetAvailabilityError() is null ? [PreferredModel] : [];

    public IReadOnlyCollection<string> ProviderDiarizationModels => [PreferredModel];
    public IReadOnlyCollection<string> WordTimestampModels => [PreferredModel];

    public string? GetAvailabilityError()
    {
        if (!Uri.TryCreate(_options.BaseUrl, UriKind.Absolute, out var baseUri)
            || !string.Equals(baseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return "xAI engine requires Transcription:Xai:BaseUrl to be an absolute HTTPS URL.";
        }

        return string.IsNullOrWhiteSpace(_options.ApiKey)
            ? "xAI engine requires Transcription:Xai:ApiKey to be set."
            : null;
    }

    public string? GetProbeError()
    {
        var availabilityError = GetAvailabilityError();
        if (availabilityError is not null)
            return availabilityError;

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var request = new HttpRequestMessage(HttpMethod.Get, "models");
            using var response = _httpClientFactory.CreateClient(HttpClientName)
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token)
                .GetAwaiter().GetResult();
            return response.IsSuccessStatusCode ? null : $"xAI models endpoint returned HTTP {(int)response.StatusCode}.";
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return "xAI models endpoint is not reachable.";
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
        if (!string.Equals(settings.Model, PreferredModel, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Unsupported xAI transcription model.", nameof(settings));
        if (settings.DiarizationEnabled
            && string.Equals(settings.DiarizationSource, "Xai", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Direct xAI transcription does not support Xai as an external diarization source.",
                nameof(settings));
        }

        HostedAudioFile preparedAudio;
        try
        {
            preparedAudio = await _audioPreparation.PrepareWholeFlacAsync(audioPath, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            throw new InvalidOperationException("xAI audio preparation failed.");
        }

        await using (preparedAudio)
        {
            long preparedLength;
            try
            {
                preparedLength = new FileInfo(preparedAudio.FilePath).Length;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new InvalidOperationException("The prepared xAI audio could not be read.");
            }

            if (preparedLength > MaxFileSizeBytes)
                throw new InvalidOperationException("The prepared recording exceeds xAI's 500 MB file limit.");

            _logger.LogInformation("Starting direct xAI transcription with model {Model}", settings.Model);
            var response = await _client.SendWithRetriesAsync(preparedAudio.FilePath, settings, ct);
            var result = _responseMapper.MapTranscription(response, settings);
            _logger.LogInformation("Direct xAI transcription completed with {SegmentCount} segments", result.Segments.Length);
            return result;
        }
    }

    public async Task<XaiDiarizationResult> DiarizeAsync(
        string audioPath,
        long? durationMs,
        CancellationToken ct = default)
    {
        var availabilityError = GetAvailabilityError();
        if (availabilityError is not null)
            throw new InvalidOperationException(availabilityError);

        HostedAudioFile preparedAudio;
        try
        {
            preparedAudio = await _audioPreparation.PrepareWholeFlacAsync(audioPath, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            throw new InvalidOperationException("xAI audio preparation failed.");
        }

        await using (preparedAudio)
        {
            long preparedLength;
            try
            {
                preparedLength = new FileInfo(preparedAudio.FilePath).Length;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new InvalidOperationException("The prepared xAI audio could not be read.");
            }

            if (preparedLength > MaxFileSizeBytes)
                throw new InvalidOperationException("The prepared recording exceeds xAI's 500 MB file limit.");

            var settings = new ProjectSettings
            {
                Engine = EngineId,
                Model = PreferredModel,
                DiarizationEnabled = true,
                DiarizationSource = "Provider",
            };
            var response = await _client.SendWithRetriesAsync(preparedAudio.FilePath, settings, ct);
            return _responseMapper.MapDiarization(response, durationMs);
        }
    }
}
