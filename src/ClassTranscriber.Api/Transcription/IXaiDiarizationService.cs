namespace ClassTranscriber.Api.Transcription;

public sealed record XaiDiarizationResult(
    IReadOnlyList<XaiSpeakerInterval> Intervals,
    string Model,
    int RequestCount,
    long? CostMicroUsd,
    long? RateMicroUsdPerHour,
    string CostClassification);

public interface IXaiDiarizationService
{
    Task<XaiDiarizationResult> DiarizeAsync(
        string audioPath,
        long? durationMs,
        CancellationToken ct = default);
}
