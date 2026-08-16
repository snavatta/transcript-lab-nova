using System.Buffers.Binary;
using ClassTranscriber.Api.Transcription;

namespace ClassTranscriber.Api.Tests;

public static class HostedLongFormTestFixtures
{
    public sealed record Interval(
        long CoreStartMs,
        long CoreEndMs,
        long ExtractionStartMs,
        long ExtractionEndMs,
        bool IsFinal);

    public sealed record GeometryCase(
        string Name,
        long DurationMs,
        IReadOnlyList<Interval> ExpectedIntervals);

    public sealed record AdaptiveSplitCase(long DurationMs, long SubThresholdBytes);

    public sealed record Checkpoint(
        string Text,
        int RequestCount,
        long SttCostMicroUsd,
        IReadOnlyList<TranscriptionWord> Words);

    public static IReadOnlyList<GeometryCase> GeometryCases { get; } =
    [
        new(
            "short",
            42_000,
            [new Interval(0, 42_000, 0, 42_000, true)]),
        new(
            "boundary-600",
            600_000,
            [new Interval(0, 600_000, 0, 600_000, true)]),
        new(
            "long-1201",
            1_201_000,
            [
                new Interval(0, 600_000, 0, 602_000, false),
                new Interval(600_000, 1_200_000, 598_000, 1_201_000, false),
                new Interval(1_200_000, 1_201_000, 1_198_000, 1_201_000, true),
            ]),
    ];

    public static AdaptiveSplitCase AdaptiveSplit { get; } = new(600_000, 23_999_999);

    public static IReadOnlyList<Checkpoint> HybridCheckpoints { get; } =
    [
        new("first", 1, 100_000, [new TranscriptionWord("first", 0, 900)]),
        new(
            "first second",
            2,
            300_000,
            [
                new TranscriptionWord("first", 0, 900),
                new TranscriptionWord(" second", 1_100, 2_000),
            ]),
    ];

    public static IReadOnlyList<XaiSpeakerInterval> HybridSpeakerIntervals { get; } =
    [
        new("native-9", 0, 1_000),
        new("native-4", 1_000, 2_000),
    ];

    public const long HybridDiarizationCostMicroUsd = 700_000;
    public const long HybridDiarizationRateMicroUsdPerHour = 1_400_000;
    public const int LongFormRequestCount = 3;
    public const long LongFormSttCostMicroUsd = 600_000;
    public const string FatalProviderDetail = "provider secret=do-not-persist";
    public const string SanitizedFatalMessage = "External xAI diarization failed.";

    public static byte[] CreateTinyWave()
    {
        const int sampleRate = 8_000;
        const short bitsPerSample = 16;
        const short channelCount = 1;
        const int sampleCount = 8;
        var dataLength = sampleCount * (bitsPerSample / 8) * channelCount;
        var bytes = new byte[44 + dataLength];
        "RIFF"u8.CopyTo(bytes);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), bytes.Length - 8);
        "WAVEfmt "u8.CopyTo(bytes.AsSpan(8));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 16);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(20), 1);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(22), channelCount);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(24), sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(28), sampleRate * channelCount * bitsPerSample / 8);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(32), (short)(channelCount * bitsPerSample / 8));
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(34), bitsPerSample);
        "data"u8.CopyTo(bytes.AsSpan(36));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(40), dataLength);
        return bytes;
    }
}
