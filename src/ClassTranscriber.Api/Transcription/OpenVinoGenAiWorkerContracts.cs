using ClassTranscriber.Api.Contracts;

namespace ClassTranscriber.Api.Transcription;

public sealed record OpenVinoGenAiWorkerRequest
{
    public required string AudioPath { get; init; }
    public required string Model { get; init; }
    public required string ModelPath { get; init; }
    public required string Device { get; init; }
    public required string LanguageMode { get; init; }
    public string? LanguageCode { get; init; }
    public required bool LogSegments { get; init; }
}

public sealed record OpenVinoGenAiWorkerResponse
{
    public required string PlainText { get; init; }
    public required TranscriptSegmentDto[] Segments { get; init; }
    public string? DetectedLanguage { get; init; }
    public long? DurationMs { get; init; }
}
