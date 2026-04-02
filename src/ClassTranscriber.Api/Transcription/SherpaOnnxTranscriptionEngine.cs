using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClassTranscriber.Api.Contracts;
using ClassTranscriber.Api.Domain;
using Microsoft.Extensions.Options;

namespace ClassTranscriber.Api.Transcription;

public abstract class SherpaOnnxEngineOptionsBase
{
    public string ModelsPath { get; set; } = "/data/models/sherpa-onnx";
    public string Provider { get; set; } = "cpu";
    public int NumThreads { get; set; } = 4;
    public bool AutoDownloadModels { get; set; } = true;
    public string ModelDownloadBaseUrl { get; set; } = "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models";
    public string WorkerPath { get; set; } = "ClassTranscriber.SherpaOnnx.Worker.dll";
    public string DotNetHostPath { get; set; } = "dotnet";
}

public sealed class SherpaOnnxOptions : SherpaOnnxEngineOptionsBase
{
    public string PythonPath { get; set; } = "python3";
    public string AdapterScriptPath { get; set; } = "Tools/sherpa_onnx_adapter.py";
}

public sealed class SherpaOnnxSenseVoiceOptions : SherpaOnnxEngineOptionsBase
{
    public SherpaOnnxSenseVoiceOptions()
    {
        ModelsPath = "/data/models/sherpa-onnx-sense-voice";
    }
}

public enum SherpaOnnxBackend
{
    SenseVoice,
    Whisper,
}

public sealed record SherpaOnnxModelDefinition(
    string ModelDirectory,
    SherpaOnnxBackend Backend,
    string TokensPath,
    string? ModelPath,
    string? EncoderPath,
    string? DecoderPath,
    bool UseInverseTextNormalization,
    string Task);

public sealed record WaveFileData(float[] Samples, int SampleRate, long DurationMs);

public sealed record SherpaOnnxDownloadDefinition(
    SherpaOnnxBackend Backend,
    string TarballName,
    string TokensFileName,
    string? ModelFileName = null,
    string? EncoderFileName = null,
    string? DecoderFileName = null,
    bool UseInverseTextNormalization = true,
    string Task = "transcribe")
{
    public IReadOnlyCollection<string> RequiredFiles
    {
        get
        {
            var files = new List<string> { TokensFileName };
            if (ModelFileName is not null)
                files.Add(ModelFileName);
            if (EncoderFileName is not null)
                files.Add(EncoderFileName);
            if (DecoderFileName is not null)
                files.Add(DecoderFileName);
            return files;
        }
    }

    public bool Matches(SherpaOnnxModelDefinition definition)
        => definition.Backend == Backend
            && MatchesFileName(definition.ModelPath, ModelFileName)
            && MatchesFileName(definition.EncoderPath, EncoderFileName)
            && MatchesFileName(definition.DecoderPath, DecoderFileName)
            && MatchesFileName(definition.TokensPath, TokensFileName);

    private static bool MatchesFileName(string? path, string? expectedFileName)
        => expectedFileName is null
            ? path is null
            : string.Equals(Path.GetFileName(path), expectedFileName, StringComparison.OrdinalIgnoreCase);
}

public static class SherpaOnnxWhisperModelDownloadCatalog
{
    private static readonly Dictionary<string, SherpaOnnxDownloadDefinition> Definitions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["small"] = new(SherpaOnnxBackend.Whisper, "sherpa-onnx-whisper-small.tar.bz2", "small-tokens.txt", EncoderFileName: "small-encoder.onnx", DecoderFileName: "small-decoder.onnx"),
        ["medium"] = new(SherpaOnnxBackend.Whisper, "sherpa-onnx-whisper-medium.tar.bz2", "medium-tokens.txt", EncoderFileName: "medium-encoder.onnx", DecoderFileName: "medium-decoder.onnx"),
    };

    public static bool TryGet(string model, out SherpaOnnxDownloadDefinition definition)
        => Definitions.TryGetValue(model, out definition!);
}

public static class SherpaOnnxSenseVoiceModelDownloadCatalog
{
    private static readonly Dictionary<string, SherpaOnnxDownloadDefinition> Definitions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["small"] = new(
            SherpaOnnxBackend.SenseVoice,
            "sherpa-onnx-sense-voice-zh-en-ja-ko-yue-2024-07-17.tar.bz2",
            "tokens.txt",
            ModelFileName: "model.int8.onnx",
            UseInverseTextNormalization: true,
            Task: "transcribe"),
    };

    public static bool TryGet(string model, out SherpaOnnxDownloadDefinition definition)
        => Definitions.TryGetValue(model, out definition!);
}

public static class SherpaOnnxModelResolver
{
    public static SherpaOnnxModelDefinition Resolve(string modelsPath, string model)
    {
        var modelDir = Path.GetFullPath(Path.Combine(modelsPath, model));
        if (!Directory.Exists(modelDir))
        {
            throw new DirectoryNotFoundException(
                $"SherpaOnnx model directory not found at {modelDir}. Place the model assets under Transcription:SherpaOnnx:ModelsPath/<model>/.");
        }

        var configPath = Path.Combine(modelDir, "config.json");
        if (File.Exists(configPath))
            return ResolveFromConfig(modelDir, configPath);

        if (File.Exists(Path.Combine(modelDir, "model.onnx")) && File.Exists(Path.Combine(modelDir, "tokens.txt")))
        {
            return new SherpaOnnxModelDefinition(
                modelDir,
                SherpaOnnxBackend.SenseVoice,
                Path.Combine(modelDir, "tokens.txt"),
                Path.Combine(modelDir, "model.onnx"),
                null,
                null,
                true,
                "transcribe");
        }

        if (File.Exists(Path.Combine(modelDir, "encoder.onnx"))
            && File.Exists(Path.Combine(modelDir, "decoder.onnx"))
            && File.Exists(Path.Combine(modelDir, "tokens.txt")))
        {
            return new SherpaOnnxModelDefinition(
                modelDir,
                SherpaOnnxBackend.Whisper,
                Path.Combine(modelDir, "tokens.txt"),
                null,
                Path.Combine(modelDir, "encoder.onnx"),
                Path.Combine(modelDir, "decoder.onnx"),
                true,
                "transcribe");
        }

        throw new InvalidOperationException(
            $"No SherpaOnnx model configuration found in {modelDir}. Add config.json or provide the expected default files.");
    }

    private static SherpaOnnxModelDefinition ResolveFromConfig(string modelDir, string configPath)
    {
        var config = JsonSerializer.Deserialize<SherpaOnnxConfigFile>(
            File.ReadAllText(configPath),
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            }) ?? throw new InvalidOperationException($"Failed to parse SherpaOnnx config at {configPath}.");

        var backend = (config.Backend ?? "sense_voice").Trim().ToLowerInvariant();
        return backend switch
        {
            "sense_voice" => new SherpaOnnxModelDefinition(
                modelDir,
                SherpaOnnxBackend.SenseVoice,
                ResolveExistingPath(modelDir, config.Tokens ?? "tokens.txt"),
                ResolveExistingPath(modelDir, config.Model ?? "model.onnx"),
                null,
                null,
                config.UseInverseTextNormalization ?? true,
                "transcribe"),
            "whisper" => new SherpaOnnxModelDefinition(
                modelDir,
                SherpaOnnxBackend.Whisper,
                ResolveExistingPath(modelDir, config.Tokens ?? "tokens.txt"),
                null,
                ResolveExistingPath(modelDir, config.Encoder ?? "encoder.onnx"),
                ResolveExistingPath(modelDir, config.Decoder ?? "decoder.onnx"),
                true,
                string.IsNullOrWhiteSpace(config.Task) ? "transcribe" : config.Task!.Trim()),
            _ => throw new InvalidOperationException($"Unsupported SherpaOnnx backend '{config.Backend}'."),
        };
    }

    private static string ResolveExistingPath(string modelDir, string relativeOrAbsolutePath)
    {
        var resolvedPath = Path.IsPathRooted(relativeOrAbsolutePath)
            ? relativeOrAbsolutePath
            : Path.GetFullPath(Path.Combine(modelDir, relativeOrAbsolutePath));

        if (!File.Exists(resolvedPath))
            throw new FileNotFoundException($"SherpaOnnx model asset not found at {resolvedPath}.");

        return resolvedPath;
    }

    private sealed record SherpaOnnxConfigFile
    {
        public string? Backend { get; init; }
        public string? Model { get; init; }
        public string? Tokens { get; init; }
        public string? Encoder { get; init; }
        public string? Decoder { get; init; }
        public string? Task { get; init; }

        [JsonPropertyName("use_itn")]
        public bool? UseInverseTextNormalization { get; init; }
    }
}

public abstract class SherpaOnnxTranscriptionEngineBase<TOptions> : IRegisteredTranscriptionEngine
    where TOptions : SherpaOnnxEngineOptionsBase
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ModelLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly TOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISherpaOnnxWorkerRunner _workerRunner;
    private readonly ILogger _logger;

    protected SherpaOnnxTranscriptionEngineBase(
        IOptions<TOptions> options,
        IHttpClientFactory httpClientFactory,
        ISherpaOnnxWorkerRunner workerRunner,
        ILogger logger)
    {
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
        _workerRunner = workerRunner;
        _logger = logger;
    }

    public abstract string EngineId { get; }

    protected abstract IReadOnlyCollection<string> CandidateModels { get; }

    protected abstract bool TryGetDownloadDefinition(string model, out SherpaOnnxDownloadDefinition definition);

    public IReadOnlyCollection<string> SupportedModels
        => CandidateModels
            .Where(IsConfiguredModelAvailableOrDownloadable)
            .ToArray();

    public string? GetAvailabilityError()
    {
        if (string.IsNullOrWhiteSpace(_options.WorkerPath))
            return $"{EngineId} is unavailable because its worker path is not configured.";

        if (!ProcessPathResolver.FileExists(_options.WorkerPath))
            return $"{EngineId} is unavailable because the SherpaOnnx worker was not found at {ProcessPathResolver.ResolveWorkerPath(_options.WorkerPath)}.";

        var resolvedWorkerPath = ProcessPathResolver.ResolveWorkerPath(_options.WorkerPath);
        if (string.Equals(Path.GetExtension(resolvedWorkerPath), ".dll", StringComparison.OrdinalIgnoreCase)
            && !ProcessPathResolver.ExecutableExists(_options.DotNetHostPath))
            return $"{EngineId} is unavailable because the dotnet host '{_options.DotNetHostPath}' could not be found.";

        if (SupportedModels.Count > 0)
            return null;

        var modelsRoot = Path.GetFullPath(_options.ModelsPath);
        return _options.AutoDownloadModels
            ? $"{EngineId} is unavailable because no installed or downloadable model definitions are available under {modelsRoot}."
            : $"{EngineId} is unavailable because no valid model assets were found under {modelsRoot}.";
    }

    public async Task<TranscriptionResult> TranscribeAsync(string audioPath, ProjectSettings settings, CancellationToken ct = default)
    {
        var availabilityError = GetAvailabilityError();
        if (availabilityError is not null)
            throw new InvalidOperationException(availabilityError);

        var modelDefinition = await ResolveModelDefinitionAsync(settings.Model, ct);
        _logger.LogInformation(
            "{EngineId} resolved model assets for {Model}. tokens={TokensPath}, model={ModelPath}, encoder={EncoderPath}, decoder={DecoderPath}",
            EngineId,
            settings.Model,
            modelDefinition.TokensPath,
            modelDefinition.ModelPath ?? "<none>",
            modelDefinition.EncoderPath ?? "<none>",
            modelDefinition.DecoderPath ?? "<none>");
        var request = new SherpaOnnxWorkerRequest
        {
            AudioPath = audioPath,
            Backend = modelDefinition.Backend,
            TokensPath = modelDefinition.TokensPath,
            ModelPath = modelDefinition.ModelPath,
            EncoderPath = modelDefinition.EncoderPath,
            DecoderPath = modelDefinition.DecoderPath,
            UseInverseTextNormalization = modelDefinition.UseInverseTextNormalization,
            Task = modelDefinition.Task,
            Provider = _options.Provider,
            NumThreads = _options.NumThreads,
            LanguageMode = settings.LanguageMode,
            LanguageCode = settings.LanguageCode,
        };

        _logger.LogInformation(
            "Starting {EngineId} transcription for {AudioPath} with model {Model}, provider={Provider}",
            EngineId,
            audioPath,
            settings.Model,
            _options.Provider);

        var response = await _workerRunner.RunAsync(request, _options.WorkerPath, _options.DotNetHostPath, ct);
        _logger.LogInformation(
            "{EngineId} worker completed for {AudioPath}. segments={SegmentCount}, detectedLanguage={DetectedLanguage}",
            EngineId,
            audioPath,
            response.Segments.Length,
            response.DetectedLanguage ?? "unknown");

        return new TranscriptionResult(
            response.PlainText,
            response.Segments,
            response.DetectedLanguage,
            response.DurationMs);
    }

    private async Task<SherpaOnnxModelDefinition> ResolveModelDefinitionAsync(string model, CancellationToken ct)
    {
        try
        {
            return ValidateResolvedModelDefinition(model, SherpaOnnxModelResolver.Resolve(_options.ModelsPath, model));
        }
        catch (Exception ex) when (CanAttemptAutoDownload(model, ex))
        {
            await EnsureModelDownloadedAsync(model, ct);
            return ValidateResolvedModelDefinition(model, SherpaOnnxModelResolver.Resolve(_options.ModelsPath, model));
        }
    }

    private bool IsConfiguredModelAvailableOrDownloadable(string model)
        => IsConfiguredModelAvailable(model)
            || (_options.AutoDownloadModels && TryGetDownloadDefinition(model, out _));

    private bool IsConfiguredModelAvailable(string model)
    {
        try
        {
            _ = ValidateResolvedModelDefinition(model, SherpaOnnxModelResolver.Resolve(_options.ModelsPath, model));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private SherpaOnnxModelDefinition ValidateResolvedModelDefinition(string model, SherpaOnnxModelDefinition definition)
    {
        if (!TryGetDownloadDefinition(model, out var downloadDefinition))
            return definition;

        if (downloadDefinition.Matches(definition))
            return definition;

        throw CreateInstalledAssetMismatchException(model, definition, downloadDefinition);
    }

    protected virtual InvalidOperationException CreateInstalledAssetMismatchException(
        string model,
        SherpaOnnxModelDefinition definition,
        SherpaOnnxDownloadDefinition downloadDefinition)
        => new(
            $"{EngineId} model '{model}' is installed with assets that do not match the expected files for this engine. " +
            "Replace the model assets or enable auto-download to refresh them.");

    private bool CanAttemptAutoDownload(string model, Exception exception)
        => _options.AutoDownloadModels
            && TryGetDownloadDefinition(model, out _)
            && IsMissingModelAssetFailure(exception);

    private static bool IsMissingModelAssetFailure(Exception exception)
        => exception switch
        {
            DirectoryNotFoundException => true,
            FileNotFoundException => true,
            InvalidOperationException invalidOperationException
                when invalidOperationException.Message.Contains("No SherpaOnnx model configuration found", StringComparison.OrdinalIgnoreCase)
                    || invalidOperationException.Message.Contains("do not match the expected files", StringComparison.OrdinalIgnoreCase)
                    || invalidOperationException.Message.Contains("legacy English-only Whisper assets", StringComparison.OrdinalIgnoreCase) => true,
            _ => false,
        };

    private async Task EnsureModelDownloadedAsync(string model, CancellationToken ct)
    {
        if (!TryGetDownloadDefinition(model, out var downloadDefinition))
            throw new InvalidOperationException($"No download definition is registered for {EngineId} model '{model}'.");

        var modelsRoot = Path.GetFullPath(_options.ModelsPath);
        Directory.CreateDirectory(modelsRoot);

        var modelDir = SherpaOnnxModelDownloads.GetModelDirectory(modelsRoot, model);
        var modelLock = ModelLocks.GetOrAdd(modelDir, _ => new SemaphoreSlim(1, 1));
        await modelLock.WaitAsync(ct);
        try
        {
            if (IsConfiguredModelAvailable(model))
                return;

            await DownloadModelAsync(model, modelDir, downloadDefinition, ct);
        }
        finally
        {
            modelLock.Release();
        }
    }

    private Task DownloadModelAsync(
        string model,
        string modelDir,
        SherpaOnnxDownloadDefinition definition,
        CancellationToken ct)
        => SherpaOnnxModelDownloads.DownloadModelAsync(
            _httpClientFactory,
            _options.ModelDownloadBaseUrl,
            EngineId,
            model,
            modelDir,
            definition,
            _logger,
            ct);
}

public sealed class SherpaOnnxTranscriptionEngine : SherpaOnnxTranscriptionEngineBase<SherpaOnnxOptions>
{
    private static readonly string[] SupportedWhisperModels = ["small", "medium"];

    public SherpaOnnxTranscriptionEngine(
        IOptions<SherpaOnnxOptions> options,
        IHttpClientFactory httpClientFactory,
        ISherpaOnnxWorkerRunner workerRunner,
        ILogger<SherpaOnnxTranscriptionEngine> logger)
        : base(options, httpClientFactory, workerRunner, logger)
    {
    }

    public override string EngineId => "SherpaOnnx";

    protected override IReadOnlyCollection<string> CandidateModels => SupportedWhisperModels;

    protected override bool TryGetDownloadDefinition(string model, out SherpaOnnxDownloadDefinition definition)
        => SherpaOnnxWhisperModelDownloadCatalog.TryGet(model, out definition);

    protected override InvalidOperationException CreateInstalledAssetMismatchException(
        string model,
        SherpaOnnxModelDefinition definition,
        SherpaOnnxDownloadDefinition downloadDefinition)
    {
        if (string.Equals(model, "medium", StringComparison.OrdinalIgnoreCase)
            && definition.Backend == SherpaOnnxBackend.Whisper)
        {
            return new InvalidOperationException(
                $"SherpaOnnx model '{model}' is installed with legacy English-only Whisper assets. " +
                "Replace it with the multilingual Whisper base assets or enable auto-download to refresh it.");
        }

        return base.CreateInstalledAssetMismatchException(model, definition, downloadDefinition);
    }
}

public sealed class SherpaOnnxSenseVoiceTranscriptionEngine : SherpaOnnxTranscriptionEngineBase<SherpaOnnxSenseVoiceOptions>
{
    private static readonly string[] SupportedSenseVoiceModels = ["small"];

    public SherpaOnnxSenseVoiceTranscriptionEngine(
        IOptions<SherpaOnnxSenseVoiceOptions> options,
        IHttpClientFactory httpClientFactory,
        ISherpaOnnxWorkerRunner workerRunner,
        ILogger<SherpaOnnxSenseVoiceTranscriptionEngine> logger)
        : base(options, httpClientFactory, workerRunner, logger)
    {
    }

    public override string EngineId => "SherpaOnnxSenseVoice";

    protected override IReadOnlyCollection<string> CandidateModels => SupportedSenseVoiceModels;

    protected override bool TryGetDownloadDefinition(string model, out SherpaOnnxDownloadDefinition definition)
        => SherpaOnnxSenseVoiceModelDownloadCatalog.TryGet(model, out definition);
}
