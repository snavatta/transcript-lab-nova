using System.Net;
using ClassTranscriber.Api.Contracts;
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

    public Task<SpeechToTextResponse> GetVerifiedWordTextAsync(
        Stream audioSpeechStream,
        SpeechToTextOptions options,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = _httpClient.BaseAddress?.ToString()?.TrimEnd('/') ?? string.Empty;
        return OpenAiAudioTranscriptionHelper.TranscribeAsync(
            _httpClient,
            $"{baseUrl}/audio/transcriptions",
            apiKey: null,
            modelId: options.ModelId ?? string.Empty,
            language: options.SpeechLanguage,
            audioStream: audioSpeechStream,
            cancellationToken: cancellationToken,
            responseFormat: "verbose_json",
            leaveAudioStreamOpen: true,
            includeProviderErrorDetail: false,
            fileName: "audio.flac",
            mediaType: "audio/flac",
            requestWordTimestamps: true);
    }

    public IAsyncEnumerable<SpeechToTextResponseUpdate> GetStreamingTextAsync(
        Stream audioSpeechStream,
        SpeechToTextOptions? options = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("OpenRouterSpeechToTextClient does not support streaming transcription.");

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}

internal static class OpenRouterTranscriptionResultMapper
{
    public static TranscriptionResult Map(SpeechToTextResponse response, string model)
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

        var costMicroUsd = raw.Usage?.Cost is null
            ? null
            : checked((long?)Math.Round(raw.Usage.Cost.Value * 1_000_000m, MidpointRounding.AwayFromZero));
        var words = new List<TranscriptionWord>();
        foreach (var word in raw.Words ?? [])
        {
            if (string.IsNullOrWhiteSpace(word.Token)
                || word.Start is not { } start
                || word.End is not { } end
                || !double.IsFinite(start)
                || !double.IsFinite(end)
                || start < 0
                || end < start)
            {
                continue;
            }

            words.Add(new TranscriptionWord(
                word.Token,
                checked((long)Math.Round(start * 1000, MidpointRounding.AwayFromZero)),
                checked((long)Math.Round(end * 1000, MidpointRounding.AwayFromZero))));
        }

        return new TranscriptionResult(
            response.Text,
            segments,
            raw.Language,
            durationMs,
            new TranscriptionProcessingMetadata(
                "OpenRouter",
                model,
                1,
                false,
                costMicroUsd,
                null,
                costMicroUsd is null ? null : "Actual"))
        {
            Words = words,
        };
    }
}
