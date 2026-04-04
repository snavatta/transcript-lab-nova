using ClassTranscriber.Api.Transcription;

namespace ClassTranscriber.Api.Services;

internal static class TranscriptionSettingsDefaults
{
    public const string PreferredEngine = "WhisperNet";
    public const string PreferredModel = "small";
    public const string PreferredOpenVinoWhisperSidecarModel = "base-int8";

    public static string ResolveSupportedEngine(ITranscriptionEngineRegistry engineRegistry, string? requestedEngine)
    {
        var normalized = requestedEngine?.Trim();
        if (!string.IsNullOrWhiteSpace(normalized) && engineRegistry.IsSupportedEngine(normalized))
            return normalized;

        if (engineRegistry.IsSupportedEngine(PreferredEngine))
            return PreferredEngine;

        return engineRegistry.GetSupportedEngines().FirstOrDefault() ?? PreferredEngine;
    }

    public static string ResolveSupportedModel(ITranscriptionEngineRegistry engineRegistry, string engine, string? requestedModel)
    {
        var normalized = requestedModel?.Trim();
        if (!string.IsNullOrWhiteSpace(normalized) && engineRegistry.IsSupportedModel(engine, normalized))
            return normalized;

        var supportedModels = engineRegistry.GetSupportedModels(engine);
        var preferredModel = GetPreferredModel(engine);
        if (supportedModels.Contains(preferredModel, StringComparer.OrdinalIgnoreCase))
            return preferredModel;

        return supportedModels.FirstOrDefault() ?? preferredModel;
    }

    public static (string LanguageMode, string? LanguageCode) ResolveSupportedLanguage(
        string engine,
        string? requestedLanguageMode,
        string? requestedLanguageCode)
    {
        var normalizedCode = string.IsNullOrWhiteSpace(requestedLanguageCode)
            ? null
            : requestedLanguageCode.Trim();
        var wantsFixed = string.Equals(requestedLanguageMode?.Trim(), "Fixed", StringComparison.OrdinalIgnoreCase);

        if (wantsFixed
            && normalizedCode is not null
            && TranscriptionLanguageCatalog.IsSupportedFixedLanguage(engine, normalizedCode))
        {
            return ("Fixed", normalizedCode);
        }

        return ("Auto", null);
    }

    private static string GetPreferredModel(string engine)
        => string.Equals(engine, "OpenVinoWhisperSidecar", StringComparison.OrdinalIgnoreCase)
            ? PreferredOpenVinoWhisperSidecarModel
            : PreferredModel;
}
