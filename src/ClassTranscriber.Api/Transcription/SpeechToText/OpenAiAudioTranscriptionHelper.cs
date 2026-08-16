using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClassTranscriber.Api.Transcription;
using Microsoft.Extensions.AI;

namespace ClassTranscriber.Api.Transcription.SpeechToText;

// ---------------------------------------------------------------------------
// OpenAI verbose_json response DTOs (internal)
// ---------------------------------------------------------------------------

internal sealed class OpenAiVerboseTranscriptionResponse
{
    [JsonPropertyName("task")] public string? Task { get; set; }
    [JsonPropertyName("language")] public string? Language { get; set; }
    [JsonPropertyName("duration")] public double Duration { get; set; }
    [JsonPropertyName("text")] public string? Text { get; set; }
    [JsonPropertyName("segments")] public OpenAiTranscriptionSegment[]? Segments { get; set; }
    [JsonPropertyName("words")] public OpenAiTranscriptionWord[]? Words { get; set; }
    [JsonPropertyName("usage")] public OpenAiTranscriptionUsage? Usage { get; set; }
}

internal sealed class OpenAiTranscriptionWord
{
    [JsonPropertyName("word")] public string? Word { get; set; }
    [JsonPropertyName("text")] public string? Text { get; set; }
    [JsonPropertyName("start")] public double? Start { get; set; }
    [JsonPropertyName("end")] public double? End { get; set; }
    [JsonIgnore] public string Token => Word ?? Text ?? string.Empty;
}

internal sealed class OpenAiTranscriptionUsage
{
    [JsonPropertyName("seconds")] public double? Seconds { get; set; }
    [JsonPropertyName("cost")] public decimal? Cost { get; set; }
}

internal sealed class OpenAiTranscriptionSegment
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("start")] public double Start { get; set; }
    [JsonPropertyName("end")] public double End { get; set; }
    [JsonPropertyName("text")] public string Text { get; set; } = string.Empty;
}

internal enum OpenAiTranscriptionErrorKind
{
    Unknown,
    Multipart,
    ResponseFormat,
    WordTimestamps,
    AudioFormat,
    AudioDuration,
    AudioSize,
    Model,
}

internal sealed class OpenAiTranscriptionException(
    HttpStatusCode statusCode,
    OpenAiTranscriptionErrorKind errorKind,
    string message,
    TimeSpan? retryAfter = null)
    : InvalidOperationException(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public OpenAiTranscriptionErrorKind ErrorKind { get; } = errorKind;
    public bool ResponseFormatRejected => ErrorKind == OpenAiTranscriptionErrorKind.ResponseFormat;
    public TimeSpan? RetryAfter { get; } = retryAfter;
}

// ---------------------------------------------------------------------------
// Shared helper for OpenAI-compatible /v1/audio/transcriptions requests
// ---------------------------------------------------------------------------

/// <summary>
/// Shared helper for sending audio to an OpenAI-compatible <c>/v1/audio/transcriptions</c> endpoint.
/// Used by both <see cref="OpenVinoSidecarSpeechToTextClient"/> and OpenAiCompatible engine clients.
/// </summary>
internal static class OpenAiAudioTranscriptionHelper
{
    /// <summary>
    /// Posts an audio stream as a multipart form request to <paramref name="url"/> and returns
    /// a <see cref="SpeechToTextResponse"/> whose <see cref="SpeechToTextResponse.RawRepresentation"/>
    /// is the parsed <see cref="OpenAiVerboseTranscriptionResponse"/>.
    /// </summary>
    public static async Task<SpeechToTextResponse> TranscribeAsync(
        HttpClient client,
        string url,
        string? apiKey,
        string modelId,
        string? language,
        Stream audioStream,
        CancellationToken cancellationToken,
        string? device = null,
        string responseFormat = "verbose_json",
        bool leaveAudioStreamOpen = false,
        bool includeProviderErrorDetail = true,
        string fileName = "audio.wav",
        string mediaType = "audio/wav",
        bool requestWordTimestamps = false)
    {
        using var content = new MultipartFormDataContent();
        var boundary = content.Headers.ContentType!.Parameters.Single(parameter => parameter.Name == "boundary");
        boundary.Value = boundary.Value!.Trim('"');

        var streamContent = new StreamContent(
            leaveAudioStreamOpen ? new LeaveOpenStream(audioStream) : audioStream);
        streamContent.Headers.ContentType = new(mediaType);
        content.Add(streamContent, "file", fileName);
        streamContent.Headers.ContentDisposition!.FileNameStar = null;
        content.Add(new StringContent(modelId), "model");
        content.Add(new StringContent(responseFormat), "response_format");
        if (requestWordTimestamps)
            content.Add(new StringContent("word"), "timestamp_granularities[]");
        if (!string.IsNullOrWhiteSpace(language))
            content.Add(new StringContent(language), "language");
        if (!string.IsNullOrWhiteSpace(device))
            content.Add(new StringContent(device), "device");

        foreach (var part in content)
        {
            var disposition = part.Headers.ContentDisposition!;
            var partName = disposition.Name!.Trim('"');
            var partFileName = disposition.FileName?.Trim('"');
            var serializedDisposition = partFileName is null
                ? $"form-data; name=\"{partName}\""
                : $"form-data; name=\"{partName}\"; filename=\"{partFileName}\"";
            part.Headers.Remove("Content-Disposition");
            part.Headers.TryAddWithoutValidation("Content-Disposition", serializedDisposition);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        if (!string.IsNullOrWhiteSpace(apiKey))
            request.Headers.Authorization = new("Bearer", apiKey);

        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            string detail;
            try
            {
                detail = await BoundedHttpContentReader.ReadStringAsync(
                    response.Content,
                    "OpenAI-compatible transcription API response exceeded the maximum allowed size.",
                    cancellationToken);
            }
            catch (ProviderResponseTooLargeException exception)
            {
                throw new OpenAiTranscriptionException(
                    response.StatusCode,
                    OpenAiTranscriptionErrorKind.Unknown,
                    exception.Message,
                    GetRetryAfter(response));
            }
            var message = $"OpenAI-compatible transcription API returned HTTP {(int)response.StatusCode}.";
            if (includeProviderErrorDetail)
                message = $"OpenAI-compatible transcription API returned HTTP {(int)response.StatusCode}: {NormalizeErrorDetail(detail)}";
            throw new OpenAiTranscriptionException(
                response.StatusCode,
                ClassifyError(detail),
                message,
                GetRetryAfter(response));
        }

        var parsed = await BoundedHttpContentReader.ReadJsonAsync<OpenAiVerboseTranscriptionResponse>(
            response.Content,
            "OpenAI-compatible transcription API response exceeded the maximum allowed size.",
            cancellationToken);
        if (parsed is null)
            throw new InvalidOperationException("OpenAI-compatible transcription API returned an empty response.");

        var speechResponse = new SpeechToTextResponse(parsed.Text ?? string.Empty)
        {
            RawRepresentation = parsed,
            EndTime = TimeSpan.FromSeconds(parsed.Duration),
        };

        return speechResponse;
    }

    private static TimeSpan? GetRetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta)
            return delta > TimeSpan.Zero ? delta : TimeSpan.Zero;
        if (retryAfter?.Date is not { } date)
            return null;

        var delay = date - DateTimeOffset.UtcNow;
        return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
    }

    private static OpenAiTranscriptionErrorKind ClassifyError(string detail)
    {
        if (ContainsAny(detail, "multipart", "form-data body"))
            return OpenAiTranscriptionErrorKind.Multipart;
        if (ContainsAny(detail, "timestamp_granularities", "word timestamp"))
            return OpenAiTranscriptionErrorKind.WordTimestamps;
        if (ContainsAny(detail, "response_format", "verbose_json"))
            return OpenAiTranscriptionErrorKind.ResponseFormat;
        if (ContainsAny(detail, "flac", "audio format", "file format", "file type", "codec", "mime"))
            return OpenAiTranscriptionErrorKind.AudioFormat;
        if (ContainsAny(detail, "duration", "audio length", "too long"))
            return OpenAiTranscriptionErrorKind.AudioDuration;
        if (ContainsAny(detail, "file size", "too large", "25mb", "25 mb", "payload too large"))
            return OpenAiTranscriptionErrorKind.AudioSize;
        if (detail.Contains("no endpoints", StringComparison.OrdinalIgnoreCase)
            || (detail.Contains("model", StringComparison.OrdinalIgnoreCase)
                && ContainsAny(detail, "not found", "unknown", "invalid", "unsupported", "unavailable")))
        {
            return OpenAiTranscriptionErrorKind.Model;
        }

        return OpenAiTranscriptionErrorKind.Unknown;
    }

    private static bool ContainsAny(string value, params string[] candidates)
        => candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));

    private static string ExtractProviderMessage(string detail)
    {
        var trimmed = detail.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return string.Empty;

        try
        {
            var payload = JsonSerializer.Deserialize<OpenAiErrorResponse>(trimmed);
            return (payload?.Detail ?? payload?.Error?.Message)?.Trim() ?? trimmed;
        }
        catch (JsonException)
        {
            return trimmed;
        }
    }

    private static string NormalizeErrorDetail(string detail)
    {
        var providerMessage = ExtractProviderMessage(detail);
        if (string.IsNullOrWhiteSpace(providerMessage))
            return "The endpoint returned an empty error response.";
        return providerMessage;
    }
}

file sealed class LeaveOpenStream(Stream inner) : Stream
{
    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => inner.CanSeek;
    public override bool CanWrite => inner.CanWrite;
    public override long Length => inner.Length;
    public override long Position { get => inner.Position; set => inner.Position = value; }

    public override void Flush() => inner.Flush();
    public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
    public override void SetLength(long value) => inner.SetLength(value);
    public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);

    protected override void Dispose(bool disposing)
    {
    }
}

file sealed class OpenAiErrorResponse
{
    [JsonPropertyName("detail")] public string? Detail { get; set; }
    [JsonPropertyName("error")] public OpenAiNestedError? Error { get; set; }
}

file sealed class OpenAiNestedError
{
    [JsonPropertyName("message")] public string? Message { get; set; }
}
