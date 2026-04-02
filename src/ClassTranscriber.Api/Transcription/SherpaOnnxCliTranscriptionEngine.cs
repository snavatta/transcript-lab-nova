using System.Diagnostics;
using System.Text.Json;
using ClassTranscriber.Api.Contracts;
using ClassTranscriber.Api.Domain;
using Microsoft.Extensions.Options;

namespace ClassTranscriber.Api.Transcription;

public sealed class SherpaOnnxOptions
{
    public string PythonPath { get; set; } = "python3";
    public string AdapterScriptPath { get; set; } = "Tools/sherpa_onnx_adapter.py";
    public string ModelsPath { get; set; } = "/data/models/sherpa-onnx";
    public string Provider { get; set; } = "cpu";
    public int NumThreads { get; set; } = 4;
}

public sealed class SherpaOnnxCliTranscriptionEngine : IRegisteredTranscriptionEngine
{
    public const string DefaultPythonExecutableName = "python3";
    private readonly SherpaOnnxOptions _options;
    private readonly ILogger<SherpaOnnxCliTranscriptionEngine> _logger;

    public SherpaOnnxCliTranscriptionEngine(
        IOptions<SherpaOnnxOptions> options,
        ILogger<SherpaOnnxCliTranscriptionEngine> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public string EngineId => "SherpaOnnx";

    // Start conservative until the runtime contract is broadened and verified.
    public IReadOnlyCollection<string> SupportedModels { get; } = ["small", "medium"];

    public async Task<TranscriptionResult> TranscribeAsync(string audioPath, ProjectSettings settings, CancellationToken ct = default)
    {
        var executablePath = string.IsNullOrWhiteSpace(_options.PythonPath)
            ? DefaultPythonExecutableName
            : _options.PythonPath;

        var modelDir = ResolveModelDirectory(settings.Model);
        var adapterScriptPath = ResolveAdapterScriptPath();
        var outputDir = Path.GetDirectoryName(audioPath) ?? Path.GetTempPath();
        var baseName = Path.GetFileNameWithoutExtension(audioPath);
        var outputJsonPath = Path.Combine(outputDir, $"{baseName}.sherpa.json");
        var language = settings.LanguageMode == "Fixed" && !string.IsNullOrWhiteSpace(settings.LanguageCode)
            ? settings.LanguageCode!
            : "auto";

        var args = string.Join(' ',
            QuoteArgument(adapterScriptPath),
            "--model-dir", QuoteArgument(modelDir),
            "--input-wav", QuoteArgument(audioPath),
            "--output-json", QuoteArgument(outputJsonPath),
            "--language", QuoteArgument(language),
            "--provider", QuoteArgument(_options.Provider),
            "--num-threads", _options.NumThreads.ToString(System.Globalization.CultureInfo.InvariantCulture));

        _logger.LogInformation("Starting SherpaOnnx transcription for {AudioPath} with model {Model}", audioPath, settings.Model);
        _logger.LogDebug("SherpaOnnx args: {Args}", args);

        var psi = new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi);
        if (process is null)
            throw new InvalidOperationException("Failed to start SherpaOnnx process.");

        var stderr = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"SherpaOnnx failed with exit code {process.ExitCode}: {stderr}");
        }

        if (!File.Exists(outputJsonPath))
        {
            throw new FileNotFoundException(
                $"SherpaOnnx adapter did not produce output at {outputJsonPath}. Check the local adapter runtime and model configuration.");
        }

        var json = await File.ReadAllTextAsync(outputJsonPath, ct);
        var output = JsonSerializer.Deserialize<SherpaOnnxOutput>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        });

        if (output is null)
            throw new InvalidOperationException("Failed to parse SherpaOnnx output.");

        var segments = (output.Segments ?? [])
            .Where(segment => !string.IsNullOrWhiteSpace(segment.Text))
            .Select(segment => new TranscriptSegmentDto
            {
                StartMs = segment.StartMs,
                EndMs = segment.EndMs,
                Text = segment.Text.Trim(),
                Speaker = segment.Speaker,
            })
            .ToArray();

        var plainText = !string.IsNullOrWhiteSpace(output.Text)
            ? output.Text.Trim()
            : string.Join(" ", segments.Select(segment => segment.Text));
        var durationMs = output.DurationMs ?? (segments.Length > 0 ? segments.Max(segment => segment.EndMs) : 0L);

        try
        {
            File.Delete(outputJsonPath);
        }
        catch
        {
            // Best effort cleanup only.
        }

        return new TranscriptionResult(plainText, segments, output.DetectedLanguage, durationMs);
    }

    private string ResolveModelDirectory(string model)
    {
        var modelDir = Path.GetFullPath(Path.Combine(_options.ModelsPath, model));
        if (!Directory.Exists(modelDir))
        {
            throw new DirectoryNotFoundException(
                $"SherpaOnnx model directory not found at {modelDir}. Place the model assets under Transcription:SherpaOnnx:ModelsPath/<model>/.");
        }

        return modelDir;
    }

    private string ResolveAdapterScriptPath()
    {
        var configuredPath = string.IsNullOrWhiteSpace(_options.AdapterScriptPath)
            ? Path.Combine("Tools", "sherpa_onnx_adapter.py")
            : _options.AdapterScriptPath;
        var resolvedPath = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configuredPath));

        if (!File.Exists(resolvedPath))
        {
            throw new FileNotFoundException(
                $"SherpaOnnx adapter script not found at {resolvedPath}. Ensure Tools/sherpa_onnx_adapter.py is deployed with the API.");
        }

        return resolvedPath;
    }

    private static string QuoteArgument(string value)
        => $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";

    private sealed record SherpaOnnxOutput
    {
        public string? Text { get; init; }
        public string? DetectedLanguage { get; init; }
        public long? DurationMs { get; init; }
        public SherpaOnnxSegment[]? Segments { get; init; }
    }

    private sealed record SherpaOnnxSegment
    {
        public long StartMs { get; init; }
        public long EndMs { get; init; }
        public string Text { get; init; } = "";
        public string? Speaker { get; init; }
    }
}