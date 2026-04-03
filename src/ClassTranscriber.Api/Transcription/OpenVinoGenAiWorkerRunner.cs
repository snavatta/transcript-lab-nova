using System.Diagnostics;
using System.Text.Json;

namespace ClassTranscriber.Api.Transcription;

public interface IOpenVinoGenAiWorkerRunner
{
    Task<OpenVinoGenAiWorkerResponse> RunAsync(
        OpenVinoGenAiWorkerRequest request,
        string pythonPath,
        string workerScriptPath,
        CancellationToken ct = default);
}

public sealed class OpenVinoGenAiWorkerRunner : IOpenVinoGenAiWorkerRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string ResponseBeginMarker = "__TRANSCRIPTLAB_OPENVINO_GENAI_RESPONSE_BEGIN__";
    private const string ResponseEndMarker = "__TRANSCRIPTLAB_OPENVINO_GENAI_RESPONSE_END__";
    private readonly ILogger<OpenVinoGenAiWorkerRunner> _logger;

    public OpenVinoGenAiWorkerRunner(ILogger<OpenVinoGenAiWorkerRunner> logger)
    {
        _logger = logger;
    }

    public async Task<OpenVinoGenAiWorkerResponse> RunAsync(
        OpenVinoGenAiWorkerRequest request,
        string pythonPath,
        string workerScriptPath,
        CancellationToken ct = default)
    {
        var startInfo = CreateProcessStartInfo(pythonPath, workerScriptPath);

        using var process = new Process
        {
            StartInfo = startInfo,
        };

        if (!process.Start())
            throw new InvalidOperationException("Failed to start the OpenVINO GenAI worker process.");

        using var cancellationRegistration = ct.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best effort only.
            }
        });

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrLines = new List<string>();
        var stderrTask = ConsumeStandardErrorAsync(process.StandardError, stderrLines, ct);

        await JsonSerializer.SerializeAsync(process.StandardInput.BaseStream, request, JsonOptions, ct);
        await process.StandardInput.FlushAsync(ct);
        process.StandardInput.Close();

        await process.WaitForExitAsync(ct);

        var stdout = await stdoutTask;
        await stderrTask;
        var stderr = string.Join(Environment.NewLine, stderrLines);

        if (process.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            throw new InvalidOperationException($"OpenVINO GenAI worker failed with exit code {process.ExitCode}: {detail}".Trim());
        }

        if (string.IsNullOrWhiteSpace(stdout))
            throw new InvalidOperationException("OpenVINO GenAI worker returned no output.");

        var responsePayload = ExtractResponsePayload(stdout);
        var response = JsonSerializer.Deserialize<OpenVinoGenAiWorkerResponse>(responsePayload, JsonOptions);
        if (response is null)
            throw new InvalidOperationException("Failed to parse OpenVINO GenAI worker output.");

        return response;
    }

    private async Task ConsumeStandardErrorAsync(StreamReader stderr, List<string> capturedLines, CancellationToken ct)
    {
        while (true)
        {
            var line = await stderr.ReadLineAsync(ct);
            if (line is null)
                break;

            capturedLines.Add(line);
            if (!string.IsNullOrWhiteSpace(line))
                _logger.LogInformation("OpenVINO GenAI worker: {Message}", line);
        }
    }

    private static ProcessStartInfo CreateProcessStartInfo(string pythonPath, string workerScriptPath)
    {
        var resolvedPythonPath = ResolveExecutableOrPath(pythonPath);
        var resolvedWorkerScriptPath = ProcessPathResolver.ResolveFilePath(workerScriptPath);

        var startInfo = new ProcessStartInfo
        {
            FileName = resolvedPythonPath,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        startInfo.ArgumentList.Add("-u");
        startInfo.ArgumentList.Add(resolvedWorkerScriptPath);
        startInfo.Environment["PYTHONUNBUFFERED"] = "1";
        return startInfo;
    }

    private static string ResolveExecutableOrPath(string executableOrPath)
        => Path.IsPathRooted(executableOrPath)
            || executableOrPath.Contains(Path.DirectorySeparatorChar)
            || executableOrPath.Contains(Path.AltDirectorySeparatorChar)
            ? ProcessPathResolver.ResolveFilePath(executableOrPath)
            : executableOrPath;

    private static string ExtractResponsePayload(string stdout)
    {
        var trimmed = stdout.Trim();
        var startIndex = trimmed.IndexOf(ResponseBeginMarker, StringComparison.Ordinal);
        if (startIndex < 0)
            return trimmed;

        startIndex += ResponseBeginMarker.Length;
        var endIndex = trimmed.IndexOf(ResponseEndMarker, startIndex, StringComparison.Ordinal);
        if (endIndex < 0)
            throw new InvalidOperationException("OpenVINO GenAI worker output ended before the response framing marker.");

        var payload = trimmed[startIndex..endIndex].Trim();
        if (string.IsNullOrWhiteSpace(payload))
            throw new InvalidOperationException("OpenVINO GenAI worker returned an empty framed response.");

        return payload;
    }
}
