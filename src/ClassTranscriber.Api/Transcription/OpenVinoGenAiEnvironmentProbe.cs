using System.Diagnostics;

namespace ClassTranscriber.Api.Transcription;

public interface IOpenVinoGenAiEnvironmentProbe
{
    string? GetAvailabilityError();
}

public sealed class OpenVinoGenAiEnvironmentProbe : IOpenVinoGenAiEnvironmentProbe
{
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
        startInfo.ArgumentList.Add("import openvino, openvino_tokenizers, openvino_genai");

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

        if (process.ExitCode == 0)
            return null;

        var stderr = process.StandardError.ReadToEnd().Trim();
        return string.IsNullOrWhiteSpace(stderr)
            ? "OpenVinoGenAi is unavailable because the Python OpenVINO GenAI runtime probe failed."
            : $"OpenVinoGenAi is unavailable because the Python OpenVINO GenAI runtime probe failed: {stderr}";
    }

    private static string ResolveExecutableOrPath(string executableOrPath)
        => Path.IsPathRooted(executableOrPath)
            || executableOrPath.Contains(Path.DirectorySeparatorChar)
            || executableOrPath.Contains(Path.AltDirectorySeparatorChar)
            ? ProcessPathResolver.ResolveFilePath(executableOrPath)
            : executableOrPath;
}
