using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClassTranscriber.Api.Contracts;
using ClassTranscriber.Api.Domain;
using ClassTranscriber.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClassTranscriber.Api.Mcp;

public sealed class ChatGptSourceContentService(
    AppDbContext db,
    Uri? applicationBaseUrl = null,
    string? cursorIntegrityKey = null)
{
    public const string SearchSemantics =
        "Literal SQLite substring search. Case-insensitivity is ASCII-oriented; full Unicode case folding and semantic search are not provided.";

    private const int MaximumExcerptCharacters = 500;
    private const int MaximumOccurrences = 3;
    private const int CursorVersion = 1;
    private const string SegmentMode = "segments";
    private const string PlainTextMode = "plainText";
    private static readonly byte[] CursorDomain = Encoding.UTF8.GetBytes("TranscriptLab.ChatGptSource.Cursor.v1\0");
    private readonly byte[] cursorIntegrityKeyBytes = Encoding.UTF8.GetBytes(cursorIntegrityKey ?? string.Empty);
    private static readonly JsonSerializerOptions SegmentJsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task<ContentQueryResult<TranscriptSearchPage>> SearchAsync(
        string query,
        Guid? folderId = null,
        int offset = 0,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var trimmedQuery = query?.Trim();
        if (trimmedQuery is null || trimmedQuery.Length is < 2 or > 200 ||
            offset < 0 || limit is < 1 or > 20 || folderId == Guid.Empty)
        {
            return Failure<TranscriptSearchPage>(ContentErrorCodes.ValidationError, "The search request is invalid.");
        }

        var likePattern = $"%{EscapeLikePattern(trimmedQuery)}%";
        var projects = db.Projects
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

        var candidates = await projects.ToListAsync(cancellationToken);
        var hasMore = candidates.Count > limit;
        var matches = new List<TranscriptSearchMatch>(Math.Min(candidates.Count, limit));
        foreach (var candidate in candidates.Take(limit))
        {
            cancellationToken.ThrowIfCancellationRequested();
            matches.Add(BuildSearchMatch(candidate, trimmedQuery));
        }

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
        {
            return Failure<TranscriptContentPage>(ContentErrorCodes.ValidationError, "The transcript request is invalid.");
        }

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
        var segmentState = DeserializeSegments(candidate.StructuredSegmentsJson);
        if (segmentState.Status == SegmentJsonStatus.Invalid)
            return Failure<TranscriptContentPage>(ContentErrorCodes.CorruptTranscript, "The structured transcript is unavailable.");

        var segments = segmentState.Segments;
        var mode = segments.Count == 0 ? PlainTextMode : SegmentMode;
        var transcriptVersion = candidate.TranscriptUpdatedAtUtc.Value.Ticks;
        CursorPayload position;
        if (cursor is null)
        {
            position = new CursorPayload(CursorVersion, mode, projectId, transcriptVersion, 0, 0);
        }
        else if (!TryDecodeCursor(cursor, out position) ||
                 position.Version != CursorVersion || position.Mode != mode || position.ProjectId != projectId ||
                 position.TranscriptVersion != transcriptVersion || position.SegmentIndex < 0 || position.CharacterOffset < 0)
        {
            return Failure<TranscriptContentPage>(ContentErrorCodes.ValidationError, "The transcript cursor is invalid.");
        }

        var page = mode == SegmentMode
            ? BuildSegmentPage(candidate, segments, position, segmentLimit, characterLimit)
            : BuildPlainTextPage(candidate, position, characterLimit);

        if (page is null)
            return Failure<TranscriptContentPage>(ContentErrorCodes.ValidationError, "The transcript cursor is invalid.");

        return ContentQueryResult<TranscriptContentPage>.Success(page);
    }

    private TranscriptSearchMatch BuildSearchMatch(SearchCandidate candidate, string query)
    {
        var segmentState = DeserializeSegments(candidate.StructuredSegmentsJson);
        var occurrences = new List<TranscriptSearchOccurrence>(MaximumOccurrences);
        if (segmentState.Status == SegmentJsonStatus.Valid)
        {
            for (var segmentIndex = 0; segmentIndex < segmentState.Segments.Count && occurrences.Count < MaximumOccurrences; segmentIndex++)
            {
                var segment = segmentState.Segments[segmentIndex];
                foreach (var matchIndex in FindOccurrences(segment.Text, query, MaximumOccurrences - occurrences.Count))
                {
                    occurrences.Add(CreateOccurrence(segment.Text, query.Length, matchIndex, segmentIndex, segment));
                }
            }
        }

        var plainTextFallback = occurrences.Count == 0;
        if (plainTextFallback)
        {
            var matchIndex = candidate.PlainText.IndexOf(query, StringComparison.OrdinalIgnoreCase);
            if (matchIndex < 0)
                matchIndex = candidate.PlainText.IndexOf(query, StringComparison.Ordinal);
            if (matchIndex >= 0)
                occurrences.Add(CreateOccurrence(candidate.PlainText, query.Length, matchIndex, null, null));
        }

        return new TranscriptSearchMatch
        {
            Project = CreateProject(candidate),
            Occurrences = occurrences,
            Warnings = new TranscriptSearchWarnings
            {
                PlainTextFallback = plainTextFallback,
                StructuredSegmentsAbsent = segmentState.Status == SegmentJsonStatus.Absent,
                StructuredSegmentsEmpty = segmentState.Status == SegmentJsonStatus.Empty,
                StructuredSegmentsInvalid = segmentState.Status == SegmentJsonStatus.Invalid,
            },
        };
    }

    private TranscriptContentPage? BuildSegmentPage(
        RetrievalCandidate candidate,
        IReadOnlyList<TranscriptSegmentDto> segments,
        CursorPayload position,
        int segmentLimit,
        int characterLimit)
    {
        if (position.SegmentIndex > segments.Count ||
            (position.SegmentIndex == segments.Count && position.CharacterOffset != 0))
            return null;

        var chunks = new List<TranscriptChunk>(segmentLimit);
        var segmentIndex = position.SegmentIndex;
        var characterOffset = position.CharacterOffset;
        var remainingCharacters = characterLimit;
        while (segmentIndex < segments.Count && chunks.Count < segmentLimit && remainingCharacters > 0)
        {
            var segment = segments[segmentIndex];
            if (characterOffset > segment.Text.Length)
                return null;

            var take = Math.Min(segment.Text.Length - characterOffset, remainingCharacters);
            var complete = characterOffset + take == segment.Text.Length;
            chunks.Add(new TranscriptChunk
            {
                SegmentIndex = segmentIndex,
                StartMs = segment.StartMs,
                EndMs = segment.EndMs,
                Speaker = segment.Speaker,
                Text = segment.Text.Substring(characterOffset, take),
                TextStartCharacter = characterOffset,
                TextComplete = complete,
            });
            remainingCharacters -= take;
            if (complete)
            {
                segmentIndex++;
                characterOffset = 0;
            }
            else
            {
                characterOffset += take;
            }
        }

        var hasMore = segmentIndex < segments.Count;
        return new TranscriptContentPage
        {
            Project = CreateProject(candidate),
            Chunks = chunks,
            HasMore = hasMore,
            NextCursor = hasMore
                ? EncodeCursor(new CursorPayload(
                    CursorVersion,
                    SegmentMode,
                    candidate.ProjectId,
                    candidate.TranscriptUpdatedAtUtc!.Value.Ticks,
                    segmentIndex,
                    characterOffset))
                : null,
        };
    }

    private TranscriptContentPage? BuildPlainTextPage(
        RetrievalCandidate candidate,
        CursorPayload position,
        int characterLimit)
    {
        var plainText = candidate.PlainText!;
        if (position.SegmentIndex != 0 || position.CharacterOffset > plainText.Length)
            return null;

        var take = Math.Min(plainText.Length - position.CharacterOffset, characterLimit);
        var complete = position.CharacterOffset + take == plainText.Length;
        IReadOnlyList<TranscriptChunk> chunks = take == 0
            ? []
            :
            [
                new TranscriptChunk
                {
                    SegmentIndex = null,
                    Text = plainText.Substring(position.CharacterOffset, take),
                    TextStartCharacter = position.CharacterOffset,
                    TextComplete = complete,
                },
            ];

        return new TranscriptContentPage
        {
            Project = CreateProject(candidate),
            Chunks = chunks,
            HasMore = !complete,
            NextCursor = complete
                ? null
                : EncodeCursor(new CursorPayload(
                    CursorVersion,
                    PlainTextMode,
                    candidate.ProjectId,
                    candidate.TranscriptUpdatedAtUtc!.Value.Ticks,
                    0,
                    position.CharacterOffset + take)),
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

    private static TranscriptSearchOccurrence CreateOccurrence(
        string text,
        int queryLength,
        int matchIndex,
        int? segmentIndex,
        TranscriptSegmentDto? segment)
    {
        var preferredPrefix = Math.Max(0, (MaximumExcerptCharacters - queryLength) / 2);
        var excerptStart = Math.Max(0, matchIndex - preferredPrefix);
        var excerptLength = Math.Min(MaximumExcerptCharacters, text.Length - excerptStart);
        if (excerptLength < MaximumExcerptCharacters && excerptStart > 0)
        {
            excerptStart = Math.Max(0, text.Length - MaximumExcerptCharacters);
            excerptLength = text.Length - excerptStart;
        }

        return new TranscriptSearchOccurrence
        {
            SegmentIndex = segmentIndex,
            StartMs = segment?.StartMs,
            EndMs = segment?.EndMs,
            Speaker = segment?.Speaker,
            Excerpt = text.Substring(excerptStart, excerptLength),
            ExcerptTruncated = excerptStart > 0 || excerptStart + excerptLength < text.Length,
        };
    }

    private static IEnumerable<int> FindOccurrences(string text, string query, int limit)
    {
        var start = 0;
        for (var found = 0; found < limit && start <= text.Length - query.Length; found++)
        {
            var index = text.IndexOf(query, start, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                yield break;
            yield return index;
            start = index + query.Length;
        }
    }

    private static SegmentJsonState DeserializeSegments(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new SegmentJsonState(SegmentJsonStatus.Absent, []);

        try
        {
            var segments = JsonSerializer.Deserialize<TranscriptSegmentDto[]>(json, SegmentJsonOptions);
            if (segments is null)
                return new SegmentJsonState(SegmentJsonStatus.Absent, []);
            if (segments.Any(segment => segment.Text is null))
                return new SegmentJsonState(SegmentJsonStatus.Invalid, []);
            return segments.Length == 0
                ? new SegmentJsonState(SegmentJsonStatus.Empty, [])
                : new SegmentJsonState(SegmentJsonStatus.Valid, segments);
        }
        catch (JsonException)
        {
            return new SegmentJsonState(SegmentJsonStatus.Invalid, []);
        }
    }

    private static string EscapeLikePattern(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("%", "\\%", StringComparison.Ordinal)
        .Replace("_", "\\_", StringComparison.Ordinal);

    private string EncodeCursor(CursorPayload payload)
    {
        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        var integrity = ComputeCursorIntegrity(payloadBytes);
        var cursorBytes = new byte[payloadBytes.Length + integrity.Length];
        payloadBytes.CopyTo(cursorBytes, 0);
        integrity.CopyTo(cursorBytes, payloadBytes.Length);
        return Convert.ToBase64String(cursorBytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private bool TryDecodeCursor(string cursor, out CursorPayload payload)
    {
        payload = default!;
        if (cursor.Length is 0 or > 2048 || cursor.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
            return false;

        try
        {
            var base64 = cursor.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + ((4 - base64.Length % 4) % 4), '=');
            var cursorBytes = Convert.FromBase64String(base64);
            var canonicalCursor = Convert.ToBase64String(cursorBytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            if (!string.Equals(cursor, canonicalCursor, StringComparison.Ordinal))
                return false;
            if (cursorBytes.Length <= SHA256.HashSizeInBytes)
                return false;

            var payloadBytes = cursorBytes.AsSpan(0, cursorBytes.Length - SHA256.HashSizeInBytes);
            var suppliedIntegrity = cursorBytes.AsSpan(cursorBytes.Length - SHA256.HashSizeInBytes);
            var expectedIntegrity = ComputeCursorIntegrity(payloadBytes);
            if (!CryptographicOperations.FixedTimeEquals(suppliedIntegrity, expectedIntegrity))
                return false;

            payload = JsonSerializer.Deserialize<CursorPayload>(payloadBytes)!;
            return payload is not null;
        }
        catch (Exception exception) when (exception is FormatException or JsonException or NotSupportedException)
        {
            return false;
        }
    }

    private byte[] ComputeCursorIntegrity(ReadOnlySpan<byte> payload)
    {
        using var hash = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, cursorIntegrityKeyBytes);
        hash.AppendData(CursorDomain);
        hash.AppendData(payload);
        return hash.GetHashAndReset();
    }

    private static ContentQueryResult<T> Failure<T>(string code, string message) where T : class =>
        ContentQueryResult<T>.Failure(code, message);

    private sealed record SearchCandidate(
        Guid FolderId,
        string FolderName,
        Guid ProjectId,
        string ProjectName,
        string OriginalFileName,
        string? DetectedLanguage,
        long? DurationMs,
        int SegmentCount,
        DateTime? CompletedAtUtc,
        DateTime TranscriptUpdatedAtUtc,
        string PlainText,
        string StructuredSegmentsJson);

    private sealed record RetrievalCandidate(
        Guid FolderId,
        string FolderName,
        Guid ProjectId,
        string ProjectName,
        string OriginalFileName,
        ProjectStatus Status,
        DateTime? CompletedAtUtc,
        string? DetectedLanguage,
        long? DurationMs,
        int SegmentCount,
        DateTime? TranscriptUpdatedAtUtc,
        string? PlainText,
        string? StructuredSegmentsJson);

    private sealed record CursorPayload(
        int Version,
        string Mode,
        Guid ProjectId,
        long TranscriptVersion,
        int SegmentIndex,
        int CharacterOffset);

    private sealed record SegmentJsonState(SegmentJsonStatus Status, IReadOnlyList<TranscriptSegmentDto> Segments);

    private enum SegmentJsonStatus
    {
        Valid,
        Absent,
        Empty,
        Invalid,
    }
}
