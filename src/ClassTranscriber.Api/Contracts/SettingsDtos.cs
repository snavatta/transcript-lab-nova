namespace ClassTranscriber.Api.Contracts;

public sealed record GlobalSettingsDto
{
    public required string DefaultEngine { get; init; }
    public required string DefaultModel { get; init; }
    public required string DefaultLanguageMode { get; init; }
    public string? DefaultLanguageCode { get; init; }
    public required bool DefaultAudioNormalizationEnabled { get; init; }
    public required bool DefaultDiarizationEnabled { get; init; }
    public required string DefaultDiarizationMode { get; init; }
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
    public required string DefaultDiarizationMode { get; init; }
    public required string DefaultTranscriptViewMode { get; init; }
}
