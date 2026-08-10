using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ClassTranscriber.Api.Mcp;

internal sealed class TranscriptCursorCodec(string? integrityKey)
{
    private static readonly byte[] CursorDomain =
        Encoding.UTF8.GetBytes("TranscriptLab.ChatGptSource.Cursor.v1\0");
    private readonly byte[] integrityKeyBytes = Encoding.UTF8.GetBytes(integrityKey ?? string.Empty);

    public string Encode(TranscriptCursorPayload payload)
    {
        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        var integrity = ComputeIntegrity(payloadBytes);
        var cursorBytes = new byte[payloadBytes.Length + integrity.Length];
        payloadBytes.CopyTo(cursorBytes, 0);
        integrity.CopyTo(cursorBytes, payloadBytes.Length);
        return ToBase64Url(cursorBytes);
    }

    public bool TryDecode(string cursor, out TranscriptCursorPayload payload)
    {
        payload = default!;
        if (cursor.Length is 0 or > 2048 || cursor.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
        {
            return false;
        }

        try
        {
            var base64 = cursor.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + ((4 - base64.Length % 4) % 4), '=');
            var cursorBytes = Convert.FromBase64String(base64);
            if (!string.Equals(cursor, ToBase64Url(cursorBytes), StringComparison.Ordinal) ||
                cursorBytes.Length <= SHA256.HashSizeInBytes)
            {
                return false;
            }

            var payloadBytes = cursorBytes.AsSpan(0, cursorBytes.Length - SHA256.HashSizeInBytes);
            var suppliedIntegrity = cursorBytes.AsSpan(cursorBytes.Length - SHA256.HashSizeInBytes);
            var expectedIntegrity = ComputeIntegrity(payloadBytes);
            if (!CryptographicOperations.FixedTimeEquals(suppliedIntegrity, expectedIntegrity))
                return false;

            payload = JsonSerializer.Deserialize<TranscriptCursorPayload>(payloadBytes)!;
            return payload is not null;
        }
        catch (Exception exception) when (exception is FormatException or JsonException or NotSupportedException)
        {
            return false;
        }
    }

    private byte[] ComputeIntegrity(ReadOnlySpan<byte> payload)
    {
        using var hash = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, integrityKeyBytes);
        hash.AppendData(CursorDomain);
        hash.AppendData(payload);
        return hash.GetHashAndReset();
    }

    private static string ToBase64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

internal sealed record TranscriptCursorPayload(
    int Version,
    string Mode,
    Guid ProjectId,
    long TranscriptVersion,
    int SegmentIndex,
    int CharacterOffset);
