using System.Collections.Concurrent;
using ClassTranscriber.Api.Domain;
using Microsoft.Extensions.Options;

namespace ClassTranscriber.Api.Transcription;

public sealed class OpenVinoGenAiOptions
{
    public string ModelsPath { get; set; } = "/data/models/openvino-genai";
    public bool AutoDownloadModels { get; set; } = true;
    public string ModelDownloadBaseUrl { get; set; } = "https://huggingface.co";
    public string PythonPath { get; set; } = "python3";
    public string WorkerScriptPath { get; set; } = "Tools/openvino_genai_worker.py";
    public string Device { get; set; } = "GPU";
    public bool LogSegments { get; set; }
}

public sealed record OpenVinoGenAiModelDefinition(
    string Model,
    string Repository,
    IReadOnlyCollection<string> RequiredFiles);

public static class OpenVinoGenAiModelCatalog
{
    // INT8 models include openvino_config.json (quantization parameters).
    // FP16 models do not publish that file.
    private static readonly string[] CommonRequiredFilesInt8 =
    [
        "config.json",
        "generation_config.json",
        "openvino_config.json",
        "openvino_encoder_model.xml",
        "openvino_encoder_model.bin",
        "openvino_decoder_model.xml",
        "openvino_decoder_model.bin",
        "openvino_tokenizer.xml",
        "openvino_tokenizer.bin",
        "openvino_detokenizer.xml",
        "openvino_detokenizer.bin",
        "preprocessor_config.json",
        "tokenizer.json",
        "tokenizer_config.json",
        "special_tokens_map.json",
        "merges.txt",
        "vocab.json",
    ];

    private static readonly string[] CommonRequiredFilesFp16 =
    [
        "config.json",
        "generation_config.json",
        "openvino_encoder_model.xml",
        "openvino_encoder_model.bin",
        "openvino_decoder_model.xml",
        "openvino_decoder_model.bin",
        "openvino_tokenizer.xml",
        "openvino_tokenizer.bin",
        "openvino_detokenizer.xml",
        "openvino_detokenizer.bin",
        "preprocessor_config.json",
        "tokenizer.json",
        "tokenizer_config.json",
        "special_tokens_map.json",
        "merges.txt",
        "vocab.json",
    ];

    // Two-decoder non-stateful format (exported with --disable-stateful): no openvino_config.json,
    // has separate openvino_decoder_with_past_model.* for subsequent decode steps.
    private static readonly string[] CommonRequiredFilesWithPast =
    [
        "config.json",
        "generation_config.json",
        "openvino_encoder_model.xml",
        "openvino_encoder_model.bin",
        "openvino_decoder_model.xml",
        "openvino_decoder_model.bin",
        "openvino_decoder_with_past_model.xml",
        "openvino_decoder_with_past_model.bin",
        "openvino_tokenizer.xml",
        "openvino_tokenizer.bin",
        "openvino_detokenizer.xml",
        "openvino_detokenizer.bin",
        "preprocessor_config.json",
        "tokenizer.json",
        "tokenizer_config.json",
        "special_tokens_map.json",
        "merges.txt",
        "vocab.json",
    ];

    private static readonly IReadOnlyDictionary<string, OpenVinoGenAiModelDefinition> Definitions =
        new Dictionary<string, OpenVinoGenAiModelDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["tiny-int8"] = new(
                "tiny-int8",
                "OpenVINO/whisper-tiny-int8-ov",
                CommonRequiredFilesInt8),
            ["tiny-fp16"] = new(
                "tiny-fp16",
                "OpenVINO/whisper-tiny-fp16-ov",
                CommonRequiredFilesFp16),
            ["base-int8"] = new(
                "base-int8",
                "OpenVINO/whisper-base-int8-ov",
                CommonRequiredFilesInt8),
            ["base-fp16"] = new(
                "base-fp16",
                "OpenVINO/whisper-base-fp16-ov",
                CommonRequiredFilesFp16),
            ["small-int8"] = new(
                "small-int8",
                "OpenVINO/whisper-small-int8-ov",
                CommonRequiredFilesInt8),
            ["small-fp16"] = new(
                "small-fp16",
                "OpenVINO/whisper-small-fp16-ov",
                CommonRequiredFilesFp16),
            ["medium-int8"] = new(
                "medium-int8",
                "OpenVINO/whisper-medium-int8-ov",
                CommonRequiredFilesInt8),
            ["medium-fp16"] = new(
                "medium-fp16",
                "OpenVINO/whisper-medium-fp16-ov",
                CommonRequiredFilesFp16),
            ["large-v3-int8"] = new(
                "large-v3-int8",
                "OpenVINO/whisper-large-v3-int8-ov",
                CommonRequiredFilesInt8),
            ["large-v3-fp16"] = new(
                "large-v3-fp16",
                "OpenVINO/whisper-large-v3-fp16-ov",
                CommonRequiredFilesFp16),
        };

    public static IReadOnlyCollection<string> SupportedModels => Definitions.Keys.ToArray();

    public static bool TryGet(string model, out OpenVinoGenAiModelDefinition definition)
        => Definitions.TryGetValue(model, out definition!);

    public static OpenVinoGenAiModelDefinition GetRequired(string model)
        => TryGet(model, out var definition)
            ? definition
            : throw new InvalidOperationException($"Unsupported OpenVinoGenAi model '{model}'.");
}

public static class OpenVinoGenAiModelDownloads
{
    public const string ModelDownloadClientName = "OpenVinoGenAiModelDownloads";
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> DownloadLocks = new(StringComparer.OrdinalIgnoreCase);

    public static string GetModelDirectory(string modelsPath, string model)
    {
        ValidateModelName(model);
        var modelsRoot = Path.GetFullPath(modelsPath);
        Directory.CreateDirectory(modelsRoot);
        return Path.GetFullPath(Path.Combine(modelsRoot, model));
    }

    public static void ValidateInstalledModel(string installPath, OpenVinoGenAiModelDefinition definition)
    {
        if (!Directory.Exists(installPath))
            throw new DirectoryNotFoundException($"OpenVinoGenAi model directory not found at {installPath}.");

        foreach (var relativePath in definition.RequiredFiles)
        {
            var fullPath = Path.Combine(installPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
                throw new FileNotFoundException($"OpenVinoGenAi model asset not found at {fullPath}.");
        }
    }

    public static async Task DownloadModelAsync(
        IHttpClientFactory httpClientFactory,
        string downloadBaseUrl,
        OpenVinoGenAiModelDefinition definition,
        string installPath,
        ILogger logger,
        CancellationToken ct)
    {
        var modelLock = DownloadLocks.GetOrAdd(installPath, _ => new SemaphoreSlim(1, 1));
        await modelLock.WaitAsync(ct);
        try
        {
            if (Directory.Exists(installPath))
            {
                ValidateInstalledModel(installPath, definition);
                return;
            }

            var tempPath = $"{installPath}.download";
            if (Directory.Exists(tempPath))
                Directory.Delete(tempPath, recursive: true);

            Directory.CreateDirectory(tempPath);
            try
            {
                foreach (var relativePath in definition.RequiredFiles)
                {
                    var downloadUrl = BuildDownloadUrl(downloadBaseUrl, definition.Repository, relativePath);
                    var destinationPath = Path.Combine(tempPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

                    logger.LogInformation(
                        "OpenVinoGenAi model {Model} is missing. Downloading {RelativePath} from {DownloadUrl}",
                        definition.Model,
                        relativePath,
                        downloadUrl);

                    var client = httpClientFactory.CreateClient(ModelDownloadClientName);
                    using var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
                    response.EnsureSuccessStatusCode();

                    await using var source = await response.Content.ReadAsStreamAsync(ct);
                    await using var destination = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    await DownloadProgressLogger.CopyToAsync(
                        source,
                        destination,
                        response.Content.Headers.ContentLength,
                        logger,
                        $"OpenVinoGenAi model {definition.Model}/{relativePath}",
                        ct);
                    await destination.FlushAsync(ct);
                }

                ValidateInstalledModel(tempPath, definition);

                if (Directory.Exists(installPath))
                    Directory.Delete(installPath, recursive: true);

                Directory.Move(tempPath, installPath);
                logger.LogInformation("Downloaded OpenVinoGenAi model {Model} to {InstallPath}", definition.Model, installPath);
            }
            catch
            {
                try
                {
                    if (Directory.Exists(tempPath))
                        Directory.Delete(tempPath, recursive: true);
                }
                catch
                {
                    // Best effort cleanup for a failed download.
                }

                throw;
            }
        }
        finally
        {
            modelLock.Release();
        }
    }

    private static string BuildDownloadUrl(string baseUrl, string repository, string relativePath)
    {
        static string EscapeSlashSeparated(string value)
            => string.Join("/", value.Split('/').Select(Uri.EscapeDataString));

        return $"{baseUrl.TrimEnd('/')}/{EscapeSlashSeparated(repository)}/resolve/main/{EscapeSlashSeparated(relativePath)}?download=true";
    }

    private static void ValidateModelName(string model)
    {
        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model name is required.", nameof(model));

        foreach (var ch in model)
        {
            if (!char.IsLetterOrDigit(ch) && ch is not '-' and not '_' and not '.')
                throw new InvalidOperationException($"Invalid OpenVinoGenAi model name '{model}'.");
        }
    }
}

public sealed class OpenVinoGenAiTranscriptionEngine : IRegisteredTranscriptionEngine
{
    private readonly OpenVinoGenAiOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOpenVinoGenAiWorkerRunner _workerRunner;
    private readonly IOpenVinoGenAiEnvironmentProbe _environmentProbe;
    private readonly ILogger<OpenVinoGenAiTranscriptionEngine> _logger;

    public OpenVinoGenAiTranscriptionEngine(
        IOptions<OpenVinoGenAiOptions> options,
        IHttpClientFactory httpClientFactory,
        IOpenVinoGenAiWorkerRunner workerRunner,
        IOpenVinoGenAiEnvironmentProbe environmentProbe,
        ILogger<OpenVinoGenAiTranscriptionEngine> logger)
    {
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
        _workerRunner = workerRunner;
        _environmentProbe = environmentProbe;
        _logger = logger;
    }

    public string EngineId => "OpenVinoGenAi";

    public IReadOnlyCollection<string> SupportedModels { get; } = OpenVinoGenAiModelCatalog.SupportedModels;

    public string? GetAvailabilityError()
        => _environmentProbe.GetAvailabilityError();

    public string? GetProbeError()
        => GetAvailabilityError();

    public async Task<TranscriptionResult> TranscribeAsync(string audioPath, ProjectSettings settings, CancellationToken ct = default)
    {
        var availabilityError = GetAvailabilityError();
        if (availabilityError is not null)
            throw new InvalidOperationException(availabilityError);

        var definition = OpenVinoGenAiModelCatalog.GetRequired(settings.Model);
        var installPath = OpenVinoGenAiModelDownloads.GetModelDirectory(_options.ModelsPath, definition.Model);
        await EnsureModelInstalledAsync(installPath, definition, ct);

        var request = new OpenVinoGenAiWorkerRequest
        {
            AudioPath = audioPath,
            Model = definition.Model,
            ModelPath = installPath,
            Device = string.IsNullOrWhiteSpace(_options.Device) ? "GPU" : _options.Device,
            LanguageMode = settings.LanguageMode,
            LanguageCode = settings.LanguageCode,
            LogSegments = _options.LogSegments,
        };

        _logger.LogInformation(
            "Starting {Engine} transcription for {AudioPath} with model {Model} on device {Device}",
            EngineId,
            audioPath,
            settings.Model,
            request.Device);

        var response = await _workerRunner.RunAsync(request, _options.PythonPath, _options.WorkerScriptPath, ct);

        _logger.LogInformation(
            "{Engine} transcription completed: {SegmentCount} segments, language={Language}",
            EngineId,
            response.Segments.Length,
            response.DetectedLanguage ?? "unknown");

        return new TranscriptionResult(
            response.PlainText,
            response.Segments,
            response.DetectedLanguage,
            response.DurationMs);
    }

    private async Task EnsureModelInstalledAsync(string installPath, OpenVinoGenAiModelDefinition definition, CancellationToken ct)
    {
        if (Directory.Exists(installPath))
        {
            OpenVinoGenAiModelDownloads.ValidateInstalledModel(installPath, definition);
            return;
        }

        if (!_options.AutoDownloadModels)
        {
            throw new FileNotFoundException(
                $"OpenVinoGenAi model '{definition.Model}' was not found at {installPath}. Auto-download is disabled.");
        }

        await OpenVinoGenAiModelDownloads.DownloadModelAsync(
            _httpClientFactory,
            _options.ModelDownloadBaseUrl,
            definition,
            installPath,
            _logger,
            ct);
    }
}
