using ClassTranscriber.Api.Transcription;
using ClassTranscriber.Api.Media;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ClassTranscriber.Api.Tests;

public sealed class HostedAudioPreparationServiceTests
{
    [Fact]
    public async Task LongFormGeometry_UsesExactCoresAndClampedTwoSecondExtraction()
    {
        var tempRoot = CreateTempRoot();
        var encoder = new RecordingHostedFlacEncoder(_ => 1024);
        var service = new HostedAudioPreparationService(encoder, tempRoot, NullLogger<HostedAudioPreparationService>.Instance);

        await using (var result = await service.PrepareChunksAsync("lecture.wav", 1_201_000))
        {
            result.Chunks.Select(chunk => (chunk.CoreStartMs, chunk.CoreEndMs))
                .Should().Equal((0L, 600_000L), (600_000L, 1_200_000L), (1_200_000L, 1_201_000L));
            result.Chunks.Select(chunk => (chunk.ExtractionStartMs, chunk.ExtractionEndMs))
                .Should().Equal((0L, 602_000L), (598_000L, 1_201_000L), (1_198_000L, 1_201_000L));
            result.Chunks.Should().OnlyContain(chunk => chunk.EncodedLengthBytes < HostedAudioPreparationService.MaximumEncodedPartBytes);
            result.Chunks[1].OwnsTimestamp(600_000).Should().BeTrue();
            result.Chunks[0].OwnsTimestamp(600_000).Should().BeFalse();
            result.Chunks[^1].IsFinal.Should().BeTrue();
            result.Chunks[^1].OwnsTimestamp(1_201_000).Should().BeTrue();
            result.Chunks.Take(result.Chunks.Count - 1).Should().OnlyContain(chunk =>
                !chunk.IsFinal && !chunk.OwnsTimestamp(chunk.CoreEndMs));
            Directory.Exists(result.TemporaryDirectory).Should().BeTrue();
        }

        Directory.EnumerateFileSystemEntries(tempRoot).Should().BeEmpty();
        Directory.Delete(tempRoot);
    }

    [Fact]
    public async Task OversizedPart_IsRecursivelyBisectedUntilEveryPartIsStrictlyBelowLimit()
    {
        var tempRoot = CreateTempRoot();
        var encoder = new RecordingHostedFlacEncoder(interval =>
            interval.CoreEndMs - interval.CoreStartMs > 150_000
                ? HostedAudioPreparationService.MaximumEncodedPartBytes
                : HostedAudioPreparationService.MaximumEncodedPartBytes - 1);
        var service = new HostedAudioPreparationService(encoder, tempRoot, NullLogger<HostedAudioPreparationService>.Instance);

        await using (var result = await service.PrepareChunksAsync("lecture.wav", 600_000))
        {
            result.Chunks.Should().HaveCount(4);
            result.Chunks.Select(chunk => (chunk.CoreStartMs, chunk.CoreEndMs)).Should().Equal(
                (0L, 150_000L),
                (150_000L, 300_000L),
                (300_000L, 450_000L),
                (450_000L, 600_000L));
            result.Chunks.Should().OnlyContain(chunk => chunk.EncodedLengthBytes < HostedAudioPreparationService.MaximumEncodedPartBytes);
        }

        Directory.EnumerateFileSystemEntries(tempRoot).Should().BeEmpty();
        Directory.Delete(tempRoot);
    }

    [Fact]
    public async Task MinimumCoreStillOversized_FailsWithSanitizedMessageAndCleansArtifacts()
    {
        var tempRoot = CreateTempRoot();
        var encoder = new RecordingHostedFlacEncoder(_ => HostedAudioPreparationService.MaximumEncodedPartBytes);
        var service = new HostedAudioPreparationService(encoder, tempRoot, NullLogger<HostedAudioPreparationService>.Instance);

        var action = () => service.PrepareChunksAsync("/private/input/secret-lecture.wav", 60_000);

        var exception = await action.Should().ThrowAsync<HostedAudioPreparationException>();
        exception.Which.Message.Should().Be("Hosted audio exceeds the provider upload limit at the minimum chunk duration.");
        exception.Which.Message.Should().NotContain("secret-lecture").And.NotContain(tempRoot);
        Directory.EnumerateFileSystemEntries(tempRoot).Should().BeEmpty();
        Directory.Delete(tempRoot);
    }

    [Theory]
    [InlineData(61_000)]
    [InlineData(90_000)]
    [InlineData(119_999)]
    public async Task OversizedCoreBelowTwoMinutes_TriesExactMinimumBeforeTerminalFailure(long durationMs)
    {
        var tempRoot = CreateTempRoot();
        var encoder = new RecordingHostedFlacEncoder(_ => HostedAudioPreparationService.MaximumEncodedPartBytes);
        var service = new HostedAudioPreparationService(encoder, tempRoot, NullLogger<HostedAudioPreparationService>.Instance);

        await FluentActions.Invoking(() => service.PrepareChunksAsync("lecture.wav", durationMs))
            .Should().ThrowAsync<HostedAudioPreparationException>()
            .WithMessage("Hosted audio exceeds the provider upload limit at the minimum chunk duration.");

        encoder.EncodedIntervals.Select(interval =>
                (interval.CoreStartMs, interval.CoreEndMs, interval.ExtractionStartMs, interval.ExtractionEndMs, interval.IsFinal))
            .Should().Equal(
                (0L, durationMs, 0L, durationMs, true),
                (0L, 60_000L, 0L, Math.Min(durationMs, 62_000L), false));
        encoder.EncodedIntervals[1].OwnsTimestamp(60_000).Should().BeFalse();
        Directory.EnumerateFileSystemEntries(tempRoot).Should().BeEmpty();
        Directory.Delete(tempRoot);
    }

    [Theory]
    [InlineData(61_000)]
    [InlineData(90_000)]
    [InlineData(119_999)]
    public async Task OversizedCoreBelowTwoMinutes_WhenMinimumFits_UsesLegalFinalRemainder(long durationMs)
    {
        var tempRoot = CreateTempRoot();
        var encoder = new RecordingHostedFlacEncoder(interval =>
            interval.CoreStartMs == 0 && interval.CoreEndMs == durationMs
                ? HostedAudioPreparationService.MaximumEncodedPartBytes
                : HostedAudioPreparationService.MaximumEncodedPartBytes - 1);
        var service = new HostedAudioPreparationService(encoder, tempRoot, NullLogger<HostedAudioPreparationService>.Instance);

        await using (var result = await service.PrepareChunksAsync("lecture.wav", durationMs))
        {
            result.Chunks.Select(chunk =>
                    (chunk.CoreStartMs, chunk.CoreEndMs, chunk.ExtractionStartMs, chunk.ExtractionEndMs, chunk.IsFinal))
                .Should().Equal(
                    (0L, 60_000L, 0L, Math.Min(durationMs, 62_000L), false),
                    (60_000L, durationMs, 58_000L, durationMs, true));
            result.Chunks[0].OwnsTimestamp(60_000).Should().BeFalse();
            result.Chunks[1].OwnsTimestamp(60_000).Should().BeTrue();
            result.Chunks[1].OwnsTimestamp(durationMs).Should().BeTrue();
            result.Chunks.Should().OnlyContain(chunk =>
                chunk.EncodedLengthBytes < HostedAudioPreparationService.MaximumEncodedPartBytes);
        }

        Directory.EnumerateFileSystemEntries(tempRoot).Should().BeEmpty();
        Directory.Delete(tempRoot);
    }

    [Fact]
    public async Task OversizedCoreAtTwoMinimums_SplitsIntoTwoSixtySecondCores()
    {
        var tempRoot = CreateTempRoot();
        var encoder = new RecordingHostedFlacEncoder(interval =>
            interval.CoreEndMs - interval.CoreStartMs > HostedAudioPreparationService.MinimumCoreDurationMs
                ? HostedAudioPreparationService.MaximumEncodedPartBytes
                : HostedAudioPreparationService.MaximumEncodedPartBytes - 1);
        var service = new HostedAudioPreparationService(encoder, tempRoot, NullLogger<HostedAudioPreparationService>.Instance);

        await using (var result = await service.PrepareChunksAsync("lecture.wav", 120_000))
        {
            result.Chunks.Select(chunk => chunk.CoreEndMs - chunk.CoreStartMs)
                .Should().Equal(60_000, 60_000);
            result.Chunks.Should().OnlyContain(chunk =>
                chunk.CoreEndMs - chunk.CoreStartMs >= HostedAudioPreparationService.MinimumCoreDurationMs);
            result.Chunks[0].IsFinal.Should().BeFalse();
            result.Chunks[1].IsFinal.Should().BeTrue();
            result.Chunks[1].OwnsTimestamp(120_000).Should().BeTrue();
        }

        Directory.EnumerateFileSystemEntries(tempRoot).Should().BeEmpty();
        Directory.Delete(tempRoot);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task MalformedDuration_IsRejectedBeforeCreatingArtifacts(long durationMs)
    {
        var tempRoot = CreateTempRoot();
        var service = new HostedAudioPreparationService(
            new RecordingHostedFlacEncoder(_ => 1024),
            tempRoot,
            NullLogger<HostedAudioPreparationService>.Instance);

        await FluentActions.Invoking(() => service.PrepareChunksAsync("lecture.wav", durationMs))
            .Should().ThrowAsync<ArgumentOutOfRangeException>();

        Directory.EnumerateFileSystemEntries(tempRoot).Should().BeEmpty();
        Directory.Delete(tempRoot);
    }

    [Theory]
    [InlineData(-1, 1, 0, 1)]
    [InlineData(10, 10, 8, 12)]
    [InlineData(10, 20, 11, 20)]
    [InlineData(10, 20, 0, 19)]
    public void MalformedInterval_IsRejected(
        long coreStartMs,
        long coreEndMs,
        long extractionStartMs,
        long extractionEndMs)
    {
        var action = () => new HostedAudioInterval(
            coreStartMs,
            coreEndMs,
            extractionStartMs,
            extractionEndMs);

        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task CancellationCleansArtifacts()
    {
        var tempRoot = CreateTempRoot();
        using var cts = new CancellationTokenSource();
        var encoder = new RecordingHostedFlacEncoder(_ => 1024, (_, token) =>
        {
            cts.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        });
        var service = new HostedAudioPreparationService(encoder, tempRoot, NullLogger<HostedAudioPreparationService>.Instance);

        await FluentActions.Invoking(() => service.PrepareChunksAsync("lecture.wav", 600_000, cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();

        Directory.EnumerateFileSystemEntries(tempRoot).Should().BeEmpty();
        Directory.Delete(tempRoot);
    }

    [Fact]
    public async Task WholeFlac_IsLosslessAndOwnedByDisposablePreparation()
    {
        var tempRoot = CreateTempRoot();
        var encoder = new RecordingHostedFlacEncoder(_ => 4096);
        var service = new HostedAudioPreparationService(encoder, tempRoot, NullLogger<HostedAudioPreparationService>.Instance);

        string temporaryDirectory;
        await using (var result = await service.PrepareWholeFlacAsync("lecture.wav"))
        {
            temporaryDirectory = result.TemporaryDirectory;
            result.FilePath.Should().EndWith(".flac");
            encoder.WholeFileCalls.Should().Be(1);
            File.Exists(result.FilePath).Should().BeTrue();
        }

        Directory.Exists(temporaryDirectory).Should().BeFalse();
        Directory.EnumerateFileSystemEntries(tempRoot).Should().BeEmpty();
        Directory.Delete(tempRoot);
    }

    [Fact]
    public async Task EncoderFailure_CleansOnlyTaskOwnedArtifacts()
    {
        var tempRoot = CreateTempRoot();
        var staleDirectory = Path.Combine(tempRoot, "stale-unrelated");
        Directory.CreateDirectory(staleDirectory);
        var encoder = new ThrowingHostedFlacEncoder();
        var service = new HostedAudioPreparationService(encoder, tempRoot, NullLogger<HostedAudioPreparationService>.Instance);

        var exception = await FluentActions.Invoking(() =>
                service.PrepareChunksAsync("/private/input/secret-lecture.wav", 600_000))
            .Should().ThrowAsync<HostedAudioPreparationException>();

        exception.Which.Message.Should().Be("Hosted audio preparation failed.");
        exception.Which.Message.Should().NotContain("secret-lecture").And.NotContain(tempRoot);
        Directory.GetDirectories(tempRoot).Should().Equal(staleDirectory);
        Directory.Delete(staleDirectory);
        Directory.Delete(tempRoot);
    }

    [Fact]
    public async Task Cleanup_IsIdempotent()
    {
        var tempRoot = CreateTempRoot();
        var service = new HostedAudioPreparationService(
            new RecordingHostedFlacEncoder(_ => 1024),
            tempRoot,
            NullLogger<HostedAudioPreparationService>.Instance);
        var result = await service.PrepareWholeFlacAsync("lecture.wav");

        await result.DisposeAsync();
        await result.DisposeAsync();

        Directory.EnumerateFileSystemEntries(tempRoot).Should().BeEmpty();
        Directory.Delete(tempRoot);
    }

    [Fact]
    public async Task FfmpegEncoder_ProducesLosslessFlacWhileLocalExtractorStillProducesWav()
    {
        var tempRoot = CreateTempRoot();
        var inputPath = Path.Combine(tempRoot, "fixture.wav");
        WritePcmWav(inputPath, durationMs: 250);
        var ffmpegOptions = Options.Create(new FfmpegOptions { FFmpegPath = "ffmpeg" });
        var hostedService = new HostedAudioPreparationService(
            new FfmpegHostedFlacEncoder(ffmpegOptions, NullLogger<FfmpegHostedFlacEncoder>.Instance),
            tempRoot,
            NullLogger<HostedAudioPreparationService>.Instance);

        await using (var prepared = await hostedService.PrepareWholeFlacAsync(inputPath))
        {
            var magic = new byte[4];
            await using var stream = File.OpenRead(prepared.FilePath);
            (await stream.ReadAsync(magic)).Should().Be(4);
            magic.Should().Equal((byte)'f', (byte)'L', (byte)'a', (byte)'C');
        }

        var localExtractor = new FfmpegAudioExtractor(
            ffmpegOptions,
            NullLogger<FfmpegAudioExtractor>.Instance);
        var localPath = await localExtractor.ExtractAudioAsync(inputPath, Path.Combine(tempRoot, "local-prepared"));
        localPath.Should().EndWith(".wav");
        (await File.ReadAllBytesAsync(localPath)).Take(4).Should().Equal((byte)'R', (byte)'I', (byte)'F', (byte)'F');

        File.Delete(localPath);
        File.Delete(inputPath);
        Directory.EnumerateFileSystemEntries(tempRoot).Should().BeEmpty();
        Directory.Delete(tempRoot);
    }

    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hosted-audio-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WritePcmWav(string path, int durationMs)
    {
        const int sampleRate = 16_000;
        const short channels = 1;
        const short bitsPerSample = 16;
        var sampleCount = sampleRate * durationMs / 1000;
        var dataLength = sampleCount * sizeof(short);

        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(36 + dataLength);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * bitsPerSample / 8);
        writer.Write((short)(channels * bitsPerSample / 8));
        writer.Write(bitsPerSample);
        writer.Write("data"u8);
        writer.Write(dataLength);
        for (var index = 0; index < sampleCount; index++)
        {
            var sample = (short)(Math.Sin(2 * Math.PI * 440 * index / sampleRate) * short.MaxValue / 4);
            writer.Write(sample);
        }
    }

    private sealed class RecordingHostedFlacEncoder(
        Func<HostedAudioInterval, long> encodedLength,
        Func<HostedAudioInterval, CancellationToken, Task>? beforeEncode = null) : IHostedFlacEncoder
    {
        public int WholeFileCalls { get; private set; }
        public List<HostedAudioInterval> EncodedIntervals { get; } = [];

        public async Task EncodeWholeAsync(string inputPath, string outputPath, CancellationToken ct)
        {
            WholeFileCalls++;
            var interval = new HostedAudioInterval(0, 1, 0, 1, isFinal: true);
            if (beforeEncode is not null)
                await beforeEncode(interval, ct);
            await WriteLengthAsync(outputPath, encodedLength(interval), ct);
        }

        public async Task EncodeIntervalAsync(
            string inputPath,
            string outputPath,
            HostedAudioInterval interval,
            CancellationToken ct)
        {
            EncodedIntervals.Add(interval);
            if (beforeEncode is not null)
                await beforeEncode(interval, ct);
            await WriteLengthAsync(outputPath, encodedLength(interval), ct);
        }

        private static async Task WriteLengthAsync(string path, long length, CancellationToken ct)
        {
            await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, useAsync: true);
            stream.SetLength(length);
            await stream.FlushAsync(ct);
        }
    }

    private sealed class ThrowingHostedFlacEncoder : IHostedFlacEncoder
    {
        public Task EncodeWholeAsync(string inputPath, string outputPath, CancellationToken ct) =>
            throw new InvalidOperationException($"unsafe {inputPath} {outputPath}");

        public Task EncodeIntervalAsync(
            string inputPath,
            string outputPath,
            HostedAudioInterval interval,
            CancellationToken ct)
        {
            File.WriteAllText(outputPath, "partial");
            throw new InvalidOperationException($"unsafe {inputPath} {outputPath}");
        }
    }
}
