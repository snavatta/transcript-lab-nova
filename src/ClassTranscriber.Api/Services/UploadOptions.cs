namespace ClassTranscriber.Api.Services;

public sealed class UploadOptions
{
    public const string SectionName = "Uploads";
    public const long DefaultMaxRequestBodySizeBytes = 10_737_418_240;

    public long MaxRequestBodySizeBytes { get; set; } = DefaultMaxRequestBodySizeBytes;
}
