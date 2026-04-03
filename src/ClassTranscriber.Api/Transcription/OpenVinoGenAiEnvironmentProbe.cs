using System.Diagnostics;

namespace ClassTranscriber.Api.Transcription;

public interface IOpenVinoGenAiEnvironmentProbe
{
    string? GetAvailabilityError();
}

public sealed class OpenVinoGenAiEnvironmentProbe : IOpenVinoGenAiEnvironmentProbe
{
    private const string RuntimeProbeScript = """
        import sys
        import openvino as ov
        import openvino_tokenizers
        import openvino_genai

        device = (sys.argv[1] if len(sys.argv) > 1 else "GPU").strip() or "GPU"
        core = ov.Core()
        available = [str(entry) for entry in list(getattr(core, "available_devices", []) or [])]
        normalized = device.upper()

        if normalized == "AUTO":
            raise SystemExit(0)

        if normalized == "GPU":
            gpu_devices = [entry for entry in available if entry.upper() == "GPU" or entry.upper().startswith("GPU.")]
            if not gpu_devices:
                raise RuntimeError(f"Requested device '{device}' is not available. availableDevices={available}")

            indexed_gpu_devices = [entry for entry in gpu_devices if "." in entry]
            resolved = indexed_gpu_devices[0] if indexed_gpu_devices else gpu_devices[0]
        else:
            resolved = device
            if ":" not in resolved and "," not in resolved and not any(entry.upper() == resolved.upper() for entry in available):
                raise RuntimeError(f"Requested device '{device}' is not available. availableDevices={available}")

        if ":" not in resolved and "," not in resolved:
            core.get_property(resolved, "FULL_DEVICE_NAME")
        """;

    private readonly OpenVinoGenAiOptions _options;
    private readonly object _sync = new();
    private string? _cachedError;
    private DateTime _lastProbeUtc;
    private bool _hasCachedResult;

    public OpenVinoGenAiEnvironmentProbe(Microsoft.Extensions.Options.IOptions<OpenVinoGenAiOptions> options)
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
            return "OpenVinoGenAi is unavailable because Transcription:OpenVinoGenAi:PythonPath is not configured.";

        if (!ProcessPathResolver.ExecutableExists(_options.PythonPath))
            return $"OpenVinoGenAi is unavailable because the Python executable '{_options.PythonPath}' could not be found.";

        if (string.IsNullOrWhiteSpace(_options.WorkerScriptPath))
            return "OpenVinoGenAi is unavailable because Transcription:OpenVinoGenAi:WorkerScriptPath is not configured.";

        if (!File.Exists(ProcessPathResolver.ResolveFilePath(_options.WorkerScriptPath)))
            return $"OpenVinoGenAi is unavailable because the worker script was not found at {ProcessPathResolver.ResolveFilePath(_options.WorkerScriptPath)}.";

        var startInfo = new ProcessStartInfo
        {
            FileName = ResolveExecutableOrPath(_options.PythonPath),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(RuntimeProbeScript);
        startInfo.ArgumentList.Add(string.IsNullOrWhiteSpace(_options.Device) ? "GPU" : _options.Device);

        using var process = new Process
        {
            StartInfo = startInfo,
        };

        if (!process.Start())
            return "OpenVinoGenAi is unavailable because the Python runtime could not be started.";

        if (!process.WaitForExit(10000))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best effort only.
            }

            return "OpenVinoGenAi is unavailable because the Python OpenVINO GenAI runtime probe timed out.";
        }

        var stdout = process.StandardOutput.ReadToEnd().Trim();
        var stderr = process.StandardError.ReadToEnd().Trim();

        if (process.ExitCode == 0)
            return null;

        var failureMessage = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
        return string.IsNullOrWhiteSpace(failureMessage)
            ? "OpenVinoGenAi is unavailable because the Python OpenVINO GenAI runtime probe failed."
            : $"OpenVinoGenAi is unavailable because the Python OpenVINO GenAI runtime probe failed: {failureMessage}";
    }

    private static string ResolveExecutableOrPath(string executableOrPath)
        => Path.IsPathRooted(executableOrPath)
            || executableOrPath.Contains(Path.DirectorySeparatorChar)
            || executableOrPath.Contains(Path.AltDirectorySeparatorChar)
            ? ProcessPathResolver.ResolveFilePath(executableOrPath)
            : executableOrPath;
}
