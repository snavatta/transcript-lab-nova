using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace ClassTranscriber.Api.Storage;

public class LocalFileStorage : IFileStorage
{
    private readonly StorageOptions _options;

    public LocalFileStorage(IOptions<StorageOptions> options)
    {
        _options = options.Value;
        EnsureBaseDirectories();
    }

    public string GetUploadsPath() => _options.UploadsPath;
    public string GetAudioPath() => _options.AudioPath;
    public string GetTempPath() => _options.TempPath;
    public string GetExportsPath() => _options.ExportsPath;

    public string GenerateSafeFileName(string originalFileName)
    {
        var extension = Path.GetExtension(originalFileName)?.ToLowerInvariant() ?? "";
        var randomBytes = RandomNumberGenerator.GetBytes(16);
        var safeName = Convert.ToHexString(randomBytes).ToLowerInvariant();
        return $"{safeName}{extension}";
    }

    public async Task SaveFileAsync(string relativePath, Stream content, CancellationToken ct = default)
    {
        var fullPath = GetFullPath(relativePath);
        ValidatePath(fullPath);

        var directory = Path.GetDirectoryName(fullPath);
        if (directory != null)
            Directory.CreateDirectory(directory);

        await using var fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await content.CopyToAsync(fileStream, ct);
    }

    public Task<Stream> ReadFileAsync(string relativePath, CancellationToken ct = default)
    {
        var fullPath = GetFullPath(relativePath);
        ValidatePath(fullPath);

        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"File not found: {relativePath}");

        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult(stream);
    }

    public Task DeleteFileAsync(string relativePath, CancellationToken ct = default)
    {
        var fullPath = GetFullPath(relativePath);
        ValidatePath(fullPath);

        if (File.Exists(fullPath))
            File.Delete(fullPath);

        return Task.CompletedTask;
    }

    public bool FileExists(string relativePath)
    {
        var fullPath = GetFullPath(relativePath);
        ValidatePath(fullPath);
        return File.Exists(fullPath);
    }

    public string GetFullPath(string relativePath)
    {
        return Path.GetFullPath(Path.Combine(_options.BasePath, relativePath));
    }

    public void EnsureDirectoryExists(string relativePath)
    {
        var fullPath = GetFullPath(relativePath);
        ValidatePath(fullPath);
        Directory.CreateDirectory(fullPath);
    }

    private void ValidatePath(string fullPath)
    {
        var basePath = Path.GetFullPath(_options.BasePath);
        if (!fullPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Path traversal detected.");
    }

    private void EnsureBaseDirectories()
    {
        var basePath = Path.GetFullPath(_options.BasePath);
        Directory.CreateDirectory(basePath);
        Directory.CreateDirectory(Path.Combine(basePath, _options.UploadsPath));
        Directory.CreateDirectory(Path.Combine(basePath, _options.AudioPath));
        Directory.CreateDirectory(Path.Combine(basePath, _options.TranscriptsPath));
        Directory.CreateDirectory(Path.Combine(basePath, _options.ExportsPath));
        Directory.CreateDirectory(Path.Combine(basePath, _options.TempPath));
        Directory.CreateDirectory(Path.Combine(basePath, _options.ModelsPath));
    }
}
