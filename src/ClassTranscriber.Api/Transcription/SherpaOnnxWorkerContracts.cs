using ClassTranscriber.Api.Contracts;

namespace ClassTranscriber.Api.Transcription;

public sealed record SherpaOnnxWorkerRequest
{
    public required string AudioPath { get; init; }
    public required SherpaOnnxBackend Backend { get; init; }
    public required string TokensPath { get; init; }
    public string? ModelPath { get; init; }
    public string? EncoderPath { get; init; }
    public string? DecoderPath { get; init; }
    public required bool UseInverseTextNormalization { get; init; }
    public required string Task { get; init; }
    public required string Provider { get; init; }
    public required int NumThreads { get; init; }
    public required string LanguageMode { get; init; }
    public string? LanguageCode { get; init; }
    public required bool LogSegments { get; init; }
}

public sealed record SherpaOnnxWorkerResponse
{
    public required string PlainText { get; init; }
    public required TranscriptSegmentDto[] Segments { get; init; }
    public string? DetectedLanguage { get; init; }
    public long? DurationMs { get; init; }
}
