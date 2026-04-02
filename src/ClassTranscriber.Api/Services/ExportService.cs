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
            "pdf" => GeneratePdf(project, segments, showTimestamps, safeName),
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

    private static ExportResult GeneratePdf(Domain.Project project, TranscriptSegmentDto[] segments, bool showTimestamps, string safeName)
    {
        var lines = BuildExportLines(project, segments, showTimestamps);
        var pdf = BuildPdfDocument(lines);
        return new ExportResult(pdf, "application/pdf", $"{safeName}.pdf");
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

    private static List<string> BuildExportLines(Domain.Project project, TranscriptSegmentDto[] segments, bool showTimestamps)
    {
        var lines = new List<string>
        {
            project.Name,
            "",
            $"Folder: {project.Folder?.Name ?? "-"}",
            $"Original file: {project.OriginalFileName}",
            $"Processed: {project.CompletedAtUtc?.ToString("O") ?? "-"}",
            "",
        };

        if (segments.Length == 0)
        {
            lines.AddRange((project.Transcript?.PlainText ?? string.Empty)
                .Split('\n', StringSplitOptions.TrimEntries)
                .Select(line => line.Replace("\r", string.Empty)));
            return lines;
        }

        foreach (var segment in segments)
        {
            var prefix = string.Empty;
            if (showTimestamps)
                prefix += $"[{FormatTime(segment.StartMs)}] ";
            if (!string.IsNullOrWhiteSpace(segment.Speaker))
                prefix += $"{segment.Speaker}: ";

            lines.Add(prefix + segment.Text);
        }

        return lines;
    }

    private static byte[] BuildPdfDocument(IReadOnlyList<string> lines)
    {
        const int maxCharsPerLine = 92;
        const int linesPerPage = 48;

        var wrappedLines = lines
            .SelectMany(line => WrapLine(line, maxCharsPerLine))
            .DefaultIfEmpty(string.Empty)
            .ToArray();

        var pageCount = (int)Math.Ceiling(wrappedLines.Length / (double)linesPerPage);
        var objectCount = 3 + (pageCount * 2);
        var fontObjectNumber = objectCount;
        var offsets = new long[objectCount + 1];

        using var stream = new MemoryStream();
        WriteAscii(stream, "%PDF-1.4\n");

        WriteObject(stream, offsets, 1, () =>
            WriteAscii(stream, "<< /Type /Catalog /Pages 2 0 R >>\n"));

        WriteObject(stream, offsets, 2, () =>
        {
            var kids = Enumerable.Range(0, pageCount)
                .Select(index => $"{3 + (index * 2)} 0 R");
            WriteAscii(stream, $"<< /Type /Pages /Kids [{string.Join(" ", kids)}] /Count {pageCount} >>\n");
        });

        for (var pageIndex = 0; pageIndex < pageCount; pageIndex += 1)
        {
            var pageObjectNumber = 3 + (pageIndex * 2);
            var contentObjectNumber = pageObjectNumber + 1;
            var pageLines = wrappedLines
                .Skip(pageIndex * linesPerPage)
                .Take(linesPerPage)
                .ToArray();
            var content = BuildPdfContentStream(pageLines);

            WriteObject(stream, offsets, pageObjectNumber, () =>
            {
                WriteAscii(
                    stream,
                    $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 {fontObjectNumber} 0 R >> >> /Contents {contentObjectNumber} 0 R >>\n");
            });

            WriteObject(stream, offsets, contentObjectNumber, () =>
            {
                WriteAscii(stream, $"<< /Length {content.Length} >>\nstream\n");
                stream.Write(content, 0, content.Length);
                WriteAscii(stream, "\nendstream\n");
            });
        }

        WriteObject(stream, offsets, fontObjectNumber, () =>
            WriteAscii(stream, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\n"));

        var xrefOffset = stream.Position;
        WriteAscii(stream, $"xref\n0 {objectCount + 1}\n");
        WriteAscii(stream, "0000000000 65535 f \n");

        for (var objectNumber = 1; objectNumber <= objectCount; objectNumber += 1)
            WriteAscii(stream, $"{offsets[objectNumber]:D10} 00000 n \n");

        WriteAscii(stream, $"trailer\n<< /Size {objectCount + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF");
        return stream.ToArray();
    }

    private static byte[] BuildPdfContentStream(IEnumerable<string> lines)
    {
        using var stream = new MemoryStream();
        WriteAscii(stream, "BT\n/F1 10 Tf\n14 TL\n50 742 Td\n");

        foreach (var line in lines)
            WriteAscii(stream, $"({EscapePdfText(line)}) Tj\nT*\n");

        WriteAscii(stream, "ET\n");
        return stream.ToArray();
    }

    private static IEnumerable<string> WrapLine(string line, int width)
    {
        if (string.IsNullOrEmpty(line))
            return [string.Empty];

        var wrapped = new List<string>();
        var remaining = line.TrimEnd();

        while (remaining.Length > width)
        {
            var breakIndex = remaining.LastIndexOf(' ', width);
            if (breakIndex <= 0)
                breakIndex = width;

            wrapped.Add(remaining[..breakIndex].TrimEnd());
            remaining = remaining[breakIndex..].TrimStart();
        }

        wrapped.Add(remaining);
        return wrapped;
    }

    private static string EscapePdfText(string value)
    {
        var builder = new StringBuilder(value.Length);

        foreach (var ch in value)
        {
            switch (ch)
            {
                case '\\':
                    builder.Append(@"\\");
                    break;
                case '(':
                    builder.Append(@"\(");
                    break;
                case ')':
                    builder.Append(@"\)");
                    break;
                case '\r':
                case '\n':
                    builder.Append(' ');
                    break;
                default:
                    builder.Append(ch <= 0xFF ? ch : '?');
                    break;
            }
        }

        return builder.ToString();
    }

    private static void WriteObject(Stream stream, long[] offsets, int objectNumber, Action writeBody)
    {
        offsets[objectNumber] = stream.Position;
        WriteAscii(stream, $"{objectNumber} 0 obj\n");
        writeBody();
        WriteAscii(stream, "endobj\n");
    }

    private static void WriteAscii(Stream stream, string value)
    {
        foreach (var ch in value)
            stream.WriteByte(ch <= 0xFF ? (byte)ch : (byte)'?');
    }
}
