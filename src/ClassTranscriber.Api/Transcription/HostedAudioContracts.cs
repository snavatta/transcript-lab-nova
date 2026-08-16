namespace ClassTranscriber.Api.Transcription;

public interface IHostedAudioPreparationService
{
    Task<HostedAudioFile> PrepareWholeFlacAsync(string inputPath, CancellationToken ct = default);
    Task<HostedAudioChunkSet> PrepareChunksAsync(string inputPath, long durationMs, CancellationToken ct = default);
}

public interface IHostedFlacEncoder
{
    Task EncodeWholeAsync(string inputPath, string outputPath, CancellationToken ct);
    Task EncodeIntervalAsync(string inputPath, string outputPath, HostedAudioInterval interval, CancellationToken ct);
}

public sealed record HostedAudioInterval
{
    public HostedAudioInterval(long coreStartMs, long coreEndMs, long extractionStartMs, long extractionEndMs, bool isFinal = false)
    {
        if (coreStartMs < 0)
            throw new ArgumentOutOfRangeException(nameof(coreStartMs));
        if (coreEndMs <= coreStartMs)
            throw new ArgumentOutOfRangeException(nameof(coreEndMs));
        if (extractionStartMs < 0 || extractionStartMs > coreStartMs)
            throw new ArgumentOutOfRangeException(nameof(extractionStartMs));
        if (extractionEndMs < coreEndMs)
            throw new ArgumentOutOfRangeException(nameof(extractionEndMs));

        CoreStartMs = coreStartMs;
        CoreEndMs = coreEndMs;
        ExtractionStartMs = extractionStartMs;
        ExtractionEndMs = extractionEndMs;
        IsFinal = isFinal;
    }

    public long CoreStartMs { get; }
    public long CoreEndMs { get; }
    public long ExtractionStartMs { get; }
    public long ExtractionEndMs { get; }
    public bool IsFinal { get; }
    public bool OwnsTimestamp(long timestampMs) =>
        timestampMs >= CoreStartMs && (timestampMs < CoreEndMs || (IsFinal && timestampMs == CoreEndMs));
}

public sealed record HostedAudioChunk(
    string FilePath,
    long EncodedLengthBytes,
    long CoreStartMs,
    long CoreEndMs,
    long ExtractionStartMs,
    long ExtractionEndMs,
    bool IsFinal)
{
    public bool OwnsTimestamp(long timestampMs) =>
        timestampMs >= CoreStartMs && (timestampMs < CoreEndMs || (IsFinal && timestampMs == CoreEndMs));
}

public sealed class HostedAudioFile : IAsyncDisposable
{
    private readonly TemporaryArtifactOwner _owner;
    internal HostedAudioFile(string filePath, TemporaryArtifactOwner owner) => (FilePath, _owner) = (filePath, owner);
    public string FilePath { get; }
    public string TemporaryDirectory => _owner.DirectoryPath;
    public ValueTask DisposeAsync() => _owner.DisposeAsync();
}

public sealed class HostedAudioChunkSet : IAsyncDisposable
{
    private readonly TemporaryArtifactOwner _owner;
    internal HostedAudioChunkSet(IReadOnlyList<HostedAudioChunk> chunks, TemporaryArtifactOwner owner) =>
        (Chunks, _owner) = (chunks, owner);
    public IReadOnlyList<HostedAudioChunk> Chunks { get; }
    public string TemporaryDirectory => _owner.DirectoryPath;
    public ValueTask DisposeAsync() => _owner.DisposeAsync();
}

public sealed class HostedAudioPreparationException : InvalidOperationException
{
    public HostedAudioPreparationException(string message) : base(message) { }
    internal HostedAudioPreparationException(string message, Exception innerException) : base(message, innerException) { }
}

internal sealed class TemporaryArtifactOwner(string directoryPath) : IAsyncDisposable
{
    private int _disposed;
    public string DirectoryPath { get; } = directoryPath;

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return ValueTask.CompletedTask;
        try
        {
            if (Directory.Exists(DirectoryPath))
                Directory.Delete(DirectoryPath, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
        return ValueTask.CompletedTask;
    }
}
