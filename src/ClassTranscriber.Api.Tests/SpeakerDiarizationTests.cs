using System.Text;
using ClassTranscriber.Api.Transcription;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClassTranscriber.Api.Tests;

public sealed class SpeakerDiarizationTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    [Fact]
    public void AssignSpeakers_SeparatesDistinctSyntheticVoices()
    {
        var audioPath = CreateSyntheticWaveFile(
            16_000,
            [
                (180d, 0.55d, 1_400),
                (0d, 0d, 350),
                (285d, 0.45d, 1_350),
                (0d, 0d, 350),
                (180d, 0.52d, 1_450),
                (0d, 0d, 350),
                (285d, 0.48d, 1_300),
            ]);

        var diarizer = new BasicSpeakerDiarizer(NullLogger<BasicSpeakerDiarizer>.Instance);
        var labeled = diarizer.AssignSpeakers(audioPath, new List<ClassTranscriber.Api.Contracts.TranscriptSegmentDto>
        {
            new() { StartMs = 0, EndMs = 1400, Text = "teacher segment", Speaker = null },
            new() { StartMs = 1750, EndMs = 3100, Text = "student segment", Speaker = null },
            new() { StartMs = 3450, EndMs = 4900, Text = "teacher follow up", Speaker = null },
            new() { StartMs = 5250, EndMs = 6550, Text = "student response", Speaker = null },
        });

        labeled.Select(segment => segment.Speaker).Should().OnlyContain(speaker => !string.IsNullOrWhiteSpace(speaker));
        labeled.Select(segment => segment.Speaker).Distinct().Should().HaveCountGreaterThan(1);
    }

    [Fact]
    public void AssignSpeakers_UsesSingleLabel_ForUniformSyntheticVoice()
    {
        var audioPath = CreateSyntheticWaveFile(
            16_000,
            [
                (190d, 0.5d, 1_100),
                (0d, 0d, 350),
                (190d, 0.52d, 1_050),
                (0d, 0d, 350),
                (190d, 0.48d, 1_200),
            ]);

        var diarizer = new BasicSpeakerDiarizer(NullLogger<BasicSpeakerDiarizer>.Instance);
        var labeled = diarizer.AssignSpeakers(audioPath, new List<ClassTranscriber.Api.Contracts.TranscriptSegmentDto>
        {
            new() { StartMs = 0, EndMs = 1100, Text = "first", Speaker = null },
            new() { StartMs = 1450, EndMs = 2500, Text = "second", Speaker = null },
            new() { StartMs = 2850, EndMs = 4050, Text = "third", Speaker = null },
        });

        labeled.Select(segment => segment.Speaker).Distinct().Should().Equal("Speaker 1");
    }

    public void Dispose()
    {
        foreach (var tempFile in _tempFiles.Where(File.Exists))
            File.Delete(tempFile);
    }

    private string CreateSyntheticWaveFile(int sampleRate, IReadOnlyList<(double FrequencyHz, double Amplitude, int DurationMs)> segments)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"transcriptlab-diarization-{Guid.NewGuid():N}.wav");
        _tempFiles.Add(tempPath);

        var samples = new List<short>();
        foreach (var segment in segments)
        {
            var sampleCount = (int)Math.Round(sampleRate * (segment.DurationMs / 1000d));
            for (var sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
            {
                var value = segment.FrequencyHz <= 0
                    ? 0d
                    : Math.Sin((2d * Math.PI * segment.FrequencyHz * sampleIndex) / sampleRate) * segment.Amplitude;
                samples.Add((short)Math.Clamp(value * short.MaxValue, short.MinValue, short.MaxValue));
            }
        }

        using var stream = File.Create(tempPath);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: false);
        var dataSize = samples.Count * sizeof(short);

        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataSize);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * sizeof(short));
        writer.Write((short)sizeof(short));
        writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataSize);
        foreach (var sample in samples)
            writer.Write(sample);

        return tempPath;
    }
}
