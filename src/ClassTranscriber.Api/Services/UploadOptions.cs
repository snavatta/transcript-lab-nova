namespace ClassTranscriber.Api.Services;

public sealed class UploadOptions
{
    public const string SectionName = "Uploads";
    public const long DefaultMaxRequestBodySizeBytes = 1_073_741_824;

    public long MaxRequestBodySizeBytes { get; set; } = DefaultMaxRequestBodySizeBytes;
}
