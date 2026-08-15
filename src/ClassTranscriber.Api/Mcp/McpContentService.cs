using ClassTranscriber.Api.Contracts;
using ClassTranscriber.Api.Domain;
using ClassTranscriber.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClassTranscriber.Api.Mcp;

public sealed class McpContentService
{
    public const string SearchSemantics =
        "Literal SQLite substring search. Case-insensitivity is ASCII-oriented; full Unicode case folding and semantic search are not provided.";

    private const int CursorVersion = 1;
    private const string SegmentMode = "segments";
    private const string PlainTextMode = "plainText";
    private readonly AppDbContext db;
    private readonly Uri? applicationBaseUrl;
    private readonly TranscriptCursorCodec cursorCodec;

    public McpContentService(
        AppDbContext db,
        Uri? applicationBaseUrl = null,
        string? cursorIntegrityKey = null)
    {
        this.db = db;
        this.applicationBaseUrl = applicationBaseUrl;
        cursorCodec = new TranscriptCursorCodec(cursorIntegrityKey);
    }

    public async Task<ContentQueryResult<TranscriptSearchPage>> SearchAsync(
        string query,
        Guid? folderId = null,
        int offset = 0,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var trimmedQuery = query?.Trim();
        if (trimmedQuery is null || trimmedQuery.Length is < 2 or > 200 || trimmedQuery.Contains('\0') ||
            !TranscriptOccurrenceMatcher.IsWellFormedUtf16(trimmedQuery) ||
            offset < 0 || limit is < 1 or > 20 || folderId == Guid.Empty)
        {
            return Failure<TranscriptSearchPage>(ContentErrorCodes.ValidationError, "The search request is invalid.");
        }

        var likePattern = $"%{EscapeLikePattern(trimmedQuery)}%";
        var queryable = db.Projects
            .AsNoTracking()
            .Where(project => project.Status == ProjectStatus.Completed && project.Transcript != null)
            .Where(project => folderId == null || project.FolderId == folderId)
            .Where(project => EF.Functions.Like(project.Transcript!.PlainText, likePattern, "\\"))
            .OrderByDescending(project => project.Transcript!.UpdatedAtUtc)
            .ThenBy(project => project.Id)
            .Skip(offset)
            .Take(limit + 1)
            .Select(project => new SearchCandidate(
                project.FolderId,
                project.Folder.Name,
                project.Id,
                project.Name,
                project.OriginalFileName,
                project.Transcript!.DetectedLanguage,
                project.Transcript.DurationMs,
                project.Transcript.SegmentCount,
                project.CompletedAtUtc,
                project.Transcript.UpdatedAtUtc,
                project.Transcript.PlainText,
                project.Transcript.StructuredSegmentsJson));

        var candidates = await queryable.ToListAsync(cancellationToken);
        var hasMore = candidates.Count > limit;
        var matches = candidates.Take(limit)
            .Select(candidate => CreateSearchMatch(candidate, trimmedQuery))
            .ToArray();
        return ContentQueryResult<TranscriptSearchPage>.Success(new TranscriptSearchPage
        {
            Matches = matches,
            Offset = offset,
            Limit = limit,
            HasMore = hasMore,
            NextOffset = hasMore ? offset + limit : null,
            SearchSemantics = SearchSemantics,
        });
    }

    public async Task<ContentQueryResult<TranscriptContentPage>> GetTranscriptAsync(
        Guid projectId,
        string? cursor = null,
        int segmentLimit = 100,
        int characterLimit = 12_000,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (projectId == Guid.Empty || segmentLimit is < 1 or > 200 || characterLimit is < 1_000 or > 20_000)
            return Failure<TranscriptContentPage>(ContentErrorCodes.ValidationError, "The transcript request is invalid.");

        var candidate = await db.Projects
            .AsNoTracking()
            .Where(project => project.Id == projectId)
            .Select(project => new RetrievalCandidate(
                project.FolderId,
                project.Folder.Name,
                project.Id,
                project.Name,
                project.OriginalFileName,
                project.Status,
                project.CompletedAtUtc,
                project.Transcript == null ? null : project.Transcript.DetectedLanguage,
                project.Transcript == null ? null : project.Transcript.DurationMs,
                project.Transcript == null ? 0 : project.Transcript.SegmentCount,
                project.Transcript == null ? null : project.Transcript.UpdatedAtUtc,
                project.Transcript == null ? null : project.Transcript.PlainText,
                project.Transcript == null ? null : project.Transcript.StructuredSegmentsJson))
            .SingleOrDefaultAsync(cancellationToken);

        if (candidate is null)
            return Failure<TranscriptContentPage>(ContentErrorCodes.NotFound, "The requested project was not found.");
        if (candidate.Status != ProjectStatus.Completed || candidate.TranscriptUpdatedAtUtc is null ||
            candidate.PlainText is null || candidate.StructuredSegmentsJson is null)
        {
            return Failure<TranscriptContentPage>(ContentErrorCodes.TranscriptNotReady, "The transcript is not ready.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var segmentState = TranscriptOccurrenceMatcher.DeserializeSegments(candidate.StructuredSegmentsJson);
        if (segmentState.Status == SegmentJsonStatus.Invalid)
            return Failure<TranscriptContentPage>(ContentErrorCodes.CorruptTranscript, "The structured transcript is unavailable.");

        var mode = segmentState.Segments.Count == 0 ? PlainTextMode : SegmentMode;
        var position = DecodePosition(cursor, projectId, candidate.TranscriptUpdatedAtUtc.Value.Ticks, mode);
        if (position is null)
            return Failure<TranscriptContentPage>(ContentErrorCodes.ValidationError, "The transcript cursor is invalid.");

        var project = CreateProject(candidate);
        var page = mode == SegmentMode
            ? TranscriptContentPageBuilder.BuildSegmentPage(
                project, segmentState.Segments, position, segmentLimit, characterLimit, cursorCodec)
            : TranscriptContentPageBuilder.BuildPlainTextPage(
                project, candidate.PlainText, position, characterLimit, cursorCodec);
        return page is null
            ? Failure<TranscriptContentPage>(ContentErrorCodes.ValidationError, "The transcript cursor is invalid.")
            : ContentQueryResult<TranscriptContentPage>.Success(page);
    }

    private TranscriptCursorPayload? DecodePosition(string? cursor, Guid projectId, long version, string mode)
    {
        if (cursor is null)
            return new TranscriptCursorPayload(CursorVersion, mode, projectId, version, 0, 0);
        if (!cursorCodec.TryDecode(cursor, out var position) || position.Version != CursorVersion ||
            position.Mode != mode || position.ProjectId != projectId || position.TranscriptVersion != version ||
            position.SegmentIndex < 0 || position.CharacterOffset < 0)
        {
            return null;
        }

        return position;
    }

    private TranscriptSearchMatch CreateSearchMatch(SearchCandidate candidate, string query)
    {
        var occurrenceResult = TranscriptOccurrenceMatcher.Match(
            candidate.PlainText,
            candidate.StructuredSegmentsJson,
            query);
        return new TranscriptSearchMatch
        {
            Project = CreateProject(candidate),
            Occurrences = occurrenceResult.Occurrences,
            Warnings = occurrenceResult.Warnings,
        };
    }

    private TranscriptSourceProject CreateProject(SearchCandidate candidate) => new()
    {
        FolderId = candidate.FolderId,
        FolderName = candidate.FolderName,
        ProjectId = candidate.ProjectId,
        ProjectName = candidate.ProjectName,
        OriginalFileName = candidate.OriginalFileName,
        DetectedLanguage = candidate.DetectedLanguage,
        DurationMs = candidate.DurationMs,
        SegmentCount = candidate.SegmentCount,
        CompletedAtUtc = candidate.CompletedAtUtc,
        TranscriptUpdatedAtUtc = candidate.TranscriptUpdatedAtUtc,
        SourcePath = SourcePath(candidate.ProjectId),
        SourceUrl = SourceUrl(candidate.ProjectId),
    };

    private TranscriptSourceProject CreateProject(RetrievalCandidate candidate) => new()
    {
        FolderId = candidate.FolderId,
        FolderName = candidate.FolderName,
        ProjectId = candidate.ProjectId,
        ProjectName = candidate.ProjectName,
        OriginalFileName = candidate.OriginalFileName,
        DetectedLanguage = candidate.DetectedLanguage,
        DurationMs = candidate.DurationMs,
        SegmentCount = candidate.SegmentCount,
        CompletedAtUtc = candidate.CompletedAtUtc,
        TranscriptUpdatedAtUtc = candidate.TranscriptUpdatedAtUtc!.Value,
        SourcePath = SourcePath(candidate.ProjectId),
        SourceUrl = SourceUrl(candidate.ProjectId),
    };

    private string? SourceUrl(Guid projectId) => applicationBaseUrl is null
        ? null
        : new Uri(applicationBaseUrl, $"projects/{projectId}").AbsoluteUri;

    private static string SourcePath(Guid projectId) => $"/projects/{projectId}";

    private static string EscapeLikePattern(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("%", "\\%", StringComparison.Ordinal)
        .Replace("_", "\\_", StringComparison.Ordinal);

    private static ContentQueryResult<T> Failure<T>(string code, string message) where T : class =>
        ContentQueryResult<T>.Failure(code, message);

    private sealed record SearchCandidate(
        Guid FolderId, string FolderName, Guid ProjectId, string ProjectName, string OriginalFileName,
        string? DetectedLanguage, long? DurationMs, int SegmentCount, DateTime? CompletedAtUtc,
        DateTime TranscriptUpdatedAtUtc, string PlainText, string StructuredSegmentsJson);

    private sealed record RetrievalCandidate(
        Guid FolderId, string FolderName, Guid ProjectId, string ProjectName, string OriginalFileName,
        ProjectStatus Status, DateTime? CompletedAtUtc, string? DetectedLanguage, long? DurationMs,
        int SegmentCount, DateTime? TranscriptUpdatedAtUtc, string? PlainText, string? StructuredSegmentsJson);
}
