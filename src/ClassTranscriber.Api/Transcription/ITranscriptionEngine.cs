using ClassTranscriber.Api.Contracts;
using ClassTranscriber.Api.Domain;

namespace ClassTranscriber.Api.Transcription;

public record TranscriptionResult(
    string PlainText,
    TranscriptSegmentDto[] Segments,
    string? DetectedLanguage,
    long? DurationMs);

public interface ITranscriptionEngine
{
    Task<TranscriptionResult> TranscribeAsync(string audioPath, ProjectSettings settings, CancellationToken ct = default);
}

public interface IRegisteredTranscriptionEngine : ITranscriptionEngine
{
    string EngineId { get; }
    IReadOnlyCollection<string> SupportedModels { get; }
    string? GetAvailabilityError();
}

public interface ITranscriptionEngineRegistry
{
    IReadOnlyCollection<string> GetSupportedEngines();
    IReadOnlyCollection<string> GetSupportedModels(string engineId);
    bool IsSupportedEngine(string engineId);
    bool IsSupportedModel(string engineId, string model);
    ITranscriptionEngine Resolve(string engineId);
}
