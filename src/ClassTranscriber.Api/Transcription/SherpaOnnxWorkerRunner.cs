using System.Diagnostics;
using System.Text.Json;

namespace ClassTranscriber.Api.Transcription;

public interface ISherpaOnnxWorkerRunner
{
    Task<SherpaOnnxWorkerResponse> RunAsync(SherpaOnnxWorkerRequest request, string workerPath, string? dotNetHostPath, CancellationToken ct = default);
}

public sealed class SherpaOnnxWorkerRunner : ISherpaOnnxWorkerRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ILogger<SherpaOnnxWorkerRunner> _logger;

    public SherpaOnnxWorkerRunner(ILogger<SherpaOnnxWorkerRunner> logger)
    {
        _logger = logger;
    }

    public async Task<SherpaOnnxWorkerResponse> RunAsync(
        SherpaOnnxWorkerRequest request,
        string workerPath,
        string? dotNetHostPath,
        CancellationToken ct = default)
    {
        var startInfo = CreateProcessStartInfo(workerPath, dotNetHostPath);
        _logger.LogInformation(
            "Launching SherpaOnnx worker. backend={Backend}, provider={Provider}, audioPath={AudioPath}, modelPath={ModelPath}, encoderPath={EncoderPath}, decoderPath={DecoderPath}",
            request.Backend,
            request.Provider,
            request.AudioPath,
            request.ModelPath ?? "<none>",
            request.EncoderPath ?? "<none>",
            request.DecoderPath ?? "<none>");

        using var process = new Process
        {
            StartInfo = startInfo,
        };

        if (!process.Start())
            throw new InvalidOperationException("Failed to start the SherpaOnnx worker process.");

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
            throw new InvalidOperationException($"SherpaOnnx worker failed with exit code {process.ExitCode}: {detail}".Trim());
        }

        if (string.IsNullOrWhiteSpace(stdout))
            throw new InvalidOperationException("SherpaOnnx worker returned no output.");

        var response = JsonSerializer.Deserialize<SherpaOnnxWorkerResponse>(stdout, JsonOptions);
        if (response is null)
            throw new InvalidOperationException("Failed to parse SherpaOnnx worker output.");

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
                _logger.LogInformation("SherpaOnnx worker: {Message}", line);
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
}
