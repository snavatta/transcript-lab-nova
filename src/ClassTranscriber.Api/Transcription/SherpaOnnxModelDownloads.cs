using System.Formats.Tar;
using SharpCompress.Compressors;
using SharpCompress.Compressors.BZip2;

namespace ClassTranscriber.Api.Transcription;

public static class SherpaOnnxModelDownloads
{
    public const string ModelDownloadClientName = "SherpaOnnxModelDownloads";

    public static string GetModelDirectory(string modelsPath, string model)
        => Path.Combine(Path.GetFullPath(modelsPath), model);

    public static async Task DownloadModelAsync(
        IHttpClientFactory httpClientFactory,
        string downloadBaseUrl,
        string engineId,
        string model,
        string modelDir,
        SherpaOnnxDownloadDefinition definition,
        ILogger logger,
        CancellationToken ct)
    {
        var downloadUrl = $"{downloadBaseUrl.TrimEnd('/')}/{Uri.EscapeDataString(definition.TarballName)}";
        var tempRoot = Path.Combine(Path.GetTempPath(), $"transcriptlab-sherpa-{model}-{Guid.NewGuid():N}");
        var archivePath = Path.Combine(tempRoot, definition.TarballName);
        var extractedPath = Path.Combine(tempRoot, "extracted");

        Directory.CreateDirectory(tempRoot);
        Directory.CreateDirectory(extractedPath);

        logger.LogInformation(
            "{EngineId} model {Model} is missing. Downloading from {DownloadUrl} into {ModelDirectory}",
            engineId,
            model,
            downloadUrl,
            modelDir);

        try
        {
            var client = httpClientFactory.CreateClient(ModelDownloadClientName);
            using var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            await using (var responseStream = await response.Content.ReadAsStreamAsync(ct))
            await using (var archiveStream = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await DownloadProgressLogger.CopyToAsync(
                    responseStream,
                    archiveStream,
                    response.Content.Headers.ContentLength,
                    logger,
                    $"{engineId} model {model}",
                    ct);
                await archiveStream.FlushAsync(ct);
            }

            logger.LogInformation(
                "{EngineId} model {Model} download complete. Extracting required files from {ArchivePath}",
                engineId,
                model,
                archivePath);

            ExtractRequiredFiles(archivePath, extractedPath, definition, logger);

            logger.LogInformation(
                "{EngineId} model {Model} extraction complete. Writing resolved files into {ModelDirectory}",
                engineId,
                model,
                modelDir);

            Directory.CreateDirectory(modelDir);
            foreach (var fileName in definition.RequiredFiles)
            {
                var sourcePath = Path.Combine(extractedPath, fileName);
                var destinationPath = Path.Combine(modelDir, fileName);
                File.Move(sourcePath, destinationPath, overwrite: true);
            }

            var configPath = Path.Combine(modelDir, "config.json");
            await File.WriteAllTextAsync(configPath, BuildConfigJson(definition), ct);

            logger.LogInformation("Downloaded {EngineId} model {Model} to {ModelDirectory}", engineId, model, modelDir);
        }
        catch
        {
            try
            {
                if (Directory.Exists(tempRoot))
                    Directory.Delete(tempRoot, recursive: true);
            }
            catch
            {
                // Best effort cleanup for a failed download.
            }

            throw;
        }

        try
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
        catch
        {
            // Best effort cleanup for a completed download.
        }
    }

    private static void ExtractRequiredFiles(
        string archivePath,
        string destinationDirectory,
        SherpaOnnxDownloadDefinition definition,
        ILogger logger)
    {
        using var archiveStream = File.OpenRead(archivePath);
        using var decompressedStream = new BZip2Stream(archiveStream, CompressionMode.Decompress, false);
        var extractedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var reader = new TarReader(decompressedStream, leaveOpen: false);

        TarEntry? entry;
        while ((entry = reader.GetNextEntry()) is not null)
        {
            if (entry.EntryType is TarEntryType.Directory or TarEntryType.DirectoryList)
                continue;

            var fileName = Path.GetFileName(entry.Name);
            if (string.IsNullOrWhiteSpace(fileName))
                continue;

            if (!definition.RequiredFiles.Contains(fileName, StringComparer.OrdinalIgnoreCase))
                continue;

            var destinationPath = Path.Combine(destinationDirectory, fileName);
            var entryStream = entry.DataStream;
            if (entryStream is null)
                continue;

            logger.LogInformation(
                "Extracting SherpaOnnx asset {FileName} from {ArchiveName}",
                fileName,
                Path.GetFileName(archivePath));
            using var destinationStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
            entryStream.CopyTo(destinationStream);
            extractedFiles.Add(fileName);
        }

        var missingFiles = definition.RequiredFiles
            .Where(fileName => !extractedFiles.Contains(fileName))
            .ToArray();
        if (missingFiles.Length > 0)
        {
            throw new FileNotFoundException(
                $"Downloaded SherpaOnnx archive {Path.GetFileName(archivePath)} did not contain the expected files: {string.Join(", ", missingFiles)}.");
        }
    }

    private static string BuildConfigJson(SherpaOnnxDownloadDefinition definition)
        => definition.Backend switch
        {
            SherpaOnnxBackend.SenseVoice => $$"""
            {
              "backend": "sense_voice",
              "model": "{{definition.ModelFileName}}",
              "tokens": "{{definition.TokensFileName}}",
              "use_itn": {{definition.UseInverseTextNormalization.ToString().ToLowerInvariant()}}
            }
            """,
            SherpaOnnxBackend.Whisper => $$"""
            {
              "backend": "whisper",
              "encoder": "{{definition.EncoderFileName}}",
              "decoder": "{{definition.DecoderFileName}}",
              "tokens": "{{definition.TokensFileName}}",
              "task": "{{definition.Task}}"
            }
            """,
            _ => throw new InvalidOperationException($"Unsupported SherpaOnnx backend '{definition.Backend}'."),
        };
}
