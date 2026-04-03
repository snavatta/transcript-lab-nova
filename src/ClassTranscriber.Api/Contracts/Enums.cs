namespace ClassTranscriber.Api.Contracts;

public enum ProjectStatus
{
    Draft,
    Queued,
    PreparingMedia,
    Transcribing,
    Completed,
    Failed,
    Cancelled
}

public enum MediaType
{
    Audio,
    Video,
    Unknown
}

public enum LanguageMode
{
    Auto,
    Fixed
}

public enum TranscriptViewMode
{
    Readable,
    Timestamped
}

public enum TranscriptionEngine
{
    SherpaOnnx,
    SherpaOnnxSenseVoice,
    WhisperNet,
    WhisperNetCuda,
    WhisperNetOpenVino,
    OpenVinoGenAi
}
