using ClassTranscriber.Api.Transcription;
using FluentAssertions;

namespace ClassTranscriber.Api.Tests;

public sealed class XaiSpeakerTimingMergeTests
{
    [Fact]
    public void XaiTimingMerge_PreservesOpenRouterWords_AndUsesDeterministicSpeakerRules()
    {
        var words = new[]
        {
            new TranscriptionWord(" Hello", 0, 1_000),
            new TranscriptionWord(",", 1_000, 1_100),
            new TranscriptionWord(" exact", 2_000, 2_100),
            new TranscriptionWord(" far", 4_101, 4_200),
            new TranscriptionWord(" tie", 5_301, 6_301),
            new TranscriptionWord(" again!", 6_301, 6_801),
        };
        var intervals = new[]
        {
            new XaiSpeakerInterval("provider-8", 0, 600),
            new XaiSpeakerInterval("provider-2", 600, 1_100),
            new XaiSpeakerInterval("provider-8", 1_100, 1_999),
            new XaiSpeakerInterval("provider-7", 3_000, 3_100),
            new XaiSpeakerInterval("provider-2", 5_301, 5_801),
            new XaiSpeakerInterval("provider-8", 5_801, 6_801),
        };
        var source = new TranscriptionResult(
            " Hello, exact far tie again!",
            [],
            "en",
            6_801)
        {
            Words = words,
        };

        var merged = XaiSpeakerTimingMerge.Apply(source, intervals);

        merged.PlainText.Should().Be(source.PlainText);
        merged.Words.Should().Equal(words);
        merged.Segments.Select(segment => (segment.Text, segment.Speaker, segment.StartMs, segment.EndMs))
            .Should().Equal(
                (" Hello", "Speaker 1", 0L, 1_000L),
                (",", "Speaker 2", 1_000L, 1_100L),
                (" exact", "Speaker 1", 2_000L, 2_100L),
                (" far", (string?)null, 4_101L, 4_200L),
                (" tie", "Speaker 2", 5_301L, 6_301L),
                (" again!", "Speaker 1", 6_301L, 6_801L));
        string.Concat(merged.Segments.Select(segment => segment.Text)).Should().Be(source.PlainText);
    }

    [Theory]
    [InlineData(999, "Speaker 1")]
    [InlineData(1000, "Speaker 1")]
    [InlineData(1001, null)]
    public void NearestTolerance_IncludesExactlyOneThousandMilliseconds(int gapMs, string? expectedSpeaker)
    {
        var source = new TranscriptionResult("word", [], null, 4_000)
        {
            Words = [new TranscriptionWord("word", 2_000 + gapMs, 2_100 + gapMs)],
        };

        var merged = XaiSpeakerTimingMerge.Apply(
            source,
            [new XaiSpeakerInterval("speaker", 1_000, 2_000)]);

        merged.Segments.Should().ContainSingle();
        merged.Segments[0].Speaker.Should().Be(expectedSpeaker);
    }

    [Fact]
    public void ConsecutiveEqualSpeakers_AreGroupedWithoutChangingTokensOrTimestamps()
    {
        var words = new[]
        {
            new TranscriptionWord("¿Qué", 10, 20),
            new TranscriptionWord(" tal", 20, 30),
            new TranscriptionWord("?", 30, 40),
        };
        var source = new TranscriptionResult("¿Qué tal?", [], "es", 40) { Words = words };

        var merged = XaiSpeakerTimingMerge.Apply(
            source,
            [new XaiSpeakerInterval("original-42", 0, 100)]);

        merged.Segments.Should().ContainSingle();
        merged.Segments[0].Should().BeEquivalentTo(new
        {
            StartMs = 10L,
            EndMs = 40L,
            Text = "¿Qué tal?",
            Speaker = "Speaker 1",
        });
        merged.Words.Should().Equal(words);
        merged.PlainText.Should().Be("¿Qué tal?");
    }
}
