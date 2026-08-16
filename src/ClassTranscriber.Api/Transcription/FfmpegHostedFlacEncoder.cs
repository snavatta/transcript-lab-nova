using System.Diagnostics;
using System.Globalization;
using ClassTranscriber.Api.Media;
using Microsoft.Extensions.Options;

namespace ClassTranscriber.Api.Transcription;

public sealed class FfmpegHostedFlacEncoder : IHostedFlacEncoder
{
    private readonly string _ffmpegPath;
    private readonly ILogger<FfmpegHostedFlacEncoder> _logger;

    public FfmpegHostedFlacEncoder(IOptions<FfmpegOptions> options, ILogger<FfmpegHostedFlacEncoder> logger)
    {
        _ffmpegPath = options.Value.FFmpegPath;
        _logger = logger;
    }

    public Task EncodeWholeAsync(string inputPath, string outputPath, CancellationToken ct) =>
        EncodeAsync(inputPath, outputPath, interval: null, ct);

    public Task EncodeIntervalAsync(string inputPath, string outputPath, HostedAudioInterval interval, CancellationToken ct) =>
        EncodeAsync(inputPath, outputPath, interval, ct);

    private async Task EncodeAsync(string inputPath, string outputPath, HostedAudioInterval? interval, CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-nostdin");
        startInfo.ArgumentList.Add("-y");
        if (interval is not null)
        {
            startInfo.ArgumentList.Add("-ss");
            startInfo.ArgumentList.Add(ToSeconds(interval.ExtractionStartMs));
        }
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(inputPath);
        if (interval is not null)
        {
            startInfo.ArgumentList.Add("-t");
            startInfo.ArgumentList.Add(ToSeconds(interval.ExtractionEndMs - interval.ExtractionStartMs));
        }
        startInfo.ArgumentList.Add("-vn");
        startInfo.ArgumentList.Add("-map_metadata");
        startInfo.ArgumentList.Add("-1");
        startInfo.ArgumentList.Add("-c:a");
        startInfo.ArgumentList.Add("flac");
        startInfo.ArgumentList.Add(outputPath);

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
                throw new InvalidOperationException("FFmpeg could not be started.");

            using var cancellationRegistration = ct.Register(static state =>
            {
                var child = (Process)state!;
                try
                {
                    if (!child.HasExited)
                        child.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                }
            }, process);

            var stderrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(ct);
            _ = await stderrTask;
            ct.ThrowIfCancellationRequested();
            if (process.ExitCode != 0)
            {
                _logger.LogWarning("FFmpeg hosted FLAC preparation failed with exit code {ExitCode}.", process.ExitCode);
                throw new InvalidOperationException("FFmpeg hosted FLAC preparation failed.");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning("FFmpeg hosted FLAC preparation could not complete.");
            throw new InvalidOperationException("FFmpeg hosted FLAC preparation failed.", exception);
        }
    }

    private static string ToSeconds(long milliseconds) =>
        (milliseconds / 1000d).ToString("0.###", CultureInfo.InvariantCulture);
}
