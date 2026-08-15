using System.Net;
using Microsoft.Extensions.AI;

namespace ClassTranscriber.Api.Transcription.SpeechToText;

public sealed class OpenRouterSpeechToTextClient : ISpeechToTextClient
{
    private readonly HttpClient _httpClient;

    public OpenRouterSpeechToTextClient(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient(OpenRouterTranscriptionEngine.HttpClientName);
    }

    public async Task<SpeechToTextResponse> GetTextAsync(
        Stream audioSpeechStream,
        SpeechToTextOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = _httpClient.BaseAddress?.ToString()?.TrimEnd('/') ?? string.Empty;
        var initialPosition = audioSpeechStream.CanSeek ? audioSpeechStream.Position : (long?)null;

        try
        {
            return await TranscribeAsync("verbose_json");
        }
        catch (OpenAiTranscriptionException ex) when (
            ex.StatusCode == HttpStatusCode.BadRequest
            && initialPosition is not null
            && ex.ResponseFormatRejected)
        {
            audioSpeechStream.Position = initialPosition.Value;
            return await TranscribeAsync("json");
        }

        Task<SpeechToTextResponse> TranscribeAsync(string responseFormat)
        {
            return OpenAiAudioTranscriptionHelper.TranscribeAsync(
                _httpClient,
                $"{baseUrl}/audio/transcriptions",
                apiKey: null,
                modelId: options?.ModelId ?? string.Empty,
                language: options?.SpeechLanguage,
                audioStream: audioSpeechStream,
                cancellationToken: cancellationToken,
                responseFormat: responseFormat,
                leaveAudioStreamOpen: true,
                includeProviderErrorDetail: false);
        }
    }

    public IAsyncEnumerable<SpeechToTextResponseUpdate> GetStreamingTextAsync(
        Stream audioSpeechStream,
        SpeechToTextOptions? options = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("OpenRouterSpeechToTextClient does not support streaming transcription.");

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}
