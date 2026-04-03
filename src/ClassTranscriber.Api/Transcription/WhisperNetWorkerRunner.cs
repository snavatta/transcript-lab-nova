using System.Diagnostics;
using System.Text.Json;

namespace ClassTranscriber.Api.Transcription;

public interface IWhisperNetWorkerRunner
{
    Task<WhisperNetWorkerResponse> RunAsync(WhisperNetWorkerRequest request, string workerPath, string? dotNetHostPath, CancellationToken ct = default);
}

public sealed class WhisperNetWorkerRunner : IWhisperNetWorkerRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string ResponseBeginMarker = "__TRANSCRIPTLAB_WHISPERNET_RESPONSE_BEGIN__";
    private const string ResponseEndMarker = "__TRANSCRIPTLAB_WHISPERNET_RESPONSE_END__";
    private readonly ILogger<WhisperNetWorkerRunner> _logger;

    public WhisperNetWorkerRunner(ILogger<WhisperNetWorkerRunner> logger)
    {
        _logger = logger;
    }

    public async Task<WhisperNetWorkerResponse> RunAsync(
        WhisperNetWorkerRequest request,
        string workerPath,
        string? dotNetHostPath,
        CancellationToken ct = default)
    {
        var startInfo = CreateProcessStartInfo(workerPath, dotNetHostPath);

        using var process = new Process
        {
            StartInfo = startInfo,
        };

        if (!process.Start())
            throw new InvalidOperationException("Failed to start the Whisper.net worker process.");

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
        await process.StandardInput.FlushAsync();
        process.StandardInput.Close();

        await process.WaitForExitAsync(ct);

        var stdout = await stdoutTask;
        await stderrTask;
        var stderr = string.Join(Environment.NewLine, stderrLines);

        if (process.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            throw new InvalidOperationException($"Whisper.net worker failed with exit code {process.ExitCode}: {detail}".Trim());
        }

        if (string.IsNullOrWhiteSpace(stdout))
            throw new InvalidOperationException("Whisper.net worker returned no output.");

        var responsePayload = ExtractResponsePayload(stdout);
        var response = JsonSerializer.Deserialize<WhisperNetWorkerResponse>(responsePayload, JsonOptions);
        if (response is null)
            throw new InvalidOperationException("Failed to parse Whisper.net worker output.");

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
                _logger.LogInformation("Whisper.net worker: {Message}", line);
        }
    }

    private static ProcessStartInfo CreateProcessStartInfo(string workerPath, string? dotNetHostPath)
    {
        var resolvedWorkerPath = ProcessPathResolver.ResolveWorkerPath(workerPath);
        var useDotNetHost = string.Equals(Path.GetExtension(resolvedWorkerPath), ".dll", StringComparison.OrdinalIgnoreCase);
        var fileName = useDotNetHost
            ? dotNetHostPath ?? "dotnet"
            : resolvedWorkerPath;
        var arguments = useDotNetHost
            ? $"\"{resolvedWorkerPath}\""
            : string.Empty;

        return new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
    }

    private static string ExtractResponsePayload(string stdout)
    {
        var trimmed = stdout.Trim();
        var startIndex = trimmed.IndexOf(ResponseBeginMarker, StringComparison.Ordinal);
        if (startIndex < 0)
            return trimmed;

        startIndex += ResponseBeginMarker.Length;
        var endIndex = trimmed.IndexOf(ResponseEndMarker, startIndex, StringComparison.Ordinal);
        if (endIndex < 0)
            throw new InvalidOperationException("Whisper.net worker output ended before the response framing marker.");

        var payload = trimmed[startIndex..endIndex].Trim();
        if (string.IsNullOrWhiteSpace(payload))
            throw new InvalidOperationException("Whisper.net worker returned an empty framed response.");

        return payload;
    }
}
