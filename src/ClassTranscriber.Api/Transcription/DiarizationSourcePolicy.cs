namespace ClassTranscriber.Api.Transcription;

internal static class DiarizationSourcePolicy
{
    public const string Local = "Local";
    public const string Provider = "Provider";
    public const string Xai = "Xai";
    public const string OpenRouterEngine = "OpenRouter";
    public const string XaiEngine = "Xai";
    public const string XaiModel = "grok-stt-1.0";

    public static bool TryNormalize(string? source, out string normalized)
    {
        normalized = source?.Trim() switch
        {
            { } value when value.Equals(Local, StringComparison.OrdinalIgnoreCase) => Local,
            { } value when value.Equals(Provider, StringComparison.OrdinalIgnoreCase) => Provider,
            { } value when value.Equals(Xai, StringComparison.OrdinalIgnoreCase) => Xai,
            _ => string.Empty,
        };
        return normalized.Length > 0;
    }

    public static bool IsSupported(
        string source,
        string engine,
        string model,
        ITranscriptionEngineRegistry engineRegistry)
        => source switch
        {
            Local => true,
            Provider => engineRegistry.SupportsProviderDiarization(engine, model),
            Xai => CanUseXai(engine, model, engineRegistry),
            _ => false,
        };

    public static string NormalizeStored(
        string? source,
        string engine,
        string model,
        ITranscriptionEngineRegistry engineRegistry)
        => TryNormalize(source, out var normalized)
            && IsSupported(normalized, engine, model, engineRegistry)
                ? normalized
                : Local;

    public static string ResolveDefault(
        string engine,
        string model,
        ITranscriptionEngineRegistry engineRegistry)
        => engineRegistry.SupportsProviderDiarization(engine, model) ? Provider : Local;

    public static bool IsXaiAvailable(ITranscriptionEngineRegistry engineRegistry)
        => engineRegistry.GetSupportedEngines().Contains(XaiEngine, StringComparer.OrdinalIgnoreCase)
            && engineRegistry.SupportsProviderDiarization(XaiEngine, XaiModel);

    private static bool CanUseXai(
        string engine,
        string model,
        ITranscriptionEngineRegistry engineRegistry)
        => engine.Equals(OpenRouterEngine, StringComparison.OrdinalIgnoreCase)
            && model is "openai/whisper-large-v3" or "openai/whisper-large-v3-turbo"
            && engineRegistry.SupportsWordTimestamps(engine, model)
            && !engineRegistry.SupportsProviderDiarization(engine, model)
            && IsXaiAvailable(engineRegistry);
}
