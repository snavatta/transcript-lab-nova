using System.Diagnostics;
using System.Text;
using ClassTranscriber.Api.Contracts;
using ClassTranscriber.Api.Domain;
using ClassTranscriber.Api.Transcription;
using Microsoft.Extensions.Options;

namespace ClassTranscriber.Api.Services;

public interface ITranscriptionModelManagerService
{
    Task<TranscriptionModelCatalogDto> GetCatalogAsync(CancellationToken ct);
    Task<TranscriptionModelEntryDto> ManageAsync(ManageTranscriptionModelRequest request, CancellationToken ct);
}

public sealed class TranscriptionModelManagerService : ITranscriptionModelManagerService
{
    private static readonly string[] GgmlModels = ["tiny", "base", "small", "medium", "large"];
    private static readonly string[] SherpaWhisperModels = ["small", "medium"];
    private static readonly string[] SherpaSenseVoiceModels = ["small"];
    private static readonly HashSet<string> LightweightProbeEngines = new(StringComparer.OrdinalIgnoreCase)
    {
        "WhisperNet",
        "WhisperNetCuda",
        "WhisperNetOpenVino",
    };
    private readonly Dictionary<string, IRegisteredTranscriptionEngine> _engines;
    private readonly WhisperNetOptions _whisperNetOptions;
    private readonly OpenVinoGenAiOptions _openVinoGenAiOptions;
    private readonly SherpaOnnxOptions _sherpaOnnxOptions;
    private readonly SherpaOnnxSenseVoiceOptions _sherpaOnnxSenseVoiceOptions;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TranscriptionModelManagerService> _logger;

    public TranscriptionModelManagerService(
        IEnumerable<IRegisteredTranscriptionEngine> engines,
        IOptions<WhisperNetOptions> whisperNetOptions,
        IOptions<OpenVinoGenAiOptions> openVinoGenAiOptions,
        IOptions<SherpaOnnxOptions> sherpaOnnxOptions,
        IOptions<SherpaOnnxSenseVoiceOptions> sherpaOnnxSenseVoiceOptions,
        IHttpClientFactory httpClientFactory,
        ILogger<TranscriptionModelManagerService> logger)
    {
        _engines = engines.ToDictionary(engine => engine.EngineId, StringComparer.OrdinalIgnoreCase);
        _whisperNetOptions = whisperNetOptions.Value;
        _openVinoGenAiOptions = openVinoGenAiOptions.Value;
        _sherpaOnnxOptions = sherpaOnnxOptions.Value;
        _sherpaOnnxSenseVoiceOptions = sherpaOnnxSenseVoiceOptions.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<TranscriptionModelCatalogDto> GetCatalogAsync(CancellationToken ct)
    {
        var models = new List<TranscriptionModelEntryDto>();
        foreach (var registration in GetKnownRegistrations())
            models.Add(await BuildEntryAsync(registration, includeProbe: registration.IsInstalled, ct));

        return new TranscriptionModelCatalogDto
        {
            Models = models
                .OrderBy(model => model.Engine, StringComparer.OrdinalIgnoreCase)
                .ThenBy(model => model.Model, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
        };
    }

    public async Task<TranscriptionModelEntryDto> ManageAsync(ManageTranscriptionModelRequest request, CancellationToken ct)
    {
        var engine = request.Engine?.Trim();
        var model = request.Model?.Trim();
        var action = request.Action?.Trim();

        if (string.IsNullOrWhiteSpace(engine) || string.IsNullOrWhiteSpace(model) || string.IsNullOrWhiteSpace(action))
            throw new ArgumentException("Engine, model, and action are required.");

        var registration = CreateRegistration(engine, model);

        switch (action.ToLowerInvariant())
        {
            case "download":
                if (!registration.CanDownload)
                    throw new InvalidOperationException($"Model download is not supported for {engine} / {model}.");
                if (registration.IsInstalled)
                    throw new InvalidOperationException($"Model {engine} / {model} is already installed. Use Redownload to refresh it.");

                await registration.DownloadAsync(ct);
                break;

            case "redownload":
                if (!registration.CanDownload)
                    throw new InvalidOperationException($"Model redownload is not supported for {engine} / {model}.");

                DeleteInstalledModel(registration);
                await registration.DownloadAsync(ct);
                break;

            case "probe":
                if (!registration.IsInstalled)
                    throw new InvalidOperationException($"Model {engine} / {model} is not installed on the filesystem.");
                break;

            default:
                throw new InvalidOperationException($"Unsupported model action '{action}'. Supported actions: Download, Redownload, Probe.");
        }

        return await BuildEntryAsync(CreateRegistration(engine, model), includeProbe: true, ct);
    }

    private async Task<TranscriptionModelEntryDto> BuildEntryAsync(
        ManagedModelRegistration registration,
        bool includeProbe,
        CancellationToken ct)
    {
        var probeState = registration.IsInstalled ? "Installed" : "Missing";
        var probeMessage = registration.IsInstalled
            ? "Model assets are present on the filesystem."
            : "Model assets are not present on the filesystem.";

        if (includeProbe && registration.IsInstalled)
        {
            if (!_engines.TryGetValue(registration.Engine, out var engine))
            {
                probeState = "Unsupported";
                probeMessage = $"Engine {registration.Engine} is not registered in this runtime.";
            }
            else if (engine.GetProbeError() is { } probeError)
            {
                probeState = "Unavailable";
                probeMessage = probeError;
            }
            else if (LightweightProbeEngines.Contains(registration.Engine))
            {
                probeState = "Ready";
                probeMessage = "Model assets are present and runtime preflight passed. Full inference probe is skipped for this engine.";
            }
            else
            {
                var probeResult = await ProbeInstalledModelAsync(registration, ct);
                probeState = probeResult.State;
                probeMessage = probeResult.Message;
            }
        }
        else if (!registration.IsInstalled && _engines.TryGetValue(registration.Engine, out var engine))
        {
            var availabilityError = engine.GetProbeError();
            if (availabilityError is not null)
            {
                probeState = "Unavailable";
                probeMessage = availabilityError;
            }
        }

        return new TranscriptionModelEntryDto
        {
            Engine = registration.Engine,
            Model = registration.Model,
            IsInstalled = registration.IsInstalled,
            InstallPath = registration.InstallPath,
            CanDownload = registration.CanDownload && !registration.IsInstalled,
            CanRedownload = registration.CanDownload && registration.IsInstalled,
            CanProbe = registration.IsInstalled,
            ProbeState = probeState,
            ProbeMessage = probeMessage,
        };
    }

    private async Task<ModelProbeResult> ProbeInstalledModelAsync(ManagedModelRegistration registration, CancellationToken ct)
    {
        if (!_engines.TryGetValue(registration.Engine, out var engine))
            return new ModelProbeResult("Unsupported", $"Engine {registration.Engine} is not registered in this runtime.");

        try
        {
            registration.ValidateInstalledAssets?.Invoke();
        }
        catch (Exception ex)
        {
            return new ModelProbeResult("Failed", ex.Message);
        }

        var probePath = Path.Combine(Path.GetTempPath(), $"transcriptlab-probe-{Guid.NewGuid():N}.wav");
        try
        {
            WriteSilenceWaveFile(probePath, durationMs: 800);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            var stopwatch = Stopwatch.StartNew();

            await engine.TranscribeAsync(
                probePath,
                new ProjectSettings
                {
                    Engine = registration.Engine,
                    Model = registration.Model,
                    LanguageMode = "Auto",
                    LanguageCode = null,
                    AudioNormalizationEnabled = false,
                    DiarizationEnabled = false,
                },
                timeout.Token);

            stopwatch.Stop();
            return new ModelProbeResult("Ready", $"Probe completed successfully in {stopwatch.Elapsed.TotalSeconds:F1}s.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new ModelProbeResult("Failed", "Probe timed out after 20 seconds.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Transcription model probe failed for {Engine}/{Model}", registration.Engine, registration.Model);
            return new ModelProbeResult("Failed", ex.Message);
        }
        finally
        {
            try
            {
                if (File.Exists(probePath))
                    File.Delete(probePath);
            }
            catch
            {
                // Best effort cleanup for probe file.
            }
        }
    }

    private IEnumerable<ManagedModelRegistration> GetKnownRegistrations()
    {
        if (_engines.ContainsKey("WhisperNet"))
        {
            foreach (var model in GgmlModels)
                yield return CreateRegistration("WhisperNet", model);
        }

        if (_engines.ContainsKey("WhisperNetCuda"))
        {
            foreach (var model in GgmlModels)
                yield return CreateRegistration("WhisperNetCuda", model);
        }

        if (_engines.ContainsKey("WhisperNetOpenVino"))
        {
            foreach (var model in GgmlModels)
                yield return CreateRegistration("WhisperNetOpenVino", model);
        }

        if (_engines.ContainsKey("OpenVinoGenAi"))
        {
            foreach (var model in OpenVinoGenAiModelCatalog.SupportedModels)
                yield return CreateRegistration("OpenVinoGenAi", model);
        }

        if (_engines.ContainsKey("SherpaOnnx"))
        {
            foreach (var model in SherpaWhisperModels)
                yield return CreateRegistration("SherpaOnnx", model);
        }

        if (_engines.ContainsKey("SherpaOnnxSenseVoice"))
        {
            foreach (var model in SherpaSenseVoiceModels)
                yield return CreateRegistration("SherpaOnnxSenseVoice", model);
        }
    }

    private ManagedModelRegistration CreateRegistration(string engine, string model)
        => engine switch
        {
            "WhisperNet" => CreateGgmlRegistration(engine, model, _whisperNetOptions.ModelsPath),
            "WhisperNetCuda" => CreateGgmlRegistration(engine, model, _whisperNetOptions.ModelsPath),
            "WhisperNetOpenVino" => CreateGgmlRegistration(engine, model, _whisperNetOptions.ModelsPath),
            "OpenVinoGenAi" => CreateOpenVinoGenAiRegistration(engine, model),
            "SherpaOnnx" => CreateSherpaRegistration(engine, model, _sherpaOnnxOptions.ModelsPath, _sherpaOnnxOptions.ModelDownloadBaseUrl, SherpaOnnxWhisperModelDownloadCatalog.TryGet),
            "SherpaOnnxSenseVoice" => CreateSherpaRegistration(engine, model, _sherpaOnnxSenseVoiceOptions.ModelsPath, _sherpaOnnxSenseVoiceOptions.ModelDownloadBaseUrl, SherpaOnnxSenseVoiceModelDownloadCatalog.TryGet),
            _ => throw new InvalidOperationException($"Unsupported managed engine '{engine}'."),
        };

    private ManagedModelRegistration CreateGgmlRegistration(string engine, string model, string modelsPath)
    {
        var installPath = GgmlModelDownloads.GetModelPath(modelsPath, model);
        return new ManagedModelRegistration(
            engine,
            model,
            installPath,
            File.Exists(installPath),
            true,
            DownloadAsync: downloadCt => GgmlModelDownloads.DownloadModelAsync(
                _httpClientFactory,
                _whisperNetOptions.ModelDownloadBaseUrl,
                model,
                installPath,
                _logger,
                downloadCt),
            ValidateInstalledAssets: null);
    }

    private ManagedModelRegistration CreateSherpaRegistration(
        string engine,
        string model,
        string modelsPath,
        string downloadBaseUrl,
        TryGetDefinition tryGetDefinition)
    {
        if (!tryGetDefinition(model, out var definition))
            throw new InvalidOperationException($"Unsupported managed model '{model}' for engine '{engine}'.");

        var installPath = SherpaOnnxModelDownloads.GetModelDirectory(modelsPath, model);
        return new ManagedModelRegistration(
            engine,
            model,
            installPath,
            Directory.Exists(installPath),
            true,
            DownloadAsync: downloadCt => SherpaOnnxModelDownloads.DownloadModelAsync(
                _httpClientFactory,
                downloadBaseUrl,
                engine,
                model,
                installPath,
                definition,
                _logger,
                downloadCt),
            ValidateInstalledAssets: () =>
            {
                var resolvedDefinition = SherpaOnnxModelResolver.Resolve(modelsPath, model);
                if (!definition.Matches(resolvedDefinition))
                    throw new InvalidOperationException($"{engine} model '{model}' is installed with unexpected assets for this engine.");
            });
    }

    private ManagedModelRegistration CreateOpenVinoGenAiRegistration(string engine, string model)
    {
        var definition = OpenVinoGenAiModelCatalog.GetRequired(model);
        var installPath = OpenVinoGenAiModelDownloads.GetModelDirectory(_openVinoGenAiOptions.ModelsPath, model);
        return new ManagedModelRegistration(
            engine,
            model,
            installPath,
            Directory.Exists(installPath),
            true,
            DownloadAsync: downloadCt => OpenVinoGenAiModelDownloads.DownloadModelAsync(
                _httpClientFactory,
                _openVinoGenAiOptions.ModelDownloadBaseUrl,
                definition,
                installPath,
                _logger,
                downloadCt),
            ValidateInstalledAssets: () => OpenVinoGenAiModelDownloads.ValidateInstalledModel(installPath, definition));
    }

    private static void DeleteInstalledModel(ManagedModelRegistration registration)
    {
        if (File.Exists(registration.InstallPath))
        {
            File.Delete(registration.InstallPath);
            return;
        }

        if (Directory.Exists(registration.InstallPath))
            Directory.Delete(registration.InstallPath, recursive: true);
    }

    private static void WriteSilenceWaveFile(string path, int durationMs)
    {
        const short channels = 1;
        const int sampleRate = 16000;
        const short bitsPerSample = 16;
        var bytesPerSample = bitsPerSample / 8;
        var sampleCount = (int)Math.Max(1, sampleRate * durationMs / 1000.0);
        var dataSize = sampleCount * channels * bytesPerSample;

        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: false);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataSize);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * bytesPerSample);
        writer.Write((short)(channels * bytesPerSample));
        writer.Write(bitsPerSample);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataSize);

        for (var i = 0; i < sampleCount; i++)
            writer.Write((short)0);
    }

    private delegate bool TryGetDefinition(string model, out SherpaOnnxDownloadDefinition definition);

    private sealed record ManagedModelRegistration(
        string Engine,
        string Model,
        string InstallPath,
        bool IsInstalled,
        bool CanDownload,
        Func<CancellationToken, Task> DownloadAsync,
        Action? ValidateInstalledAssets);

    private sealed record ModelProbeResult(string State, string Message);
}
