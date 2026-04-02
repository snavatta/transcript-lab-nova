using ClassTranscriber.Api.Contracts;

namespace ClassTranscriber.Api.Transcription;

public enum WhisperNetWorkerMode
{
    Cpu,
    Cuda,
    OpenVino,
}

public sealed record WhisperNetWorkerRequest
{
    public required WhisperNetWorkerMode Mode { get; init; }
    public required string AudioPath { get; init; }
    public required string Model { get; init; }
    public required string LanguageMode { get; init; }
    public string? LanguageCode { get; init; }
    public required string ModelsPath { get; init; }
    public required bool AutoDownloadModels { get; init; }
    public required string OpenVinoDevice { get; init; }
    public string? OpenVinoCachePath { get; init; }
}

public sealed record WhisperNetWorkerResponse
{
    public required string PlainText { get; init; }
    public required TranscriptSegmentDto[] Segments { get; init; }
    public string? DetectedLanguage { get; init; }
    public long? DurationMs { get; init; }
}
