namespace ClassTranscriber.Api.Transcription;

internal static class DownloadProgressLogger
{
    private const int BufferSize = 1024 * 80;
    private const int PercentStep = 10;
    private const long UnknownLengthLogStepBytes = 25L * 1024 * 1024;

    public static async Task CopyToAsync(
        Stream source,
        Stream destination,
        long? totalBytes,
        ILogger logger,
        string artifactName,
        CancellationToken ct)
    {
        var buffer = new byte[BufferSize];
        long bytesCopied = 0;
        var nextPercentToLog = PercentStep;
        var nextUnknownLengthLogBytes = UnknownLengthLogStepBytes;

        if (totalBytes is > 0)
        {
            logger.LogInformation(
                "Downloading {ArtifactName}. size={SizeBytes} bytes",
                artifactName,
                totalBytes.Value);
        }
        else
        {
            logger.LogInformation(
                "Downloading {ArtifactName}. total size unknown",
                artifactName);
        }

        while (true)
        {
            var bytesRead = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
            if (bytesRead == 0)
                break;

            await destination.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            bytesCopied += bytesRead;

            if (totalBytes is > 0)
            {
                var progressPercent = (int)((bytesCopied * 100) / totalBytes.Value);
                while (progressPercent >= nextPercentToLog && nextPercentToLog < 100)
                {
                    logger.LogInformation(
                        "Downloading {ArtifactName}: {Percent}% ({CopiedBytes}/{TotalBytes} bytes)",
                        artifactName,
                        nextPercentToLog,
                        bytesCopied,
                        totalBytes.Value);
                    nextPercentToLog += PercentStep;
                }
            }
            else if (bytesCopied >= nextUnknownLengthLogBytes)
            {
                logger.LogInformation(
                    "Downloading {ArtifactName}: {CopiedBytes} bytes received",
                    artifactName,
                    bytesCopied);
                nextUnknownLengthLogBytes += UnknownLengthLogStepBytes;
            }
        }

        logger.LogInformation(
            "Downloading {ArtifactName}: completed. total={CopiedBytes} bytes",
            artifactName,
            bytesCopied);
    }
}
