using ClassTranscriber.Api.Domain;
using Microsoft.Extensions.Options;

namespace ClassTranscriber.Api.Transcription;

public sealed class WhisperNetOptions
{
    public string ModelsPath { get; set; } = "/data/models";
    public bool AutoDownloadModels { get; set; } = true;
    public bool LogSegments { get; set; }
    public string ModelDownloadBaseUrl { get; set; } = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main";
    public string WorkerPath { get; set; } = "ClassTranscriber.WhisperNet.Worker.dll";
    public string DotNetHostPath { get; set; } = "dotnet";
    public string OpenVinoDevice { get; set; } = "GPU";
    public string? OpenVinoCachePath { get; set; }
}

public abstract class WhisperNetTranscriptionEngineBase : IRegisteredTranscriptionEngine
{
    private readonly WhisperNetOptions _options;
    private readonly IWhisperNetWorkerRunner _workerRunner;
    private readonly ILogger _logger;

    protected WhisperNetTranscriptionEngineBase(
        IOptions<WhisperNetOptions> options,
        IWhisperNetWorkerRunner workerRunner,
        ILogger logger)
    {
        _options = options.Value;
        _workerRunner = workerRunner;
        _logger = logger;
    }

    public abstract string EngineId { get; }

    protected abstract WhisperNetWorkerMode WorkerMode { get; }

    public IReadOnlyCollection<string> SupportedModels { get; } = ["tiny", "base", "small", "medium", "large"];

    public string? GetAvailabilityError()
    {
        if (string.IsNullOrWhiteSpace(_options.WorkerPath))
            return $"{EngineId} is unavailable because Transcription:WhisperNet:WorkerPath is not configured.";

        if (!ProcessPathResolver.FileExists(_options.WorkerPath))
            return $"{EngineId} is unavailable because the Whisper.net worker was not found at {ProcessPathResolver.ResolveWorkerPath(_options.WorkerPath)}.";

        var resolvedWorkerPath = ProcessPathResolver.ResolveWorkerPath(_options.WorkerPath);
        if (string.Equals(Path.GetExtension(resolvedWorkerPath), ".dll", StringComparison.OrdinalIgnoreCase)
            && !ProcessPathResolver.ExecutableExists(_options.DotNetHostPath))
            return $"{EngineId} is unavailable because the dotnet host '{_options.DotNetHostPath}' could not be found.";

        return GetAdditionalAvailabilityError();
    }

    public string? GetProbeError()
        => GetAvailabilityError() ?? GetExecutionPreflightError();

    public async Task<TranscriptionResult> TranscribeAsync(string audioPath, ProjectSettings settings, CancellationToken ct = default)
    {
        var availabilityError = GetAvailabilityError();
        if (availabilityError is not null)
            throw new InvalidOperationException(availabilityError);

        var executionPreflightError = GetExecutionPreflightError();
        if (executionPreflightError is not null)
        {
            _logger.LogWarning("{Engine} runtime preflight failed: {Reason}", EngineId, executionPreflightError);
            throw new InvalidOperationException(executionPreflightError);
        }

        var request = new WhisperNetWorkerRequest
        {
            Mode = WorkerMode,
            AudioPath = audioPath,
            Model = settings.Model,
            LanguageMode = settings.LanguageMode,
            LanguageCode = settings.LanguageCode,
            ModelsPath = Path.GetFullPath(_options.ModelsPath),
            AutoDownloadModels = _options.AutoDownloadModels,
            LogSegments = _options.LogSegments,
            OpenVinoDevice = _options.OpenVinoDevice,
            OpenVinoCachePath = ResolveOptionalPath(_options.OpenVinoCachePath),
        };

        _logger.LogInformation(
            "Starting {Engine} transcription for {AudioPath} with model {Model}",
            EngineId,
            audioPath,
            settings.Model);

        var response = await _workerRunner.RunAsync(request, _options.WorkerPath, _options.DotNetHostPath, ct);

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

    private static string? ResolveOptionalPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        return ProcessPathResolver.ResolveFilePath(path);
    }

    protected virtual string? GetAdditionalAvailabilityError() => null;

    protected virtual string? GetExecutionPreflightError() => null;
}

public sealed class WhisperNetCpuTranscriptionEngine : WhisperNetTranscriptionEngineBase
{
    public WhisperNetCpuTranscriptionEngine(
        IOptions<WhisperNetOptions> options,
        IWhisperNetWorkerRunner workerRunner,
        ILogger<WhisperNetCpuTranscriptionEngine> logger)
        : base(options, workerRunner, logger)
    {
    }

    public override string EngineId => "WhisperNet";

    protected override WhisperNetWorkerMode WorkerMode => WhisperNetWorkerMode.Cpu;
}

public sealed class WhisperNetCudaTranscriptionEngine : WhisperNetTranscriptionEngineBase
{
    private readonly ICudaEnvironmentProbe _cudaEnvironmentProbe;

    public WhisperNetCudaTranscriptionEngine(
        IOptions<WhisperNetOptions> options,
        IWhisperNetWorkerRunner workerRunner,
        ICudaEnvironmentProbe cudaEnvironmentProbe,
        ILogger<WhisperNetCudaTranscriptionEngine> logger)
        : base(options, workerRunner, logger)
    {
        _cudaEnvironmentProbe = cudaEnvironmentProbe;
    }

    public override string EngineId => "WhisperNetCuda";

    protected override WhisperNetWorkerMode WorkerMode => WhisperNetWorkerMode.Cuda;

    protected override string? GetExecutionPreflightError()
        => _cudaEnvironmentProbe.GetAvailabilityError();
}

public sealed class WhisperNetOpenVinoTranscriptionEngine : WhisperNetTranscriptionEngineBase
{
    private readonly IOpenVinoEnvironmentProbe _openVinoEnvironmentProbe;

    public WhisperNetOpenVinoTranscriptionEngine(
        IOptions<WhisperNetOptions> options,
        IWhisperNetWorkerRunner workerRunner,
        IOpenVinoEnvironmentProbe openVinoEnvironmentProbe,
        ILogger<WhisperNetOpenVinoTranscriptionEngine> logger)
        : base(options, workerRunner, logger)
    {
        _openVinoEnvironmentProbe = openVinoEnvironmentProbe;
    }

    public override string EngineId => "WhisperNetOpenVino";

    protected override WhisperNetWorkerMode WorkerMode => WhisperNetWorkerMode.OpenVino;

    protected override string? GetExecutionPreflightError()
        => _openVinoEnvironmentProbe.GetAvailabilityError();
}
