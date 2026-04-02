namespace ClassTranscriber.Api.Transcription;

public static class GgmlModelDownloads
{
    public const string ModelDownloadClientName = "WhisperModelDownloads";

    public static string GetModelFileName(string model)
    {
        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Whisper model is required.", nameof(model));

        foreach (var ch in model)
        {
            if (!char.IsLetterOrDigit(ch) && ch is not '-' and not '_' and not '.')
                throw new InvalidOperationException($"Invalid Whisper model name '{model}'.");
        }

        return $"ggml-{model}.bin";
    }

    public static string GetModelPath(string modelsPath, string model)
    {
        var modelsRoot = Path.GetFullPath(modelsPath);
        Directory.CreateDirectory(modelsRoot);
        return Path.GetFullPath(Path.Combine(modelsRoot, GetModelFileName(model)));
    }

    public static async Task DownloadModelAsync(
        IHttpClientFactory httpClientFactory,
        string downloadBaseUrl,
        string model,
        string destinationPath,
        ILogger logger,
        CancellationToken ct)
    {
        var modelFileName = GetModelFileName(model);
        var downloadUrl = $"{downloadBaseUrl.TrimEnd('/')}/{Uri.EscapeDataString(modelFileName)}";
        var tempPath = $"{destinationPath}.download";

        logger.LogInformation(
            "Whisper model {Model} is missing. Downloading from {DownloadUrl} to {DestinationPath}",
            model,
            downloadUrl,
            destinationPath);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? Path.GetTempPath());

            if (File.Exists(tempPath))
                File.Delete(tempPath);

            var client = httpClientFactory.CreateClient(ModelDownloadClientName);
            using var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            await using var source = await response.Content.ReadAsStreamAsync(ct);
            await using var destination = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await DownloadProgressLogger.CopyToAsync(
                source,
                destination,
                response.Content.Headers.ContentLength,
                logger,
                $"Whisper model {model}",
                ct);
            await destination.FlushAsync(ct);

            File.Move(tempPath, destinationPath, overwrite: true);
            logger.LogInformation("Downloaded Whisper model {Model} to {DestinationPath}", model, destinationPath);
        }
        catch
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch
            {
                // Best effort cleanup for a failed download.
            }

            throw;
        }
    }
}
