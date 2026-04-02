using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using ClassTranscriber.Api.Contracts;
using ClassTranscriber.Api.Domain;
using Microsoft.Extensions.Options;

namespace ClassTranscriber.Api.Transcription;

public class WhisperOptions
{
    public string WhisperCliPath { get; set; } = "whisper-cli";
    public string ModelsPath { get; set; } = "/data/models";
    public bool AutoDownloadModels { get; set; } = true;
    public string ModelDownloadBaseUrl { get; set; } = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main";
}

public class WhisperCliTranscriptionEngine : IRegisteredTranscriptionEngine
{
    private const string ModelDownloadClientName = "WhisperModelDownloads";
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ModelLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly WhisperOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WhisperCliTranscriptionEngine> _logger;

    public WhisperCliTranscriptionEngine(
        IOptions<WhisperOptions> options,
        IHttpClientFactory httpClientFactory,
        ILogger<WhisperCliTranscriptionEngine> logger)
    {
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public string EngineId => "Whisper";

    public IReadOnlyCollection<string> SupportedModels { get; } = ["tiny", "base", "small", "medium", "large"];

    public async Task<TranscriptionResult> TranscribeAsync(string audioPath, ProjectSettings settings, CancellationToken ct = default)
    {
        var modelPath = await ResolveModelPathAsync(settings.Model, ct);
        var outputDir = Path.GetDirectoryName(audioPath) ?? Path.GetTempPath();
        var baseName = Path.GetFileNameWithoutExtension(audioPath);
        var outputFilePath = Path.Combine(outputDir, baseName);

        var args = $"-m \"{modelPath}\" -f \"{audioPath}\" --output-json --output-file \"{outputFilePath}\"";

        if (settings.LanguageMode == "Fixed" && !string.IsNullOrWhiteSpace(settings.LanguageCode))
            args += $" -l {settings.LanguageCode}";
        else
            args += " -l auto";

        _logger.LogInformation("Starting Whisper transcription for {AudioPath} with model {Model}", audioPath, settings.Model);
        _logger.LogDebug("Whisper args: {Args}", args);

        var psi = new ProcessStartInfo
        {
            FileName = _options.WhisperCliPath,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi);
        if (process is null)
            throw new InvalidOperationException("Failed to start Whisper process.");

        var stderr = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            _logger.LogError("Whisper transcription failed: {Error}", stderr);
            throw new InvalidOperationException($"Whisper failed with exit code {process.ExitCode}: {stderr}");
        }

        // Parse the JSON output
        var jsonPath = Path.Combine(outputDir, $"{baseName}.json");
        if (!File.Exists(jsonPath))
            throw new FileNotFoundException($"Whisper output not found at {jsonPath}");

        var json = await File.ReadAllTextAsync(jsonPath, ct);
        var whisperOutput = JsonSerializer.Deserialize<WhisperOutput>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (whisperOutput is null)
            throw new InvalidOperationException("Failed to parse Whisper output.");

        var segments = (whisperOutput.Transcription ?? [])
            .Select(s => new TranscriptSegmentDto
            {
                StartMs = ParseTimestampMs(s.Timestamps.From),
                EndMs = ParseTimestampMs(s.Timestamps.To),
                Text = s.Text.Trim(),
                Speaker = null,
            })
            .Where(s => !string.IsNullOrWhiteSpace(s.Text))
            .ToArray();

        var plainText = string.Join(" ", segments.Select(s => s.Text));
        var detectedLanguage = whisperOutput.Result?.Language;
        var durationMs = segments.Length > 0 ? segments.Max(s => s.EndMs) : 0L;

        // Clean up JSON output file
        try { File.Delete(jsonPath); } catch { /* best effort */ }

        _logger.LogInformation("Whisper transcription completed: {SegmentCount} segments, language={Language}", segments.Length, detectedLanguage ?? "unknown");

        return new TranscriptionResult(plainText, segments, detectedLanguage, durationMs);
    }

    private async Task<string> ResolveModelPathAsync(string model, CancellationToken ct)
    {
        var modelFileName = GetModelFileName(model);
        var modelsRoot = Path.GetFullPath(_options.ModelsPath);
        Directory.CreateDirectory(modelsRoot);

        var path = Path.GetFullPath(Path.Combine(modelsRoot, modelFileName));
        if (File.Exists(path))
            return path;

        if (!_options.AutoDownloadModels)
            throw new FileNotFoundException($"Whisper model not found at {path}. Auto-download is disabled.");

        var modelLock = ModelLocks.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));
        await modelLock.WaitAsync(ct);
        try
        {
            if (File.Exists(path))
                return path;

            await DownloadModelAsync(model, modelFileName, path, ct);
            return path;
        }
        finally
        {
            modelLock.Release();
        }
    }

    private async Task DownloadModelAsync(string model, string modelFileName, string destinationPath, CancellationToken ct)
    {
        var downloadBaseUrl = _options.ModelDownloadBaseUrl.TrimEnd('/');
        var downloadUrl = $"{downloadBaseUrl}/{Uri.EscapeDataString(modelFileName)}";
        var tempPath = $"{destinationPath}.download";

        _logger.LogInformation(
            "Whisper model {Model} is missing. Downloading from {DownloadUrl} to {DestinationPath}",
            model,
            downloadUrl,
            destinationPath);

        try
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);

            var client = _httpClientFactory.CreateClient(ModelDownloadClientName);
            using var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            await using var source = await response.Content.ReadAsStreamAsync(ct);
            await using var destination = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await source.CopyToAsync(destination, ct);
            await destination.FlushAsync(ct);

            File.Move(tempPath, destinationPath, overwrite: true);
            _logger.LogInformation("Downloaded Whisper model {Model} to {DestinationPath}", model, destinationPath);
        }
        catch
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch
            {
                // Best effort cleanup for a failed download.
            }

            throw;
        }
    }

    private static string GetModelFileName(string model)
    {
        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Whisper model is required.", nameof(model));

        foreach (var ch in model)
        {
            if (!char.IsLetterOrDigit(ch) && ch is not '-' and not '_' and not '.')
                throw new InvalidOperationException($"Invalid Whisper model name '{model}'.");
        }

        return $"ggml-{model}.bin";
    }

    // whisper.cpp JSON output format
    private record WhisperOutput
    {
        public WhisperResult? Result { get; init; }
        public WhisperSegment[]? Transcription { get; init; }
    }

    private record WhisperResult
    {
        public string? Language { get; init; }
    }

    private record WhisperSegment
    {
        public WhisperTimestamps Timestamps { get; init; } = new();
        public string Text { get; init; } = "";
    }

    private record WhisperTimestamps
    {
        public string From { get; init; } = "00:00:00";
        public string To { get; init; } = "00:00:00";
    }

    private static long ParseTimestampMs(string timestamp)
    {
        if (TimeSpan.TryParseExact(timestamp, @"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture, out var ts))
            return (long)ts.TotalMilliseconds;
        if (TimeSpan.TryParseExact(timestamp, @"hh\:mm\:ss\,fff", CultureInfo.InvariantCulture, out ts))
            return (long)ts.TotalMilliseconds;
        if (TimeSpan.TryParse(timestamp, CultureInfo.InvariantCulture, out ts))
            return (long)ts.TotalMilliseconds;
        return 0;
    }
}
