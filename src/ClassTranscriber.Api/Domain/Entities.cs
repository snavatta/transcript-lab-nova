using ClassTranscriber.Api.Contracts;

namespace ClassTranscriber.Api.Domain;

public class Folder
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string IconKey { get; set; } = FolderAppearance.DefaultIconKey;
    public string ColorHex { get; set; } = FolderAppearance.DefaultColorHex;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public long TotalSizeBytes { get; set; }

    public List<Project> Projects { get; set; } = [];
}

public class Project
{
    public Guid Id { get; set; }
    public Guid FolderId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string StoredFileName { get; set; } = string.Empty;
    public MediaType MediaType { get; set; }
    public string FileExtension { get; set; } = string.Empty;
    public string MediaPath { get; set; } = string.Empty;
    public ProjectStatus Status { get; set; }
    public int Progress { get; set; }
    public long? DurationMs { get; set; }
    public long? TranscriptionElapsedMs { get; set; }
    public long? TotalProcessingElapsedMs { get; set; }
    public long? MediaInspectionElapsedMs { get; set; }
    public long? AudioExtractionElapsedMs { get; set; }
    public long? AudioNormalizationElapsedMs { get; set; }
    public long? ResultPersistenceElapsedMs { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? QueuedAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? FailedAtUtc { get; set; }
    public string? ErrorMessage { get; set; }
    public long? OriginalFileSizeBytes { get; set; }
    public long? WorkspaceSizeBytes { get; set; }
    public long? TotalSizeBytes { get; set; }

    public ProjectSettings Settings { get; set; } = new();
    public Folder Folder { get; set; } = null!;
    public Transcript? Transcript { get; set; }
}

public class ProjectSettings
{
    public string Engine { get; set; } = "WhisperNet";
    public string Model { get; set; } = "small";
    public string LanguageMode { get; set; } = "Auto";
    public string? LanguageCode { get; set; }
    public bool AudioNormalizationEnabled { get; set; } = true;
    public bool DiarizationEnabled { get; set; }
}

public class Transcript
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public string PlainText { get; set; } = string.Empty;
    public string StructuredSegmentsJson { get; set; } = "[]";
    public string? DetectedLanguage { get; set; }
    public long? DurationMs { get; set; }
    public int SegmentCount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public Project Project { get; set; } = null!;
}

public class GlobalSettings
{
    public int Id { get; set; } = 1;
    public string DefaultEngine { get; set; } = "WhisperNet";
    public string DefaultModel { get; set; } = "small";
    public string DefaultLanguageMode { get; set; } = "Auto";
    public string? DefaultLanguageCode { get; set; }
    public bool DefaultAudioNormalizationEnabled { get; set; } = true;
    public bool DefaultDiarizationEnabled { get; set; }
    public string DefaultTranscriptViewMode { get; set; } = "Readable";
}
