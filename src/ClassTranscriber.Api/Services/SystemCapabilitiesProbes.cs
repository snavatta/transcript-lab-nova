using System.Diagnostics;
using System.Runtime.InteropServices;
using ClassTranscriber.Api.Transcription;
using Microsoft.Extensions.Options;

namespace ClassTranscriber.Api.Services;

public sealed class HostedProviderCapabilitiesProbe : IHostedProviderCapabilitiesProbe
{
    private readonly OpenRouterOptions _openRouter;
    private readonly XaiOptions _xai;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TimeSpan _probeTimeout;

    public HostedProviderCapabilitiesProbe(
        IOptions<OpenRouterOptions> openRouter,
        IOptions<XaiOptions> xai,
        IHttpClientFactory httpClientFactory,
        TimeSpan? probeTimeout = null)
    {
        _openRouter = openRouter.Value;
        _xai = xai.Value;
        _httpClientFactory = httpClientFactory;
        _probeTimeout = probeTimeout is { } configuredTimeout && configuredTimeout > TimeSpan.Zero
            ? configuredTimeout
            : TimeSpan.FromSeconds(3);
    }

    public bool IsConfigured(string provider) => provider switch
    {
        "OpenRouter" => IsHttpsConfigurationValid(_openRouter.BaseUrl, _openRouter.ApiKey),
        "xAI" => IsHttpsConfigurationValid(_xai.BaseUrl, _xai.ApiKey),
        _ => false,
    };

    public async Task<bool> IsReachableAsync(string provider, CancellationToken ct)
    {
        if (!IsConfigured(provider))
            return false;

        var (clientName, relativePath) = provider switch
        {
            "OpenRouter" => (OpenRouterTranscriptionEngine.HttpClientName, "models?output_modalities=transcription"),
            "xAI" => (XaiTranscriptionEngine.HttpClientName, "models"),
            _ => throw new ArgumentOutOfRangeException(nameof(provider)),
        };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_probeTimeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, relativePath);
            using var response = await _httpClientFactory.CreateClient(clientName)
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            return response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return false;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool IsHttpsConfigurationValid(string? baseUrl, string? apiKey) =>
        !string.IsNullOrWhiteSpace(apiKey)
        && Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
        && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
}

public sealed class RuntimeCapabilitiesProbe : IRuntimeCapabilitiesProbe
{
    private readonly ICudaEnvironmentProbe _cudaProbe;
    private readonly OpenVinoWhisperSidecarOptions _openVinoOptions;
    private readonly TimeSpan _openVinoProbeTimeout;
    private readonly TimeSpan _openVinoTerminationTimeout;

    public RuntimeCapabilitiesProbe(
        ICudaEnvironmentProbe cudaProbe,
        IOptions<OpenVinoWhisperSidecarOptions> openVinoOptions,
        TimeSpan? openVinoProbeTimeout = null,
        TimeSpan? openVinoTerminationTimeout = null)
    {
        _cudaProbe = cudaProbe;
        _openVinoOptions = openVinoOptions.Value;
        _openVinoProbeTimeout = openVinoProbeTimeout is { } configuredTimeout && configuredTimeout > TimeSpan.Zero
            ? configuredTimeout
            : TimeSpan.FromSeconds(2);
        _openVinoTerminationTimeout = openVinoTerminationTimeout is { } configuredTerminationTimeout
            && configuredTerminationTimeout > TimeSpan.Zero
                ? configuredTerminationTimeout
                : TimeSpan.FromSeconds(1);
    }

    public RuntimeCapabilitiesSnapshot Capture()
    {
        var hardwareName = GetCpuHardwareName();
        var cudaAvailable = ProbeCudaRuntime();
        var coreMlAvailable = OperatingSystem.IsMacOS()
            && RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
        var openVinoAvailable = ProbeOpenVinoRuntime();

        return new RuntimeCapabilitiesSnapshot(
            RuntimeInformation.ProcessArchitecture.ToString(),
            Environment.ProcessorCount,
            RuntimeInformation.OSDescription,
            hardwareName,
            [
                new RuntimeComputeBackend("CPU", true, hardwareName is null ? [] : [hardwareName]),
                new RuntimeComputeBackend("CUDA", cudaAvailable, cudaAvailable ? ["CUDA device"] : []),
                new RuntimeComputeBackend("CoreML", coreMlAvailable, coreMlAvailable ? ["Apple Silicon"] : []),
                new RuntimeComputeBackend("OpenVINO", openVinoAvailable, openVinoAvailable ? ["OpenVINO device"] : []),
            ]);
    }

    private bool ProbeCudaRuntime()
    {
        try
        {
            return _cudaProbe.GetAvailabilityError() is null;
        }
        catch
        {
            return false;
        }
    }

    private bool ProbeOpenVinoRuntime()
    {
        if (string.IsNullOrWhiteSpace(_openVinoOptions.PythonPath)
            || !ProcessPathResolver.ExecutableExists(_openVinoOptions.PythonPath))
        {
            return false;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = _openVinoOptions.PythonPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("import openvino_genai");

        try
        {
            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
                return false;
            if (!process.WaitForExit((int)_openVinoProbeTimeout.TotalMilliseconds))
            {
                process.Kill();
                _ = process.WaitForExit((int)_openVinoTerminationTimeout.TotalMilliseconds);
                return false;
            }
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static string? GetCpuHardwareName()
    {
        if (!OperatingSystem.IsLinux())
            return null;

        try
        {
            foreach (var line in File.ReadLines("/proc/cpuinfo"))
            {
                var separator = line.IndexOf(':');
                if (separator < 0)
                    continue;
                var key = line[..separator].Trim();
                if (!string.Equals(key, "model name", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(key, "hardware", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var value = line[(separator + 1)..].Trim();
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return null;
    }
}
