using ClassTranscriber.Api.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ClassTranscriber.Api.Transcription;

public sealed class HostedAudioPreparationService : IHostedAudioPreparationService
{
    public const long MaximumEncodedPartBytes = 24_000_000;
    public const long CoreDurationMs = 600_000;
    public const long ExtractionOverlapMs = 2_000;
    public const long MinimumCoreDurationMs = 60_000;

    private const string EncodingFailureMessage = "Hosted audio preparation failed.";
    private const string OversizedMinimumCoreMessage =
        "Hosted audio exceeds the provider upload limit at the minimum chunk duration.";

    private readonly IHostedFlacEncoder _encoder;
    private readonly string _tempRoot;
    private readonly ILogger<HostedAudioPreparationService> _logger;

    [ActivatorUtilitiesConstructor]
    public HostedAudioPreparationService(
        IHostedFlacEncoder encoder,
        IOptions<StorageOptions> storageOptions,
        ILogger<HostedAudioPreparationService> logger)
        : this(
            encoder,
            Path.Combine(storageOptions.Value.BasePath, storageOptions.Value.TempPath, "hosted-audio"),
            logger)
    {
    }

    public HostedAudioPreparationService(
        IHostedFlacEncoder encoder,
        string tempRoot,
        ILogger<HostedAudioPreparationService> logger)
    {
        _encoder = encoder;
        _tempRoot = Path.GetFullPath(tempRoot);
        _logger = logger;
    }

    public async Task<HostedAudioFile> PrepareWholeFlacAsync(string inputPath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ct.ThrowIfCancellationRequested();

        var owner = CreateArtifactOwner();
        try
        {
            var outputPath = Path.Combine(owner.DirectoryPath, "whole.flac");
            await _encoder.EncodeWholeAsync(inputPath, outputPath, ct);
            ValidateEncodedFile(outputPath);
            return new HostedAudioFile(outputPath, owner);
        }
        catch (OperationCanceledException)
        {
            await owner.DisposeAsync();
            throw;
        }
        catch (HostedAudioPreparationException)
        {
            await owner.DisposeAsync();
            throw;
        }
        catch (Exception exception) when (exception is not HostedAudioPreparationException)
        {
            await owner.DisposeAsync();
            _logger.LogWarning("Hosted whole-file FLAC preparation failed.");
            throw new HostedAudioPreparationException(EncodingFailureMessage, exception);
        }
    }

    public async Task<HostedAudioChunkSet> PrepareChunksAsync(
        string inputPath,
        long durationMs,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        if (durationMs <= 0)
            throw new ArgumentOutOfRangeException(nameof(durationMs), "Duration must be positive.");

        ct.ThrowIfCancellationRequested();
        var owner = CreateArtifactOwner();
        try
        {
            var chunks = new List<HostedAudioChunk>();
            for (long coreStartMs = 0; coreStartMs < durationMs;)
            {
                var coreEndMs = durationMs - coreStartMs <= CoreDurationMs
                    ? durationMs
                    : coreStartMs + CoreDurationMs;
                await EncodeWithinLimitAsync(
                    inputPath,
                    durationMs,
                    coreStartMs,
                    coreEndMs,
                    owner.DirectoryPath,
                    chunks,
                    ct);
                coreStartMs = coreEndMs;
            }

            return new HostedAudioChunkSet(chunks, owner);
        }
        catch (OperationCanceledException)
        {
            await owner.DisposeAsync();
            throw;
        }
        catch (HostedAudioPreparationException)
        {
            await owner.DisposeAsync();
            throw;
        }
        catch (Exception exception)
        {
            await owner.DisposeAsync();
            _logger.LogWarning("Hosted chunk FLAC preparation failed.");
            throw new HostedAudioPreparationException(EncodingFailureMessage, exception);
        }
    }

    private async Task EncodeWithinLimitAsync(
        string inputPath,
        long totalDurationMs,
        long coreStartMs,
        long coreEndMs,
        string temporaryDirectory,
        List<HostedAudioChunk> chunks,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (coreStartMs < 0 || coreEndMs <= coreStartMs || coreEndMs > totalDurationMs)
            throw new HostedAudioPreparationException("Hosted audio interval is invalid.");

        var extractionEndMs = totalDurationMs - coreEndMs <= ExtractionOverlapMs
            ? totalDurationMs
            : coreEndMs + ExtractionOverlapMs;
        var interval = new HostedAudioInterval(
            coreStartMs,
            coreEndMs,
            Math.Max(0, coreStartMs - ExtractionOverlapMs),
            extractionEndMs,
            isFinal: coreEndMs == totalDurationMs);
        var outputPath = Path.Combine(
            temporaryDirectory,
            $"part-{coreStartMs:D12}-{coreEndMs:D12}-{Guid.NewGuid():N}.flac");

        await _encoder.EncodeIntervalAsync(inputPath, outputPath, interval, ct);
        var encodedLength = ValidateEncodedFile(outputPath);
        if (encodedLength < MaximumEncodedPartBytes)
        {
            chunks.Add(new HostedAudioChunk(
                outputPath,
                encodedLength,
                interval.CoreStartMs,
                interval.CoreEndMs,
                interval.ExtractionStartMs,
                interval.ExtractionEndMs,
                interval.IsFinal));
            return;
        }

        File.Delete(outputPath);
        var coreDurationMs = coreEndMs - coreStartMs;
        if (coreDurationMs <= MinimumCoreDurationMs)
            throw new HostedAudioPreparationException(OversizedMinimumCoreMessage);

        var midpointMs = coreDurationMs < checked(MinimumCoreDurationMs * 2)
            ? coreStartMs + MinimumCoreDurationMs
            : coreStartMs + (coreDurationMs / 2);
        await EncodeWithinLimitAsync(
            inputPath,
            totalDurationMs,
            coreStartMs,
            midpointMs,
            temporaryDirectory,
            chunks,
            ct);
        await EncodeWithinLimitAsync(
            inputPath,
            totalDurationMs,
            midpointMs,
            coreEndMs,
            temporaryDirectory,
            chunks,
            ct);
    }

    private TemporaryArtifactOwner CreateArtifactOwner()
    {
        Directory.CreateDirectory(_tempRoot);
        var directoryPath = Path.Combine(_tempRoot, $"hosted-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directoryPath);
        return new TemporaryArtifactOwner(directoryPath);
    }

    private static long ValidateEncodedFile(string outputPath)
    {
        var file = new FileInfo(outputPath);
        if (!file.Exists || file.Length <= 0)
            throw new HostedAudioPreparationException(EncodingFailureMessage);
        return file.Length;
    }
}
