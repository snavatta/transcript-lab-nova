using FluentAssertions;

namespace ClassTranscriber.Api.Tests;

public class SherpaOnnxWorkerTests
{
    [Fact]
    public void WhisperChunkProcessor_SplitsLongWaveIntoSubThirtySecondChunks()
    {
        var sampleRate = 16_000;
        var durationMs = 65_000;
        var sampleCount = sampleRate * durationMs / 1000;
        var wave = new WaveFileData(new float[sampleCount], sampleRate, durationMs);

        var chunks = SherpaOnnxWhisperChunkProcessor.CreateChunks(wave);

        chunks.Should().HaveCount(3);
        chunks[0].StartOffsetMs.Should().Be(0);
        chunks[0].DurationMs.Should().Be(SherpaOnnxWhisperChunkProcessor.MaxChunkDurationMs);
        chunks[1].StartOffsetMs.Should().Be(SherpaOnnxWhisperChunkProcessor.MaxChunkDurationMs);
        chunks[1].DurationMs.Should().Be(SherpaOnnxWhisperChunkProcessor.MaxChunkDurationMs);
        chunks[2].StartOffsetMs.Should().Be(56_000);
        chunks[2].DurationMs.Should().Be(9_000);
    }

    [Fact]
    public void SegmentBuilder_OffsetsFallbackSegmentByChunkStart()
    {
        var segments = SherpaOnnxSegmentBuilder.Build(
            "chunk text",
            timestamps: [],
            durationMs: 9_000,
            startOffsetMs: 56_000);

        segments.Should().ContainSingle();
        segments[0].StartMs.Should().Be(56_000);
        segments[0].EndMs.Should().Be(65_000);
        segments[0].Text.Should().Be("chunk text");
    }
}
