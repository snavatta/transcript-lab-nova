using System.Net.Http.Headers;
using ClassTranscriber.Api.Transcription;

namespace ClassTranscriber.Api.Tests;

internal sealed class OversizedProviderResponseContent : StreamContent
{
    public const int ResponseLimitBytes = ProviderResponseLimits.MaximumResponseBytes;

    public OversizedProviderResponseContent(bool declareLength)
        : this(new CountingPatternStream(ResponseLimitBytes + 65_536), declareLength)
    {
    }

    private OversizedProviderResponseContent(CountingPatternStream stream, bool declareLength)
        : base(stream)
    {
        Source = stream;
        Headers.ContentType = new MediaTypeHeaderValue("application/json");
        if (declareLength)
            Headers.ContentLength = stream.TotalLength;
    }

    public CountingPatternStream Source { get; }
}

internal sealed class CountingPatternStream(long totalLength) : Stream
{
    private static readonly byte[] Pattern = "provider-secret-sentinel"u8.ToArray();
    private long _position;

    public long TotalLength { get; } = totalLength;
    public long BytesRead { get; private set; }
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => _position; set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) =>
        Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        var read = (int)Math.Min(buffer.Length, TotalLength - _position);
        for (var index = 0; index < read; index++)
            buffer[index] = Pattern[(int)((_position + index) % Pattern.Length)];
        _position += read;
        BytesRead += read;
        return read;
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Read(buffer.Span));
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
