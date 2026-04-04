using System.Net.Http.Json;
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
}

internal sealed class OpenAiTranscriptionSegment
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("start")] public double Start { get; set; }
    [JsonPropertyName("end")] public double End { get; set; }
    [JsonPropertyName("text")] public string Text { get; set; } = string.Empty;
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
        string? device = null)
    {
        using var content = new MultipartFormDataContent();

        var streamContent = new StreamContent(audioStream);
        streamContent.Headers.ContentType = new("audio/wav");
        content.Add(streamContent, "file", "audio.wav");
        content.Add(new StringContent(modelId), "model");
        content.Add(new StringContent("verbose_json"), "response_format");
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
            throw new InvalidOperationException(
                $"OpenAI-compatible transcription API returned HTTP {(int)response.StatusCode}: {detail.Trim()}");
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
}
