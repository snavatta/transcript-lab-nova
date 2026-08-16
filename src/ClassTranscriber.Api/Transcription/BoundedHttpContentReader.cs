using System.Buffers;
using System.Text;
using System.Text.Json;

namespace ClassTranscriber.Api.Transcription;

public static class ProviderResponseLimits
{
    /// <summary>
    /// Caps decoded provider response bodies at 16 MiB. This leaves several-fold headroom for
    /// 40-120 minute verbose transcription JSON with word timestamps while preventing an
    /// untrusted provider from causing unbounded buffering.
    /// </summary>
    public const int MaximumResponseBytes = 16 * 1024 * 1024;
}

internal sealed class ProviderResponseTooLargeException(string message) : InvalidOperationException(message);

internal static class BoundedHttpContentReader
{
    private const int BufferSize = 81_920;

    public static async Task<string> ReadStringAsync(
        HttpContent content,
        string oversizedMessage,
        CancellationToken ct)
    {
        var bytes = await ReadBytesAsync(content, oversizedMessage, ct);
        return Encoding.UTF8.GetString(bytes);
    }

    public static async Task<T?> ReadJsonAsync<T>(
        HttpContent content,
        string oversizedMessage,
        CancellationToken ct)
    {
        var bytes = await ReadBytesAsync(content, oversizedMessage, ct);
        return JsonSerializer.Deserialize<T>(bytes);
    }

    private static async Task<byte[]> ReadBytesAsync(
        HttpContent content,
        string oversizedMessage,
        CancellationToken ct)
    {
        if (content.Headers.ContentLength is > ProviderResponseLimits.MaximumResponseBytes)
            throw new ProviderResponseTooLargeException(oversizedMessage);

        var initialCapacity = content.Headers.ContentLength is > 0 and <= ProviderResponseLimits.MaximumResponseBytes
            ? checked((int)content.Headers.ContentLength.Value)
            : BufferSize;
        using var destination = new MemoryStream(initialCapacity);
        await using var source = await content.ReadAsStreamAsync(ct);
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            var totalBytes = 0;
            while (true)
            {
                var remainingThroughSentinel = checked(
                    ProviderResponseLimits.MaximumResponseBytes + 1 - totalBytes);
                var read = await source.ReadAsync(
                    buffer.AsMemory(0, Math.Min(buffer.Length, remainingThroughSentinel)),
                    ct);
                if (read == 0)
                    return destination.ToArray();

                totalBytes = checked(totalBytes + read);
                if (totalBytes > ProviderResponseLimits.MaximumResponseBytes)
                    throw new ProviderResponseTooLargeException(oversizedMessage);

                await destination.WriteAsync(buffer.AsMemory(0, read), ct);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
