using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace ClassTranscriber.Api.Transcription.SpeechToText;

// ---------------------------------------------------------------------------
// Model management DTOs
// ---------------------------------------------------------------------------

public sealed record SidecarModelStatusDto(
    string Name,
    string DisplayName,
    bool IsInstalled,
    string? InstallPath,
    long? SizeBytes);

// ---------------------------------------------------------------------------
// Interface
// ---------------------------------------------------------------------------

public interface IOpenVinoSidecarModelManager
{
    /// <summary>
    /// Ensures the given model is installed on the sidecar. If not, triggers
    /// a download via the sidecar's <c>POST /models/download</c> SSE endpoint
    /// and waits until download completes.
    /// </summary>
    Task EnsureModelInstalledAsync(string model, CancellationToken cancellationToken);

    /// <summary>Returns the list of catalog models and their installation status from the sidecar.</summary>
    Task<IReadOnlyList<SidecarModelStatusDto>> ListModelsAsync(CancellationToken cancellationToken);
}

// ---------------------------------------------------------------------------
// Implementation
// ---------------------------------------------------------------------------

public sealed class OpenVinoSidecarModelManager : IOpenVinoSidecarModelManager
{
    private readonly IOpenVinoWhisperSidecarManager _sidecarManager;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<OpenVinoSidecarModelManager> _logger;

    public OpenVinoSidecarModelManager(
        IOpenVinoWhisperSidecarManager sidecarManager,
        IHttpClientFactory httpClientFactory,
        ILogger<OpenVinoSidecarModelManager> logger)
    {
        _sidecarManager = sidecarManager;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task EnsureModelInstalledAsync(string model, CancellationToken cancellationToken)
    {
        var models = await ListModelsAsync(cancellationToken);
        var entry = models.FirstOrDefault(m => string.Equals(m.Name, model, StringComparison.OrdinalIgnoreCase));

        if (entry is { IsInstalled: true })
        {
            _logger.LogDebug("OpenVINO sidecar model '{Model}' is already installed at {Path}", model, entry.InstallPath);
            return;
        }

        _logger.LogInformation("OpenVINO sidecar model '{Model}' is not installed. Triggering download.", model);
        await DownloadModelViaSseAsync(model, cancellationToken);
    }

    public async Task<IReadOnlyList<SidecarModelStatusDto>> ListModelsAsync(CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(OpenVinoWhisperSidecarManager.HttpClientName);
        var response = await client.GetAsync($"{_sidecarManager.BaseUrl}/models", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"OpenVINO sidecar GET /models returned HTTP {(int)response.StatusCode}: {detail.Trim()}");
        }

        var body = await response.Content.ReadFromJsonAsync<SidecarModelsListResponse>(cancellationToken);
        if (body is null)
            return [];

        return body.Models
            .Select(m => new SidecarModelStatusDto(m.Name, m.DisplayName, m.IsInstalled, m.InstallPath, m.SizeBytes))
            .ToArray();
    }

    private async Task DownloadModelViaSseAsync(string model, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(OpenVinoWhisperSidecarManager.HttpClientName);

        var requestBody = new { model };
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_sidecarManager.BaseUrl}/models/download")
        {
            Content = JsonContent.Create(requestBody),
        };
        request.Headers.Accept.Add(new("text/event-stream"));

        // Use HttpCompletionOption.ResponseHeadersRead to stream SSE
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"OpenVINO sidecar POST /models/download returned HTTP {(int)response.StatusCode}: {detail.Trim()}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
                break;

            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (!line.StartsWith("data:", StringComparison.Ordinal))
                continue;

            var json = line["data:".Length..].Trim();
            if (string.IsNullOrWhiteSpace(json))
                continue;

            SseDownloadEvent? evt;
            try
            {
                evt = System.Text.Json.JsonSerializer.Deserialize<SseDownloadEvent>(json);
            }
            catch
            {
                continue;
            }

            if (evt is null)
                continue;

            switch (evt.Status)
            {
                case "downloading":
                    _logger.LogDebug(
                        "OpenVINO sidecar downloading model '{Model}': {Progress:P0} ({Downloaded}/{Total} bytes)",
                        model,
                        evt.Progress,
                        evt.BytesDownloaded,
                        evt.BytesTotal);
                    break;

                case "complete":
                    _logger.LogInformation("OpenVINO sidecar model '{Model}' download complete.", model);
                    return;

                case "error":
                    throw new InvalidOperationException(
                        $"OpenVINO sidecar reported a download error for model '{model}': {evt.Error}");

                case "heartbeat":
                    _logger.LogDebug("OpenVINO sidecar download heartbeat for model '{Model}'", model);
                    break;
            }
        }
    }

    // ---------------------------------------------------------------------------
    // Internal response DTOs for parsing sidecar /models endpoint
    // ---------------------------------------------------------------------------

    private sealed class SidecarModelsListResponse
    {
        [JsonPropertyName("models")] public SidecarModelEntry[] Models { get; set; } = [];
    }

    private sealed class SidecarModelEntry
    {
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("display_name")] public string DisplayName { get; set; } = string.Empty;
        [JsonPropertyName("is_installed")] public bool IsInstalled { get; set; }
        [JsonPropertyName("install_path")] public string? InstallPath { get; set; }
        [JsonPropertyName("size_bytes")] public long? SizeBytes { get; set; }
    }

    private sealed class SseDownloadEvent
    {
        [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;
        [JsonPropertyName("model")] public string? Model { get; set; }
        [JsonPropertyName("progress")] public double Progress { get; set; }
        [JsonPropertyName("bytes_downloaded")] public long? BytesDownloaded { get; set; }
        [JsonPropertyName("bytes_total")] public long? BytesTotal { get; set; }
        [JsonPropertyName("error")] public string? Error { get; set; }
    }
}
