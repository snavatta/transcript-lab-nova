using System.Collections.Concurrent;
using System.Text.Json;
using Whisper.net;
using Whisper.net.Ggml;
using Whisper.net.LibraryLoader;

var exitCode = await WhisperNetWorkerProgram.RunAsync();
return exitCode;

internal static class WhisperNetWorkerProgram
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string ResponseBeginMarker = "__TRANSCRIPTLAB_WHISPERNET_RESPONSE_BEGIN__";
    private const string ResponseEndMarker = "__TRANSCRIPTLAB_WHISPERNET_RESPONSE_END__";

    public static async Task<int> RunAsync()
    {
        try
        {
            await using var input = Console.OpenStandardInput();
            var request = await JsonSerializer.DeserializeAsync<WhisperNetWorkerRequest>(input, JsonOptions);
            if (request is null)
                throw new InvalidOperationException("No Whisper.net worker request was provided.");

            var response = await WhisperNetWorkerProcessor.ProcessAsync(request);

            await using var output = Console.OpenStandardOutput();
            await WriteResponseAsync(output, response);
            await output.FlushAsync();
            return 0;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync(ex.Message);
            return 1;
        }
    }

    private static async Task WriteResponseAsync(Stream output, WhisperNetWorkerResponse response)
    {
        var responseBytes = JsonSerializer.SerializeToUtf8Bytes(response, JsonOptions);
        await output.WriteAsync(System.Text.Encoding.UTF8.GetBytes(ResponseBeginMarker + Environment.NewLine));
        await output.WriteAsync(responseBytes);
        await output.WriteAsync(System.Text.Encoding.UTF8.GetBytes(Environment.NewLine + ResponseEndMarker + Environment.NewLine));
    }
}

internal static class WhisperNetWorkerProcessor
{
    public static async Task<WhisperNetWorkerResponse> ProcessAsync(WhisperNetWorkerRequest request)
    {
        WhisperRuntimeConfigurator.Configure(request.Mode);

        var modelPath = await WhisperModelStore.ResolveModelPathAsync(request.Model, request.ModelsPath, request.AutoDownloadModels, CancellationToken.None);
        using var whisperFactory = WhisperFactory.FromPath(modelPath);

        var builder = whisperFactory.CreateBuilder();
        builder = string.Equals(request.LanguageMode, "Fixed", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(request.LanguageCode)
            ? builder.WithLanguage(request.LanguageCode.Trim())
            : builder.WithLanguageDetection();

        using var processor = builder.Build();
        await using var audioStream = File.OpenRead(request.AudioPath);

        var segments = new List<TranscriptSegmentDto>();
        string? detectedLanguage = null;

        await foreach (var result in processor.ProcessAsync(audioStream, CancellationToken.None))
        {
            if (!string.IsNullOrWhiteSpace(result.Text))
            {
                var segment = new TranscriptSegmentDto
                {
                    StartMs = (long)result.Start.TotalMilliseconds,
                    EndMs = (long)result.End.TotalMilliseconds,
                    Text = result.Text.Trim(),
                    Speaker = null,
                };
                segments.Add(segment);

                if (request.LogSegments)
                {
                    await Console.Error.WriteLineAsync(
                        $"Whisper.net segment {segments.Count}: startMs={segment.StartMs}, endMs={segment.EndMs}, text={FormatSegmentText(segment.Text)}");
                }
            }

            if (detectedLanguage is null && !string.IsNullOrWhiteSpace(result.Language))
                detectedLanguage = result.Language;
        }

        var plainText = string.Join(" ", segments.Select(segment => segment.Text));
        var durationMs = segments.Count > 0 ? segments.Max(segment => segment.EndMs) : 0L;

        return new WhisperNetWorkerResponse
        {
            PlainText = plainText,
            Segments = [.. segments],
            DetectedLanguage = detectedLanguage,
            DurationMs = durationMs,
        };
    }

    private static string FormatSegmentText(string text)
        => text.Replace('\r', ' ').Replace('\n', ' ').Trim();
}

internal static class WhisperRuntimeConfigurator
{
    public static void Configure(WhisperNetWorkerMode mode)
    {
        RuntimeOptions.RuntimeLibraryOrder = mode switch
        {
            WhisperNetWorkerMode.Cpu => [RuntimeLibrary.Cpu, RuntimeLibrary.CpuNoAvx],
            WhisperNetWorkerMode.Cuda => [RuntimeLibrary.Cuda],
            _ => throw new InvalidOperationException($"Unsupported Whisper.net worker mode '{mode}'."),
        };
    }
}

internal static class WhisperModelStore
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ModelLocks = new(StringComparer.OrdinalIgnoreCase);
    private const int BufferSize = 1024 * 80;
    private const int PercentStep = 10;
    private const long UnknownLengthLogStepBytes = 25L * 1024 * 1024;

    public static async Task<string> ResolveModelPathAsync(string model, string modelsPath, bool autoDownloadModels, CancellationToken ct)
    {
        var modelFileName = GetModelFileName(model);
        var modelsRoot = Path.GetFullPath(modelsPath);
        Directory.CreateDirectory(modelsRoot);

        var path = Path.GetFullPath(Path.Combine(modelsRoot, modelFileName));
        if (File.Exists(path))
            return path;

        if (!autoDownloadModels)
            throw new FileNotFoundException($"Whisper model not found at {path}. Auto-download is disabled.");

        var modelLock = ModelLocks.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));
        await modelLock.WaitAsync(ct);
        try
        {
            if (File.Exists(path))
                return path;

            var tempPath = $"{path}.download";
            await Console.Error.WriteLineAsync($"Whisper.net worker downloading model '{model}' to {path}");
            using var modelStream = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(MapToGgmlType(model));
            await using var destination = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await CopyToWithProgressAsync(modelStream, destination, modelStream.CanSeek ? modelStream.Length : null, $"Whisper.net model {model}", ct);
            await destination.FlushAsync(ct);
            File.Move(tempPath, path, overwrite: true);
            return path;
        }
        finally
        {
            modelLock.Release();
        }
    }

    private static GgmlType MapToGgmlType(string model) => model.ToLowerInvariant() switch
    {
        "tiny" => GgmlType.Tiny,
        "base" => GgmlType.Base,
        "small" => GgmlType.Small,
        "medium" => GgmlType.Medium,
        "large" => GgmlType.LargeV3,
        "large-v3-turbo" => GgmlType.LargeV3Turbo,
        _ => throw new ArgumentException($"Unsupported Whisper.net model '{model}'.", nameof(model)),
    };

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

    private static async Task CopyToWithProgressAsync(
        Stream source,
        Stream destination,
        long? totalBytes,
        string artifactName,
        CancellationToken ct)
    {
        var buffer = new byte[BufferSize];
        long bytesCopied = 0;
        var nextPercentToLog = PercentStep;
        var nextUnknownLengthLogBytes = UnknownLengthLogStepBytes;

        if (totalBytes is > 0)
            await Console.Error.WriteLineAsync($"{artifactName}: size={totalBytes.Value} bytes");
        else
            await Console.Error.WriteLineAsync($"{artifactName}: total size unknown");

        while (true)
        {
            var bytesRead = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
            if (bytesRead == 0)
                break;

            await destination.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            bytesCopied += bytesRead;

            if (totalBytes is > 0)
            {
                var progressPercent = (int)((bytesCopied * 100) / totalBytes.Value);
                while (progressPercent >= nextPercentToLog && nextPercentToLog < 100)
                {
                    await Console.Error.WriteLineAsync($"{artifactName}: {nextPercentToLog}% ({bytesCopied}/{totalBytes.Value} bytes)");
                    nextPercentToLog += PercentStep;
                }
            }
            else if (bytesCopied >= nextUnknownLengthLogBytes)
            {
                await Console.Error.WriteLineAsync($"{artifactName}: {bytesCopied} bytes received");
                nextUnknownLengthLogBytes += UnknownLengthLogStepBytes;
            }
        }

        await Console.Error.WriteLineAsync($"{artifactName}: completed. total={bytesCopied} bytes");
    }
}

internal enum WhisperNetWorkerMode
{
    Cpu,
    Cuda,
    OpenVino,
}

internal sealed record WhisperNetWorkerRequest
{
    public required WhisperNetWorkerMode Mode { get; init; }
    public required string AudioPath { get; init; }
    public required string Model { get; init; }
    public required string LanguageMode { get; init; }
    public string? LanguageCode { get; init; }
    public required string ModelsPath { get; init; }
    public required bool AutoDownloadModels { get; init; }
    public required bool LogSegments { get; init; }
}

internal sealed record WhisperNetWorkerResponse
{
    public required string PlainText { get; init; }
    public required TranscriptSegmentDto[] Segments { get; init; }
    public string? DetectedLanguage { get; init; }
    public long? DurationMs { get; init; }
}

internal sealed record TranscriptSegmentDto
{
    public required long StartMs { get; init; }
    public required long EndMs { get; init; }
    public required string Text { get; init; }
    public string? Speaker { get; init; }
}
