using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using ClassTranscriber.Api.Domain;

namespace ClassTranscriber.Api.Transcription;

internal sealed class XaiDirectClient(XaiOptions options, IHttpClientFactory httpClientFactory)
{
    public async Task<string> SendWithRetriesAsync(
        string path,
        ProjectSettings settings,
        CancellationToken ct)
    {
        for (var attempt = 1; attempt <= 3; attempt += 1)
        {
            using var request = CreateRequestSafely(path, settings);
            using var response = await SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
                return await ReadResponseAsync(response, ct);

            if (attempt < 3
                && response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
            {
                var delay = response.Headers.RetryAfter?.Delta
                    ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow)
                    ?? TimeSpan.FromSeconds(attempt);
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, ct);
                continue;
            }

            throw new InvalidOperationException(
                $"xAI transcription API returned HTTP {(int)response.StatusCode}.");
        }

        throw new InvalidOperationException("xAI transcription API did not complete.");
    }

    private HttpRequestMessage CreateRequestSafely(string path, ProjectSettings settings)
    {
        try
        {
            return CreateRequest(path, settings);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException("The prepared xAI audio could not be read.");
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            return await httpClientFactory.CreateClient(XaiTranscriptionEngine.HttpClientName)
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            throw new InvalidOperationException("xAI transcription request timed out.");
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException("xAI transcription request failed.");
        }
    }

    private static async Task<string> ReadResponseAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await BoundedHttpContentReader.ReadStringAsync(
                response.Content,
                "xAI transcription response exceeded the maximum allowed size.",
                ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            throw new InvalidOperationException("xAI transcription request timed out.");
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            throw new InvalidOperationException("xAI transcription response could not be read.");
        }
    }

    private HttpRequestMessage CreateRequest(string path, ProjectSettings settings)
    {
        var multipart = new MultipartFormDataContent();
        if (string.Equals(settings.LanguageMode, "Fixed", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(settings.LanguageCode))
        {
            multipart.Add(new StringContent(settings.LanguageCode), "language");
            multipart.Add(new StringContent("true"), "format");
        }
        if (settings.DiarizationEnabled
            && string.Equals(settings.DiarizationSource, "Provider", StringComparison.OrdinalIgnoreCase))
            multipart.Add(new StringContent("true"), "diarize");
        multipart.Add(new StringContent("false"), "filler_words");
        multipart.Add(new StringContent(options.VadThreshold.ToString(CultureInfo.InvariantCulture)), "vad_threshold");

        var streamContent = new StreamContent(File.OpenRead(path));
        streamContent.Headers.ContentType = new MediaTypeHeaderValue("audio/flac");
        multipart.Add(streamContent, "file", Path.GetFileName(path));
        return new HttpRequestMessage(HttpMethod.Post, "stt") { Content = multipart };
    }
}
