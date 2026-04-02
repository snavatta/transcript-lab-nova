using ClassTranscriber.Api.Contracts;

namespace ClassTranscriber.Api.Media;

public record MediaInfo(long DurationMs, MediaType MediaType, string ContentType);

public interface IMediaInspector
{
    Task<MediaInfo?> InspectAsync(string filePath, CancellationToken ct = default);
}

public interface IAudioExtractor
{
    Task<string> ExtractAudioAsync(string inputPath, string outputPath, CancellationToken ct = default);
}

public interface IAudioNormalizer
{
    Task<string> NormalizeAsync(string inputPath, string outputPath, CancellationToken ct = default);
}
