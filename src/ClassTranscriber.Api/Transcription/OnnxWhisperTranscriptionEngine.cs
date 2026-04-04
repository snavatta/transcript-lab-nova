using ClassTranscriber.Api.Domain;

namespace ClassTranscriber.Api.Transcription;

public sealed class OnnxWhisperTranscriptionEngine : IRegisteredTranscriptionEngine
{
    public string EngineId => "OnnxWhisper";

    public IReadOnlyCollection<string> SupportedModels { get; } = [];

    public string? GetAvailabilityError() =>
        "OnnxWhisper engine is not yet implemented. It is reserved for a future release using Microsoft.ML.OnnxRuntime.";

    public string? GetProbeError() => GetAvailabilityError();

    public Task<TranscriptionResult> TranscribeAsync(
        string audioPath,
        ProjectSettings settings,
        CancellationToken ct = default)
    {
        throw new NotImplementedException(
            "OnnxWhisper engine is not yet implemented. It is reserved for a future release.");
    }
}
