using ClassTranscriber.Api.Contracts;

namespace ClassTranscriber.Api.Services;

public interface IExportService
{
    Task<ExportResult?> GenerateAsync(Guid projectId, string format, string? viewMode, bool? includeTimestamps, CancellationToken ct = default);
}

public record ExportResult(byte[] Content, string ContentType, string FileName);
