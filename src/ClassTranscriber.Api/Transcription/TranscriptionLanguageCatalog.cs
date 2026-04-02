namespace ClassTranscriber.Api.Transcription;

public static class TranscriptionLanguageCatalog
{
    private static readonly string[] SherpaOnnxSenseVoiceFixedLanguages = ["zh", "en", "ja", "ko", "yue"];

    public static IReadOnlyCollection<string> GetSupportedFixedLanguages(string engineId)
        => string.Equals(engineId, "SherpaOnnxSenseVoice", StringComparison.OrdinalIgnoreCase)
            ? SherpaOnnxSenseVoiceFixedLanguages
            : [];

    public static bool IsSupportedFixedLanguage(string engineId, string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
            return false;

        var supportedLanguages = GetSupportedFixedLanguages(engineId);
        if (supportedLanguages.Count == 0)
            return true;

        return supportedLanguages.Contains(languageCode.Trim(), StringComparer.OrdinalIgnoreCase);
    }
}
