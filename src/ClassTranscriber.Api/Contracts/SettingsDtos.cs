namespace ClassTranscriber.Api.Contracts;

public sealed record GlobalSettingsDto
{
    public required string DefaultEngine { get; init; }
    public required string DefaultModel { get; init; }
    public required string DefaultLanguageMode { get; init; }
    public string? DefaultLanguageCode { get; init; }
    public required bool DefaultAudioNormalizationEnabled { get; init; }
    public required bool DefaultDiarizationEnabled { get; init; }
    public string DefaultDiarizationSource { get; init; } = "Local";
    public required string DefaultDiarizationMode { get; init; }
    public bool DefaultSpeakerRoleAttributionEnabled { get; init; }
    public required string DefaultTranscriptViewMode { get; init; }
}

public sealed record TranscriptionEngineOptionDto
{
    public required string Engine { get; init; }
    public required string[] Models { get; init; }
    public required string[] ProviderDiarizationModels { get; init; }
    public required string[] WordTimestampModels { get; init; }
}

public sealed record TranscriptionOptionsDto
{
    public required TranscriptionEngineOptionDto[] Engines { get; init; }
    public bool SpeakerRoleAttributionAvailable { get; init; }
    public string SpeakerRoleAttributionModel { get; init; } = string.Empty;
    public string? RecommendedHostedEngine { get; init; }
    public string? RecommendedHostedModel { get; init; }
    public bool XaiDiarizationAvailable { get; init; }
    public string XaiDiarizationModel { get; init; } = "grok-stt-1.0";
}

public sealed record TranscriptionModelCatalogDto
{
    public required TranscriptionModelEntryDto[] Models { get; init; }
}

public sealed record TranscriptionModelEntryDto
{
    public required string Engine { get; init; }
    public required string Model { get; init; }
    public required bool IsInstalled { get; init; }
    public string? InstallPath { get; init; }
    public required bool CanDownload { get; init; }
    public required bool CanRedownload { get; init; }
    public required bool CanProbe { get; init; }
    public required string ProbeState { get; init; }
    public required string ProbeMessage { get; init; }
}

public sealed record ManageTranscriptionModelRequest
{
    public required string Engine { get; init; }
    public required string Model { get; init; }
    public required string Action { get; init; }
}

public sealed record UpdateGlobalSettingsRequest
{
    public required string DefaultEngine { get; init; }
    public required string DefaultModel { get; init; }
    public required string DefaultLanguageMode { get; init; }
    public string? DefaultLanguageCode { get; init; }
    public required bool DefaultAudioNormalizationEnabled { get; init; }
    public required bool DefaultDiarizationEnabled { get; init; }
    public string DefaultDiarizationSource { get; init; } = "Local";
    public required string DefaultDiarizationMode { get; init; }
    public bool DefaultSpeakerRoleAttributionEnabled { get; init; }
    public required string DefaultTranscriptViewMode { get; init; }
}
