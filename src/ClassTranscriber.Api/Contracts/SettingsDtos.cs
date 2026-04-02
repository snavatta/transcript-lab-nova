namespace ClassTranscriber.Api.Contracts;

public sealed record GlobalSettingsDto
{
    public required string DefaultEngine { get; init; }
    public required string DefaultModel { get; init; }
    public required string DefaultLanguageMode { get; init; }
    public string? DefaultLanguageCode { get; init; }
    public required bool DefaultAudioNormalizationEnabled { get; init; }
    public required bool DefaultDiarizationEnabled { get; init; }
    public required string DefaultTranscriptViewMode { get; init; }
}

public sealed record TranscriptionEngineOptionDto
{
    public required string Engine { get; init; }
    public required string[] Models { get; init; }
}

public sealed record TranscriptionOptionsDto
{
    public required TranscriptionEngineOptionDto[] Engines { get; init; }
}

public sealed record UpdateGlobalSettingsRequest
{
    public required string DefaultEngine { get; init; }
    public required string DefaultModel { get; init; }
    public required string DefaultLanguageMode { get; init; }
    public string? DefaultLanguageCode { get; init; }
    public required bool DefaultAudioNormalizationEnabled { get; init; }
    public required bool DefaultDiarizationEnabled { get; init; }
    public required string DefaultTranscriptViewMode { get; init; }
}
