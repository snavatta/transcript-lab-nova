using ClassTranscriber.Api.Transcription;
using FluentAssertions;

namespace ClassTranscriber.Api.Tests;

public sealed class HostedLongFormIntegrationTests : OpenRouterTestFixture
{
    [Fact]
    public async Task GeneratedGeometry_PreparesExpectedCoreAndExtractionIntervals()
    {
        foreach (var scenario in HostedLongFormTestFixtures.GeometryCases)
        {
            var audioPath = Path.Combine(CreateTempDirectory(), $"{scenario.Name}.wav");
            await File.WriteAllBytesAsync(audioPath, HostedLongFormTestFixtures.CreateTinyWave());
            var encoder = new IntervalLengthEncoder(_ => 1);
            var preparation = CreatePreparationService(encoder: encoder);

            await using var chunks = await preparation.PrepareChunksAsync(audioPath, scenario.DurationMs);

            chunks.Chunks.Select(chunk => new HostedLongFormTestFixtures.Interval(
                    chunk.CoreStartMs,
                    chunk.CoreEndMs,
                    chunk.ExtractionStartMs,
                    chunk.ExtractionEndMs,
                    chunk.IsFinal))
                .Should().Equal(scenario.ExpectedIntervals, scenario.Name);
            chunks.Chunks.Should().OnlyContain(chunk => chunk.EncodedLengthBytes > 0
                && chunk.EncodedLengthBytes < HostedAudioPreparationService.MaximumEncodedPartBytes);
        }
    }

    [Fact]
    public async Task GeneratedAdaptiveSplit_PreparesStrictSubThresholdPartsInOrder()
    {
        var scenario = HostedLongFormTestFixtures.AdaptiveSplit;
        var audioPath = Path.Combine(CreateTempDirectory(), "adaptive.wav");
        await File.WriteAllBytesAsync(audioPath, HostedLongFormTestFixtures.CreateTinyWave());
        var encoder = new IntervalLengthEncoder(interval =>
            interval.CoreEndMs - interval.CoreStartMs == scenario.DurationMs
                ? HostedAudioPreparationService.MaximumEncodedPartBytes
                : scenario.SubThresholdBytes);
        var preparation = CreatePreparationService(encoder: encoder);

        await using var chunks = await preparation.PrepareChunksAsync(audioPath, scenario.DurationMs);

        chunks.Chunks.Select(chunk => (chunk.CoreStartMs, chunk.CoreEndMs)).Should().Equal(
            (0L, 300_000L),
            (300_000L, 600_000L));
        chunks.Chunks.Should().OnlyContain(chunk => chunk.EncodedLengthBytes == scenario.SubThresholdBytes);
        encoder.Intervals.Select(interval => (interval.CoreStartMs, interval.CoreEndMs)).Should().Equal(
            (0L, 600_000L),
            (0L, 300_000L),
            (300_000L, 600_000L));
    }
}
