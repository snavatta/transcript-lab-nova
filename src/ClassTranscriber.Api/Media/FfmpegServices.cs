using System.Diagnostics;
using System.Globalization;
using ClassTranscriber.Api.Contracts;
using Microsoft.Extensions.Options;

namespace ClassTranscriber.Api.Media;

public class FfmpegOptions
{
    public string FFmpegPath { get; set; } = "ffmpeg";
}

public class FfmpegMediaInspector : IMediaInspector
{
    private readonly string _ffprobePath;
    private readonly ILogger<FfmpegMediaInspector> _logger;

    public FfmpegMediaInspector(IOptions<FfmpegOptions> options, ILogger<FfmpegMediaInspector> logger)
    {
        // Derive ffprobe path from ffmpeg path
        var ffmpegPath = options.Value.FFmpegPath;
        _ffprobePath = ffmpegPath.Replace("ffmpeg", "ffprobe", StringComparison.OrdinalIgnoreCase);
        _logger = logger;
    }

    public async Task<MediaInfo?> InspectAsync(string filePath, CancellationToken ct = default)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _ffprobePath,
                Arguments = $"-v error -show_entries format=duration -of csv=p=0 \"{filePath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);
            if (process is null) return null;

            var output = await process.StandardOutput.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);

            var durationMs = 0L;
            if (double.TryParse(output.Trim(), CultureInfo.InvariantCulture, out var seconds))
                durationMs = (long)(seconds * 1000);

            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            var mediaType = ext switch
            {
                ".mp4" or ".mkv" or ".mov" or ".webm" => MediaType.Video,
                ".mp3" or ".wav" or ".m4a" or ".flac" or ".ogg" => MediaType.Audio,
                _ => MediaType.Unknown,
            };

            var contentType = ext switch
            {
                ".mp4" => "video/mp4",
                ".mkv" => "video/x-matroska",
                ".mov" => "video/quicktime",
                ".webm" => "video/webm",
                ".mp3" => "audio/mpeg",
                ".wav" => "audio/wav",
                ".m4a" => "audio/mp4",
                ".flac" => "audio/flac",
                ".ogg" => "audio/ogg",
                _ => "application/octet-stream",
            };

            return new MediaInfo(durationMs, mediaType, contentType);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to inspect media at {FilePath}", filePath);
            return null;
        }
    }
}

public class FfmpegAudioExtractor : IAudioExtractor
{
    private readonly string _ffmpegPath;
    private readonly ILogger<FfmpegAudioExtractor> _logger;

    public FfmpegAudioExtractor(IOptions<FfmpegOptions> options, ILogger<FfmpegAudioExtractor> logger)
    {
        _ffmpegPath = options.Value.FFmpegPath;
        _logger = logger;
    }

    public async Task<string> ExtractAudioAsync(string inputPath, string outputPath, CancellationToken ct = default)
    {
        var wavOutput = Path.ChangeExtension(outputPath, ".wav");
        var psi = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            Arguments = $"-y -i \"{inputPath}\" -ar 16000 -ac 1 -c:a pcm_s16le \"{wavOutput}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi);
        if (process is null)
            throw new InvalidOperationException("Failed to start FFmpeg process.");

        var stderr = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            _logger.LogError("FFmpeg audio extraction failed: {Error}", stderr);
            throw new InvalidOperationException($"FFmpeg failed with exit code {process.ExitCode}");
        }

        _logger.LogInformation("Extracted audio from {Input} to {Output}", inputPath, wavOutput);
        return wavOutput;
    }
}

public class FfmpegAudioNormalizer : IAudioNormalizer
{
    private readonly string _ffmpegPath;
    private readonly ILogger<FfmpegAudioNormalizer> _logger;

    public FfmpegAudioNormalizer(IOptions<FfmpegOptions> options, ILogger<FfmpegAudioNormalizer> logger)
    {
        _ffmpegPath = options.Value.FFmpegPath;
        _logger = logger;
    }

    public async Task<string> NormalizeAsync(string inputPath, string outputPath, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            Arguments = $"-y -i \"{inputPath}\" -af loudnorm -ar 16000 -ac 1 -c:a pcm_s16le \"{outputPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi);
        if (process is null)
            throw new InvalidOperationException("Failed to start FFmpeg process.");

        var stderr = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            _logger.LogError("FFmpeg normalization failed: {Error}", stderr);
            throw new InvalidOperationException($"FFmpeg failed with exit code {process.ExitCode}");
        }

        _logger.LogInformation("Normalized audio from {Input} to {Output}", inputPath, outputPath);
        return outputPath;
    }
}
