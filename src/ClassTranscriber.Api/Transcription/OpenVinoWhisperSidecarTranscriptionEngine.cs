using System.Diagnostics;
using System.Net.NetworkInformation;
using ClassTranscriber.Api.Contracts;
using ClassTranscriber.Api.Domain;
using ClassTranscriber.Api.Transcription.SpeechToText;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ClassTranscriber.Api.Transcription;

// ---------------------------------------------------------------------------
// Options
// ---------------------------------------------------------------------------

public sealed class OpenVinoWhisperSidecarOptions
{
    public string PythonPath { get; set; } = "python3";
    public string ServerScriptPath { get; set; } = "Tools/openvino_whisper_sidecar.py";
    public int Port { get; set; } = 15432;
    public int StartupTimeoutSeconds { get; set; } = 120;
    public string ModelsPath { get; set; } = "/data/models/openvino-genai";
    public bool AutoDownloadModels { get; set; } = true;
    public string ModelDownloadBaseUrl { get; set; } = "https://huggingface.co";
    public string Device { get; set; } = "GPU";
    public bool LogSegments { get; set; }
}

// ---------------------------------------------------------------------------
// Environment probe
// ---------------------------------------------------------------------------

public interface IOpenVinoWhisperSidecarEnvironmentProbe
{
    string? GetAvailabilityError();
}

public sealed class OpenVinoWhisperSidecarEnvironmentProbe : IOpenVinoWhisperSidecarEnvironmentProbe
{
    private const string ImportProbeScript = "import openvino_genai; import fastapi; import uvicorn";

    private readonly OpenVinoWhisperSidecarOptions _options;
    private readonly object _sync = new();
    private string? _cachedError;
    private DateTime _lastProbeUtc;
    private bool _hasCachedResult;

    public OpenVinoWhisperSidecarEnvironmentProbe(IOptions<OpenVinoWhisperSidecarOptions> options)
    {
        _options = options.Value;
    }

    public string? GetAvailabilityError()
    {
        lock (_sync)
        {
            if (_hasCachedResult && (DateTime.UtcNow - _lastProbeUtc) < TimeSpan.FromSeconds(30))
                return _cachedError;

            _cachedError = Probe();
            _lastProbeUtc = DateTime.UtcNow;
            _hasCachedResult = true;
            return _cachedError;
        }
    }

    private string? Probe()
    {
        if (string.IsNullOrWhiteSpace(_options.PythonPath))
            return "OpenVinoWhisperSidecar is unavailable because Transcription:OpenVinoWhisperSidecar:PythonPath is not configured.";

        if (!ProcessPathResolver.ExecutableExists(_options.PythonPath))
            return $"OpenVinoWhisperSidecar is unavailable because the Python executable '{_options.PythonPath}' could not be found.";

        if (string.IsNullOrWhiteSpace(_options.ServerScriptPath))
            return "OpenVinoWhisperSidecar is unavailable because Transcription:OpenVinoWhisperSidecar:ServerScriptPath is not configured.";

        if (!File.Exists(ProcessPathResolver.ResolveFilePath(_options.ServerScriptPath)))
            return $"OpenVinoWhisperSidecar is unavailable because the sidecar script was not found at {ProcessPathResolver.ResolveFilePath(_options.ServerScriptPath)}.";

        var startInfo = new ProcessStartInfo
        {
            FileName = ResolveExecutableOrPath(_options.PythonPath),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(ImportProbeScript);

        using var process = new Process { StartInfo = startInfo };

        if (!process.Start())
            return "OpenVinoWhisperSidecar is unavailable because the Python runtime could not be started.";

        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            var detail = stderr.Trim();
            return string.IsNullOrWhiteSpace(detail)
                ? "OpenVinoWhisperSidecar is unavailable because the Python dependency check failed. Ensure openvino-genai, fastapi, and uvicorn are installed in the Python environment."
                : $"OpenVinoWhisperSidecar is unavailable: {detail.Split('\n')[^1].Trim()}";
        }

        return null;
    }

    private static string ResolveExecutableOrPath(string executableOrPath)
        => Path.IsPathRooted(executableOrPath)
            || executableOrPath.Contains(Path.DirectorySeparatorChar)
            || executableOrPath.Contains(Path.AltDirectorySeparatorChar)
            ? ProcessPathResolver.ResolveFilePath(executableOrPath)
            : executableOrPath;
}

// ---------------------------------------------------------------------------
// Sidecar process manager
// ---------------------------------------------------------------------------

public interface IOpenVinoWhisperSidecarManager : IAsyncDisposable
{
    string BaseUrl { get; }
    Task EnsureStartedAsync(CancellationToken ct);
}

public sealed class OpenVinoWhisperSidecarManager : IOpenVinoWhisperSidecarManager
{
    public const string HttpClientName = "OpenVinoWhisperSidecar";

    private readonly OpenVinoWhisperSidecarOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<OpenVinoWhisperSidecarManager> _logger;
    private readonly SemaphoreSlim _startLock = new(1, 1);
    private Process? _process;
    private bool _ready;

    public string BaseUrl => $"http://127.0.0.1:{_options.Port}";

    public OpenVinoWhisperSidecarManager(
        IOptions<OpenVinoWhisperSidecarOptions> options,
        IHttpClientFactory httpClientFactory,
        ILogger<OpenVinoWhisperSidecarManager> logger,
        IHostApplicationLifetime applicationLifetime)
    {
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;

        // Ensure the sidecar process is killed on application shutdown
        applicationLifetime.ApplicationStopping.Register(KillAndDisposePreviousProcess);
    }

    public async Task EnsureStartedAsync(CancellationToken ct)
    {
        if (_ready && _process is { HasExited: false })
            return;

        await _startLock.WaitAsync(ct);
        try
        {
            if (_ready && _process is { HasExited: false })
                return;

            await StartProcessAsync(ct);
        }
        finally
        {
            _startLock.Release();
        }
    }

    private async Task StartProcessAsync(CancellationToken ct)
    {
        KillAndDisposePreviousProcess();

        // If something else is already listening on the port (e.g. an orphaned sidecar
        // from a previous debug session that was force-killed), prefer reusing it.
        // If it's not healthy, kill it so the new process can bind.
        if (await IsPortInUseAsync())
        {
            if (await IsHealthyAsync(ct))
            {
                _logger.LogInformation(
                    "OpenVINO Whisper sidecar: port {Port} already has a healthy listener — reusing it.",
                    _options.Port);
                _ready = true;
                return;
            }

            _logger.LogWarning(
                "OpenVINO Whisper sidecar: port {Port} is in use but not healthy — killing the occupying process.",
                _options.Port);
            KillProcessOnPort(_options.Port);
        }

        var resolvedScript = ProcessPathResolver.ResolveFilePath(_options.ServerScriptPath);
        var resolvedPython = ResolveExecutableOrPath(_options.PythonPath);

        var startInfo = new ProcessStartInfo
        {
            FileName = resolvedPython,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(resolvedScript);
        startInfo.ArgumentList.Add("--port");
        startInfo.ArgumentList.Add(_options.Port.ToString());
        startInfo.ArgumentList.Add("--host");
        startInfo.ArgumentList.Add("127.0.0.1");
        startInfo.ArgumentList.Add("--models-path");
        startInfo.ArgumentList.Add(_options.ModelsPath);
        startInfo.ArgumentList.Add("--model-download-base-url");
        startInfo.ArgumentList.Add(_options.ModelDownloadBaseUrl);
        if (_options.LogSegments)
            startInfo.ArgumentList.Add("--log-segments");

        _process = new Process { StartInfo = startInfo };

        if (!_process.Start())
            throw new InvalidOperationException("Failed to start the OpenVINO Whisper sidecar process.");

        _ = ConsumeStderrAsync(_process.StandardError);

        _logger.LogInformation(
            "OpenVINO Whisper sidecar process started (pid={Pid}), waiting for readiness on port {Port}",
            _process.Id,
            _options.Port);

        await WaitForHealthAsync(ct);
        _ready = true;

        _logger.LogInformation("OpenVINO Whisper sidecar is ready on {BaseUrl}", BaseUrl);
    }

    private async Task WaitForHealthAsync(CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddSeconds(_options.StartupTimeoutSeconds);
        var client = _httpClientFactory.CreateClient(HttpClientName);

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            if (_process is { HasExited: true })
                throw new InvalidOperationException(
                    $"OpenVINO Whisper sidecar process exited unexpectedly (code={_process.ExitCode}) before becoming ready.");

            try
            {
                var response = await client.GetAsync($"{BaseUrl}/health", ct);
                if (response.IsSuccessStatusCode)
                    return;
            }
            catch (HttpRequestException)
            {
                // Not ready yet — keep polling.
            }

            await Task.Delay(500, ct);
        }

        throw new TimeoutException(
            $"OpenVINO Whisper sidecar did not become ready within {_options.StartupTimeoutSeconds}s.");
    }

    private async Task ConsumeStderrAsync(StreamReader stderr)
    {
        try
        {
            while (true)
            {
                var line = await stderr.ReadLineAsync();
                if (line is null)
                    break;
                if (!string.IsNullOrWhiteSpace(line))
                    _logger.LogInformation("OpenVINO Whisper sidecar: {Message}", line);
            }
        }
        catch
        {
            // Process ended; stop consuming.
        }
    }

    private void KillAndDisposePreviousProcess()
    {
        if (_process is null)
            return;

        try
        {
            if (!_process.HasExited)
                _process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best effort.
        }
        finally
        {
            _process.Dispose();
            _process = null;
            _ready = false;
        }
    }

    private bool IsPortInUse()
    {
        var properties = IPGlobalProperties.GetIPGlobalProperties();
        return properties.GetActiveTcpListeners()
            .Any(ep => ep.Port == _options.Port);
    }

    private Task<bool> IsPortInUseAsync()
        => Task.FromResult(IsPortInUse());

    private async Task<bool> IsHealthyAsync(CancellationToken ct)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(3));
            var response = await client.GetAsync($"{BaseUrl}/health", cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static void KillProcessOnPort(int port)
    {
        try
        {
            var properties = IPGlobalProperties.GetIPGlobalProperties();
            var connections = properties.GetActiveTcpConnections();
            // On Linux/macOS we can't get PIDs from IPGlobalProperties; use lsof/fuser as fallback.
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                var tool = OperatingSystem.IsLinux() ? "fuser" : "lsof";
                var args = OperatingSystem.IsLinux()
                    ? $"-k {port}/tcp"
                    : $"-ti :{port}";
                using var p = Process.Start(new ProcessStartInfo(tool, args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                p?.WaitForExit();
            }
        }
        catch
        {
            // Best effort — if we can't kill it, the spawn will fail and bubble up.
        }
    }

    public ValueTask DisposeAsync()
    {
        KillAndDisposePreviousProcess();
        _startLock.Dispose();
        return ValueTask.CompletedTask;
    }

    private static string ResolveExecutableOrPath(string executableOrPath)
        => Path.IsPathRooted(executableOrPath)
            || executableOrPath.Contains(Path.DirectorySeparatorChar)
            || executableOrPath.Contains(Path.AltDirectorySeparatorChar)
            ? ProcessPathResolver.ResolveFilePath(executableOrPath)
            : executableOrPath;
}

// ---------------------------------------------------------------------------
// Transcription engine
// ---------------------------------------------------------------------------

public sealed class OpenVinoWhisperSidecarTranscriptionEngine : IRegisteredTranscriptionEngine
{
    private readonly OpenVinoWhisperSidecarOptions _options;
    private readonly IOpenVinoWhisperSidecarManager _sidecarManager;
    private readonly IOpenVinoWhisperSidecarEnvironmentProbe _probe;
    private readonly IOpenVinoSidecarModelManager _modelManager;
    private readonly ISpeechToTextClient _speechToTextClient;
    private readonly ILogger<OpenVinoWhisperSidecarTranscriptionEngine> _logger;

    public OpenVinoWhisperSidecarTranscriptionEngine(
        IOptions<OpenVinoWhisperSidecarOptions> options,
        IOpenVinoWhisperSidecarManager sidecarManager,
        IOpenVinoWhisperSidecarEnvironmentProbe probe,
        IOpenVinoSidecarModelManager modelManager,
        [FromKeyedServices("OpenVinoWhisperSidecar")] ISpeechToTextClient speechToTextClient,
        ILogger<OpenVinoWhisperSidecarTranscriptionEngine> logger)
    {
        _options = options.Value;
        _sidecarManager = sidecarManager;
        _probe = probe;
        _modelManager = modelManager;
        _speechToTextClient = speechToTextClient;
        _logger = logger;
    }

    public string EngineId => "OpenVinoWhisperSidecar";

    public IReadOnlyCollection<string> SupportedModels { get; } = OpenVinoGenAiModelCatalog.SupportedModels;

    public string? GetAvailabilityError() => _probe.GetAvailabilityError();

    public string? GetProbeError() => GetAvailabilityError();

    public async Task<TranscriptionResult> TranscribeAsync(
        string audioPath,
        ProjectSettings settings,
        CancellationToken ct = default)
    {
        var availabilityError = GetAvailabilityError();
        if (availabilityError is not null)
            throw new InvalidOperationException(availabilityError);

        await _sidecarManager.EnsureStartedAsync(ct);
        await _modelManager.EnsureModelInstalledAsync(settings.Model, ct);

        _logger.LogInformation(
            "Starting {Engine} transcription for {AudioPath} with model {Model} on device {Device}",
            EngineId,
            audioPath,
            settings.Model,
            string.IsNullOrWhiteSpace(_options.Device) ? "GPU" : _options.Device);

        var speechOptions = new SpeechToTextOptions
        {
            ModelId = settings.Model,
        };

        if (string.Equals(settings.LanguageMode, "Fixed", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(settings.LanguageCode))
        {
            speechOptions.SpeechLanguage = settings.LanguageCode;
        }

        SpeechToTextResponse speechResponse;
        await using var audioStream = File.OpenRead(audioPath);
        speechResponse = await _speechToTextClient.GetTextAsync(audioStream, speechOptions, ct);

        // RawRepresentation is OpenAiVerboseTranscriptionResponse (from /v1/audio/transcriptions verbose_json)
        if (speechResponse.RawRepresentation is not OpenAiVerboseTranscriptionResponse raw)
            throw new InvalidOperationException(
                "OpenVINO sidecar speech client returned an unexpected response type.");

        TranscriptSegmentDto[] segments;
        if (raw.Segments is { Length: > 0 } rawSegments)
        {
            segments = rawSegments
                .Select(s => new TranscriptSegmentDto
                {
                    StartMs = (long)(s.Start * 1000),
                    EndMs = (long)(s.End * 1000),
                    Text = s.Text,
                })
                .ToArray();
        }
        else
        {
            segments = string.IsNullOrWhiteSpace(speechResponse.Text)
                ? []
                : [new TranscriptSegmentDto { StartMs = 0, EndMs = (long)(raw.Duration * 1000), Text = speechResponse.Text }];
        }

        _logger.LogInformation(
            "{Engine} transcription completed: {SegmentCount} segments, language={Language}",
            EngineId,
            segments.Length,
            raw.Language ?? "unknown");

        return new TranscriptionResult(
            speechResponse.Text,
            segments,
            raw.Language,
            (long)(raw.Duration * 1000));
    }
}
