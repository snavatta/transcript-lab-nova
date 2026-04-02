namespace ClassTranscriber.Api.Storage;

public class StorageOptions
{
    public string BasePath { get; set; } = "/data";
    public string UploadsPath { get; set; } = "uploads";
    public string AudioPath { get; set; } = "audio";
    public string TranscriptsPath { get; set; } = "transcripts";
    public string ExportsPath { get; set; } = "exports";
    public string TempPath { get; set; } = "temp";
    public string ModelsPath { get; set; } = "models";
}

public interface IFileStorage
{
    string GetUploadsPath();
    string GetAudioPath();
    string GetTempPath();
    string GetExportsPath();
    string GenerateSafeFileName(string originalFileName);
    Task SaveFileAsync(string relativePath, Stream content, CancellationToken ct = default);
    Task<Stream> ReadFileAsync(string relativePath, CancellationToken ct = default);
    Task DeleteFileAsync(string relativePath, CancellationToken ct = default);
    bool FileExists(string relativePath);
    string GetFullPath(string relativePath);
    void EnsureDirectoryExists(string relativePath);
}
