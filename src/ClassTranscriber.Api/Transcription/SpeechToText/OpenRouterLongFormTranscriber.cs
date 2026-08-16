using System.Net;
using ClassTranscriber.Api.Contracts;
using ClassTranscriber.Api.Domain;
using ClassTranscriber.Api.Media;
using Microsoft.Extensions.AI;

namespace ClassTranscriber.Api.Transcription.SpeechToText;

internal sealed class OpenRouterLongFormTranscriber(
    OpenRouterSpeechToTextClient speechToTextClient,
    IHostedAudioPreparationService hostedAudioPreparationService,
    IMediaInspector mediaInspector)
{
    private const int MaximumAttempts = 3;
    private const string MissingWordsMessage = "OpenRouter did not return usable word timestamps.";
    private const string TimeoutMessage = "OpenRouter transcription timed out.";

    public async Task<TranscriptionResult> TranscribeAsync(
        string audioPath,
        ProjectSettings settings,
        Func<OpenRouterChunkProgress, CancellationToken, ValueTask>? onChunkSucceeded,
        CancellationToken ct)
    {
        var mediaInfo = await mediaInspector.InspectAsync(audioPath, ct);
        if (mediaInfo is not { DurationMs: > 0 })
            throw new InvalidOperationException("OpenRouter could not determine the audio duration.");

        await using var chunkSet = await hostedAudioPreparationService.PrepareChunksAsync(
            audioPath,
            mediaInfo.DurationMs,
            ct);
        if (chunkSet.Chunks.Count == 0)
            throw new InvalidOperationException("OpenRouter did not receive any prepared audio chunks.");

        var words = new List<TranscriptionWord>();
        string? detectedLanguage = null;
        decimal cumulativeCostUsd = 0;
        var hasCompleteCost = true;
        var requestCount = 0;

        for (var chunkIndex = 0; chunkIndex < chunkSet.Chunks.Count; chunkIndex++)
        {
            ct.ThrowIfCancellationRequested();
            var chunk = chunkSet.Chunks[chunkIndex];
            ValidatePreparedChunk(chunk);
            var response = await SendChunkAsync(chunk.FilePath, settings, ct);
            if (response.RawRepresentation is not OpenAiVerboseTranscriptionResponse raw)
                throw new InvalidOperationException(MissingWordsMessage);

            var retainedWords = MapOwnedWords(raw.Words, chunk);
            if (retainedWords.Count == 0)
                throw new InvalidOperationException(MissingWordsMessage);

            words.AddRange(retainedWords);
            detectedLanguage ??= string.IsNullOrWhiteSpace(raw.Language) ? null : raw.Language;
            requestCount = checked(requestCount + 1);
            if (raw.Usage?.Cost is { } requestCostUsd)
                cumulativeCostUsd = checked(cumulativeCostUsd + requestCostUsd);
            else
                hasCompleteCost = false;

            var cumulativeResult = BuildResult(
                words,
                detectedLanguage,
                mediaInfo.DurationMs,
                settings.Model,
                requestCount,
                hasCompleteCost ? ConvertCostToMicroUsd(cumulativeCostUsd) : null);
            if (onChunkSucceeded is not null)
            {
                await onChunkSucceeded(
                    new OpenRouterChunkProgress(
                        chunkIndex,
                        chunkSet.Chunks.Count,
                        chunk.CoreStartMs,
                        chunk.CoreEndMs,
                        cumulativeResult),
                    ct);
            }
        }

        return BuildResult(
            words,
            detectedLanguage,
            mediaInfo.DurationMs,
            settings.Model,
            requestCount,
            hasCompleteCost ? ConvertCostToMicroUsd(cumulativeCostUsd) : null);
    }

    private async Task<SpeechToTextResponse> SendChunkAsync(
        string chunkPath,
        ProjectSettings settings,
        CancellationToken ct)
    {
        var speechOptions = new SpeechToTextOptions { ModelId = settings.Model };
        if (string.Equals(settings.LanguageMode, "Fixed", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(settings.LanguageCode))
        {
            speechOptions.SpeechLanguage = settings.LanguageCode;
        }

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var audioStream = File.OpenRead(chunkPath);
                return await speechToTextClient.GetVerifiedWordTextAsync(audioStream, speechOptions, ct);
            }
            catch (OpenAiTranscriptionException exception) when (
                attempt < MaximumAttempts
                && exception.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
            {
                if (exception.RetryAfter is { } retryAfter && retryAfter > TimeSpan.Zero)
                    await Task.Delay(retryAfter, ct);
            }
            catch (OpenAiTranscriptionException exception) when (
                exception.StatusCode == HttpStatusCode.BadRequest
                && exception.ErrorKind != OpenAiTranscriptionErrorKind.Unknown)
            {
                throw new InvalidOperationException(GetBadRequestMessage(exception.ErrorKind));
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new InvalidOperationException(TimeoutMessage);
            }
        }
    }

    private static string GetBadRequestMessage(OpenAiTranscriptionErrorKind errorKind) => errorKind switch
    {
        OpenAiTranscriptionErrorKind.Multipart => "OpenRouter rejected multipart upload framing (HTTP 400).",
        OpenAiTranscriptionErrorKind.ResponseFormat => "OpenRouter rejected the verbose response format (HTTP 400).",
        OpenAiTranscriptionErrorKind.WordTimestamps => "OpenRouter rejected word timestamps (HTTP 400).",
        OpenAiTranscriptionErrorKind.AudioFormat => "OpenRouter rejected the FLAC audio format (HTTP 400).",
        OpenAiTranscriptionErrorKind.AudioDuration => "OpenRouter rejected the audio duration (HTTP 400).",
        OpenAiTranscriptionErrorKind.AudioSize => "OpenRouter rejected the audio file size (HTTP 400).",
        OpenAiTranscriptionErrorKind.Model => "OpenRouter rejected the selected model (HTTP 400).",
        _ => "OpenRouter rejected the transcription request (HTTP 400).",
    };

    private static IReadOnlyList<TranscriptionWord> MapOwnedWords(
        OpenAiTranscriptionWord[]? providerWords,
        HostedAudioChunk chunk)
    {
        var retainedWords = new List<TranscriptionWord>();
        foreach (var word in providerWords ?? [])
        {
            if (string.IsNullOrEmpty(word.Token)
                || word.Start is not { } relativeStartSeconds
                || word.End is not { } relativeEndSeconds
                || !double.IsFinite(relativeStartSeconds)
                || !double.IsFinite(relativeEndSeconds)
                || relativeStartSeconds < 0
                || relativeEndSeconds < relativeStartSeconds)
            {
                continue;
            }

            var midpointMs = chunk.ExtractionStartMs
                + (((relativeStartSeconds + relativeEndSeconds) / 2d) * 1000d);
            if (midpointMs < chunk.CoreStartMs
                || midpointMs > chunk.CoreEndMs
                || (!chunk.IsFinal && midpointMs >= chunk.CoreEndMs))
            {
                continue;
            }

            retainedWords.Add(new TranscriptionWord(
                word.Token,
                checked(chunk.ExtractionStartMs + ToMilliseconds(relativeStartSeconds)),
                checked(chunk.ExtractionStartMs + ToMilliseconds(relativeEndSeconds))));
        }

        return retainedWords;
    }

    private static TranscriptionResult BuildResult(
        IReadOnlyList<TranscriptionWord> words,
        string? detectedLanguage,
        long durationMs,
        string model,
        int requestCount,
        long? costMicroUsd)
    {
        var snapshot = words.ToArray();
        return new TranscriptionResult(
            TranscriptionWordSegmenter.Join(snapshot),
            TranscriptionWordSegmenter.BuildReadableSegments(snapshot),
            detectedLanguage,
            durationMs,
            new TranscriptionProcessingMetadata(
                "OpenRouter",
                model,
                requestCount,
                false,
                costMicroUsd,
                null,
                costMicroUsd is null ? null : "Actual"))
        {
            Words = snapshot,
        };
    }

    private static void ValidatePreparedChunk(HostedAudioChunk chunk)
    {
        var file = new FileInfo(chunk.FilePath);
        if (!file.Exists
            || file.Length <= 0
            || file.Length >= HostedAudioPreparationService.MaximumEncodedPartBytes
            || chunk.EncodedLengthBytes != file.Length)
        {
            throw new HostedAudioPreparationException("Hosted audio chunk is outside the provider upload limit.");
        }
    }

    private static long ToMilliseconds(double seconds) =>
        checked((long)Math.Round(seconds * 1000d, MidpointRounding.AwayFromZero));

    private static long ConvertCostToMicroUsd(decimal costUsd) =>
        checked((long)Math.Round(costUsd * 1_000_000m, MidpointRounding.AwayFromZero));
}
