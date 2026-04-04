using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace ClassTranscriber.Api.Transcription.SpeechToText;

/// <summary>
/// <see cref="ISpeechToTextClient"/> implementation that calls the OpenVINO Whisper sidecar's
/// OpenAI-compatible <c>POST /v1/audio/transcriptions</c> endpoint using <c>verbose_json</c> format.
/// The returned <see cref="SpeechToTextResponse.RawRepresentation"/> is an
/// <see cref="OpenAiVerboseTranscriptionResponse"/> from which segments, language, and duration
/// can be extracted by the engine.
/// </summary>
public sealed class OpenVinoSidecarSpeechToTextClient : ISpeechToTextClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOpenVinoWhisperSidecarManager _sidecarManager;
    private readonly OpenVinoWhisperSidecarOptions _options;

    public OpenVinoSidecarSpeechToTextClient(
        IHttpClientFactory httpClientFactory,
        IOpenVinoWhisperSidecarManager sidecarManager,
        IOptions<OpenVinoWhisperSidecarOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _sidecarManager = sidecarManager;
        _options = options.Value;
    }

    public async Task<SpeechToTextResponse> GetTextAsync(
        Stream audioSpeechStream,
        SpeechToTextOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient(OpenVinoWhisperSidecarManager.HttpClientName);
        var url = $"{_sidecarManager.BaseUrl}/v1/audio/transcriptions";

        return await OpenAiAudioTranscriptionHelper.TranscribeAsync(
            client,
            url,
            apiKey: null,
            modelId: options?.ModelId ?? string.Empty,
            language: options?.SpeechLanguage,
            audioStream: audioSpeechStream,
            cancellationToken: cancellationToken,
            device: string.IsNullOrWhiteSpace(_options.Device) ? "GPU" : _options.Device);
    }

    public IAsyncEnumerable<SpeechToTextResponseUpdate> GetStreamingTextAsync(
        Stream audioSpeechStream,
        SpeechToTextOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(
            "Streaming transcription is not supported by the OpenVINO Whisper sidecar.");
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}

