using ClassTranscriber.Api.Transcription.SpeechToText;
using Microsoft.Extensions.AI;

namespace ClassTranscriber.Api.Transcription.SpeechToText;

/// <summary>
/// ISpeechToTextClient implementation that calls any OpenAI-compatible
/// /v1/audio/transcriptions endpoint (standard multipart format).
/// </summary>
public sealed class OpenAiCompatibleSpeechToTextClient : ISpeechToTextClient
{
    private readonly HttpClient _httpClient;

    public OpenAiCompatibleSpeechToTextClient(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient(OpenAiCompatibleTranscriptionEngine.HttpClientName);
    }

    public Task<SpeechToTextResponse> GetTextAsync(
        Stream audioSpeechStream,
        SpeechToTextOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = _httpClient.BaseAddress?.ToString()?.TrimEnd('/') ?? string.Empty;
        var transcriptionUrl = $"{baseUrl}/v1/audio/transcriptions";

        return OpenAiAudioTranscriptionHelper.TranscribeAsync(
            _httpClient,
            transcriptionUrl,
            apiKey: null,
            modelId: options?.ModelId ?? string.Empty,
            language: options?.SpeechLanguage,
            audioStream: audioSpeechStream,
            cancellationToken: cancellationToken);
    }

    public IAsyncEnumerable<SpeechToTextResponseUpdate> GetStreamingTextAsync(
        Stream audioSpeechStream,
        SpeechToTextOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(
            "OpenAiCompatibleSpeechToTextClient does not support streaming transcription.");
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}
