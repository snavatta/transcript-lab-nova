using System.Text;
using System.Text.Json;
using ClassTranscriber.Api.Contracts;
using ClassTranscriber.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClassTranscriber.Api.Services;

public class ExportService : IExportService
{
    private readonly AppDbContext _db;

    public ExportService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<ExportResult?> GenerateAsync(Guid projectId, string format, string? viewMode, bool? includeTimestamps, CancellationToken ct = default)
    {
        var project = await _db.Projects
            .Include(p => p.Folder)
            .Include(p => p.Transcript)
            .FirstOrDefaultAsync(p => p.Id == projectId, ct);

        if (project?.Transcript is null)
            return null;

        var segments = JsonSerializer.Deserialize<TranscriptSegmentDto[]>(
            project.Transcript.StructuredSegmentsJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];

        var showTimestamps = includeTimestamps ?? (viewMode?.Equals("timestamped", StringComparison.OrdinalIgnoreCase) == true);
        var safeName = SanitizeExportFileName(project.Name);

        return format.ToLowerInvariant() switch
        {
            "txt" => GenerateTxt(project, segments, showTimestamps, safeName),
            "md" => GenerateMarkdown(project, segments, showTimestamps, safeName),
            "html" => GenerateHtml(project, segments, showTimestamps, safeName),
            "pdf" => GenerateHtmlAsPdf(project, segments, showTimestamps, safeName),
            _ => null,
        };
    }

    private static ExportResult GenerateTxt(Domain.Project project, TranscriptSegmentDto[] segments, bool showTimestamps, string safeName)
    {
        var sb = new StringBuilder();
        sb.AppendLine(project.Name);
        sb.AppendLine($"Folder: {project.Folder?.Name ?? "—"}");
        sb.AppendLine($"Original file: {project.OriginalFileName}");
        sb.AppendLine();

        foreach (var seg in segments)
        {
            if (showTimestamps)
                sb.Append($"[{FormatTime(seg.StartMs)}] ");
            if (seg.Speaker is not null)
                sb.Append($"{seg.Speaker}: ");
            sb.AppendLine(seg.Text);
        }

        return new ExportResult(Encoding.UTF8.GetBytes(sb.ToString()), "text/plain; charset=utf-8", $"{safeName}.txt");
    }

    private static ExportResult GenerateMarkdown(Domain.Project project, TranscriptSegmentDto[] segments, bool showTimestamps, string safeName)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {project.Name}");
        sb.AppendLine();
        sb.AppendLine($"- **Folder:** {project.Folder?.Name ?? "—"}");
        sb.AppendLine($"- **Original file:** {project.OriginalFileName}");
        sb.AppendLine($"- **Processed:** {project.CompletedAtUtc?.ToString("O") ?? "—"}");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();

        foreach (var seg in segments)
        {
            if (showTimestamps)
                sb.Append($"**[{FormatTime(seg.StartMs)}]** ");
            if (seg.Speaker is not null)
                sb.Append($"*{seg.Speaker}:* ");
            sb.AppendLine(seg.Text);
            sb.AppendLine();
        }

        return new ExportResult(Encoding.UTF8.GetBytes(sb.ToString()), "text/markdown; charset=utf-8", $"{safeName}.md");
    }

    private static ExportResult GenerateHtml(Domain.Project project, TranscriptSegmentDto[] segments, bool showTimestamps, string safeName)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\"><head><meta charset=\"utf-8\">");
        sb.AppendLine($"<title>{Escape(project.Name)}</title>");
        sb.AppendLine("<style>body{font-family:system-ui,sans-serif;max-width:800px;margin:2rem auto;padding:0 1rem;line-height:1.6}");
        sb.AppendLine("h1{margin-bottom:0}.meta{color:#666;margin-bottom:2rem}.ts{color:#999;font-size:0.85em;margin-right:0.5em}");
        sb.AppendLine(".speaker{font-weight:600;margin-right:0.3em}.seg{margin-bottom:0.5rem}</style></head><body>");
        sb.AppendLine($"<h1>{Escape(project.Name)}</h1>");
        sb.AppendLine($"<div class=\"meta\"><p>Folder: {Escape(project.Folder?.Name ?? "—")}</p>");
        sb.AppendLine($"<p>Original file: {Escape(project.OriginalFileName)}</p></div>");
        sb.AppendLine("<hr>");

        foreach (var seg in segments)
        {
            sb.Append("<div class=\"seg\">");
            if (showTimestamps)
                sb.Append($"<span class=\"ts\">[{FormatTime(seg.StartMs)}]</span>");
            if (seg.Speaker is not null)
                sb.Append($"<span class=\"speaker\">{Escape(seg.Speaker)}:</span> ");
            sb.Append(Escape(seg.Text));
            sb.AppendLine("</div>");
        }

        sb.AppendLine("</body></html>");
        return new ExportResult(Encoding.UTF8.GetBytes(sb.ToString()), "text/html; charset=utf-8", $"{safeName}.html");
    }

    private static ExportResult GenerateHtmlAsPdf(Domain.Project project, TranscriptSegmentDto[] segments, bool showTimestamps, string safeName)
    {
        // For MVP, serve PDF as HTML with print-friendly styling.
        // A proper HTML-to-PDF library can be integrated later.
        var htmlResult = GenerateHtml(project, segments, showTimestamps, safeName);
        return new ExportResult(htmlResult.Content, "application/pdf", $"{safeName}.pdf");
    }

    private static string FormatTime(long ms)
    {
        var ts = TimeSpan.FromMilliseconds(ms);
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes}:{ts.Seconds:D2}";
    }

    private static string Escape(string s) =>
        System.Net.WebUtility.HtmlEncode(s);

    private static string SanitizeExportFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "transcript" : sanitized;
    }
}
