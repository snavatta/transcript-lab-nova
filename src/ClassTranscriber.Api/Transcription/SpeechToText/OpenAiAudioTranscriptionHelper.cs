using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    [JsonPropertyName("usage")] public OpenAiTranscriptionUsage? Usage { get; set; }
}

internal sealed class OpenAiTranscriptionUsage
{
    [JsonPropertyName("seconds")] public double? Seconds { get; set; }
}

internal sealed class OpenAiTranscriptionSegment
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("start")] public double Start { get; set; }
    [JsonPropertyName("end")] public double End { get; set; }
    [JsonPropertyName("text")] public string Text { get; set; } = string.Empty;
}

internal sealed class OpenAiTranscriptionException(
    HttpStatusCode statusCode,
    bool responseFormatRejected,
    string message)
    : InvalidOperationException(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public bool ResponseFormatRejected { get; } = responseFormatRejected;
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
    /// Posts a WAV stream as a multipart form request to <paramref name="url"/> and returns
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
        bool includeProviderErrorDetail = true)
    {
        using var content = new MultipartFormDataContent();

        var streamContent = new StreamContent(
            leaveAudioStreamOpen ? new LeaveOpenStream(audioStream) : audioStream);
        streamContent.Headers.ContentType = new("audio/wav");
        content.Add(streamContent, "file", "audio.wav");
        content.Add(new StringContent(modelId), "model");
        content.Add(new StringContent(responseFormat), "response_format");
        if (!string.IsNullOrWhiteSpace(language))
            content.Add(new StringContent(language), "language");
        if (!string.IsNullOrWhiteSpace(device))
            content.Add(new StringContent(device), "device");

        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        if (!string.IsNullOrWhiteSpace(apiKey))
            request.Headers.Authorization = new("Bearer", apiKey);

        using var response = await client.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken);
            var message = $"OpenAI-compatible transcription API returned HTTP {(int)response.StatusCode}.";
            if (includeProviderErrorDetail)
                message = $"OpenAI-compatible transcription API returned HTTP {(int)response.StatusCode}: {NormalizeErrorDetail(detail)}";
            throw new OpenAiTranscriptionException(
                response.StatusCode,
                IsResponseFormatRejection(detail),
                message);
        }

        var parsed = await response.Content.ReadFromJsonAsync<OpenAiVerboseTranscriptionResponse>(cancellationToken);
        if (parsed is null)
            throw new InvalidOperationException("OpenAI-compatible transcription API returned an empty response.");

        var speechResponse = new SpeechToTextResponse(parsed.Text ?? string.Empty)
        {
            RawRepresentation = parsed,
            EndTime = TimeSpan.FromSeconds(parsed.Duration),
        };

        return speechResponse;
    }

    private static bool IsResponseFormatRejection(string detail)
    {
        var trimmed = detail.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return false;

        try
        {
            var payload = JsonSerializer.Deserialize<OpenAiErrorResponse>(trimmed);
            var providerMessage = payload?.Detail ?? payload?.Error?.Message;
            if (!string.IsNullOrWhiteSpace(providerMessage))
                return ContainsResponseFormatName(providerMessage);
        }
        catch (JsonException)
        {
            return ContainsResponseFormatName(trimmed);
        }

        return ContainsResponseFormatName(trimmed);
    }

    private static bool ContainsResponseFormatName(string value)
        => value.Contains("response_format", StringComparison.OrdinalIgnoreCase)
            || value.Contains("verbose_json", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeErrorDetail(string detail)
    {
        var trimmed = detail.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return "The endpoint returned an empty error response.";

        try
        {
            var payload = JsonSerializer.Deserialize<OpenAiErrorResponse>(trimmed);
            var providerMessage = payload?.Detail ?? payload?.Error?.Message;
            if (!string.IsNullOrWhiteSpace(providerMessage))
                return providerMessage.Trim();
        }
        catch (JsonException)
        {
            return trimmed;
        }

        return trimmed;
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
