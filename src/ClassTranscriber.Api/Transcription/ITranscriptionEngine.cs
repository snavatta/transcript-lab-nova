using ClassTranscriber.Api.Contracts;
using ClassTranscriber.Api.Domain;

namespace ClassTranscriber.Api.Transcription;

public record TranscriptionResult(
    string PlainText,
    TranscriptSegmentDto[] Segments,
    string? DetectedLanguage,
    long? DurationMs,
    TranscriptionProcessingMetadata? ProcessingMetadata = null)
{
    public IReadOnlyList<TranscriptionWord> Words { get; init; } = [];
}

public sealed record TranscriptionWord(string Text, long StartMs, long EndMs);

public sealed record TranscriptionProcessingMetadata(
    string SttProvider,
    string SttModel,
    int RequestCount,
    bool NativeDiarizationUsed,
    long? SttCostMicroUsd = null,
    long? SttRateMicroUsdPerHour = null,
    string? SttCostClassification = null,
    string? RoleAttributionModel = null,
    string? RoleAttributionStatus = null,
    int? RoleAttributionPromptTokens = null,
    int? RoleAttributionOutputTokens = null,
    long? RoleAttributionCostMicroUsd = null,
    string? DiarizationSource = null,
    string? DiarizationProvider = null,
    string? DiarizationModel = null,
    int DiarizationRequestCount = 0,
    long? DiarizationCostMicroUsd = null,
    long? DiarizationRateMicroUsdPerHour = null,
    string? DiarizationCostClassification = null);

public interface ITranscriptionEngine
{
    Task<TranscriptionResult> TranscribeAsync(string audioPath, ProjectSettings settings, CancellationToken ct = default);
}

public interface IRegisteredTranscriptionEngine : ITranscriptionEngine
{
    string EngineId { get; }
    IReadOnlyCollection<string> SupportedModels { get; }
    IReadOnlyCollection<string> ProviderDiarizationModels => [];
    IReadOnlyCollection<string> WordTimestampModels => [];
    string? GetAvailabilityError();
    string? GetProbeError();
}

public interface ITranscriptionEngineRegistry
{
    IReadOnlyCollection<string> GetSupportedEngines();
    IReadOnlyCollection<string> GetSupportedModels(string engineId);
    IReadOnlyCollection<string> GetProviderDiarizationModels(string engineId);
    IReadOnlyCollection<string> GetWordTimestampModels(string engineId);
    bool SupportsProviderDiarization(string engineId, string model);
    bool SupportsWordTimestamps(string engineId, string model);
    bool IsSupportedEngine(string engineId);
    bool IsSupportedModel(string engineId, string model);
    ITranscriptionEngine Resolve(string engineId);
}
