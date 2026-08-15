using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using ClassTranscriber.Api.Contracts;
using ClassTranscriber.Api.Domain;
using ClassTranscriber.Api.Mcp;
using ClassTranscriber.Api.Persistence;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ClassTranscriber.Api.Tests;

public sealed class McpContentServiceTests
{
    [Theory]
    [InlineData("100%", "literal-percent")]
    [InlineData("under_score", "literal-underscore")]
    [InlineData("slash\\mark", "literal-escape")]
    public async Task SearchAsync_TreatsSqlLikeSpecialCharactersLiterally(string query, string expectedName)
    {
        await using var fixture = await ContentFixture.CreateAsync();
        await fixture.SeedAsync(name: expectedName, plainText: query, segments: [Segment(query)]);
        await fixture.SeedAsync(name: "wildcard-decoy", plainText: "100X underZscore slashXmark", segments: [Segment("decoy")]);

        var result = await fixture.Service.SearchAsync(query);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Matches.Should().ContainSingle().Which.ProjectName.Should().Be(expectedName);
    }

    [Fact]
    public async Task SearchAsync_UsesAsciiOrientedSqliteCaseInsensitivity()
    {
        await using var fixture = await ContentFixture.CreateAsync();
        await fixture.SeedAsync(name: "ascii", plainText: "NEEDLE CAFÉ", segments: [Segment("NEEDLE CAFÉ")]);

        (await fixture.Service.SearchAsync("needle")).Value!.Matches.Should().ContainSingle();
        (await fixture.Service.SearchAsync("CAFÉ")).Value!.Matches.Should().ContainSingle();
        (await fixture.Service.SearchAsync("café")).Value!.Matches.Should().BeEmpty();
        (await fixture.Service.SearchAsync("needle")).Value!.SearchSemantics.Should().Contain("ASCII");
    }

    [Fact]
    public async Task SearchAsync_UsesTheSameAsciiOnlyLiteralSemanticsForCandidatesAndOccurrences()
    {
        await using var fixture = await ContentFixture.CreateAsync();
        await fixture.SeedAsync(
            name: "unicode-case",
            plainText: "café … CAFÉ",
            segments: [Segment("café … CAFÉ")]);

        var lower = await fixture.Service.SearchAsync("café");
        var upper = await fixture.Service.SearchAsync("CAFÉ");

        lower.Value!.Matches.Should().ContainSingle();
        lower.Value.Matches.Single().Occurrences.Should().ContainSingle()
            .Which.Excerpt.Should().Contain("café");
        upper.Value!.Matches.Should().ContainSingle();
        upper.Value.Matches.Single().Occurrences.Should().ContainSingle()
            .Which.Excerpt.Should().Contain("CAFÉ");
    }

    [Fact]
    public async Task SearchAsync_FoldsAsciiLettersWithoutNormalizingUnicode()
    {
        await using var fixture = await ContentFixture.CreateAsync();
        const string composed = "café";
        const string decomposed = "CAFE\u0301";
        await fixture.SeedAsync(
            name: "normalization",
            plainText: $"{composed} / {decomposed}",
            segments: [Segment($"{composed} / {decomposed}")]);

        var composedResult = await fixture.Service.SearchAsync("café");
        var decomposedResult = await fixture.Service.SearchAsync("cafe\u0301");

        composedResult.Value!.Matches.Single().Occurrences.Should().ContainSingle();
        decomposedResult.Value!.Matches.Single().Occurrences.Should().ContainSingle();
    }

    [Fact]
    public async Task SearchAsync_FallsBackToPlainTextWhenStructuredTextDiffersByNonAsciiCase()
    {
        await using var fixture = await ContentFixture.CreateAsync();
        await fixture.SeedAsync(plainText: "plain café", segments: [Segment("structured CAFÉ")]);

        var match = (await fixture.Service.SearchAsync("café")).Value!.Matches.Single();

        match.Occurrences.Should().ContainSingle().Which.SegmentIndex.Should().BeNull();
        match.Warnings.PlainTextFallback.Should().BeTrue();
    }

    [Fact]
    public async Task SearchAsync_RejectsNulInQuery()
    {
        await using var fixture = await ContentFixture.CreateAsync();

        var result = await fixture.Service.SearchAsync("ab\0cd");

        result.Error!.Code.Should().Be(ContentErrorCodes.ValidationError);
        result.Error.Message.Should().Be("The search request is invalid.");
    }

    [Fact]
    public async Task SearchAsync_ConnectionKeepsSqliteAsciiLikeInvariant()
    {
        await using var fixture = await ContentFixture.CreateAsync();
        await using var command = fixture.Db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT 'A' LIKE 'a' = 1";

        var result = await command.ExecuteScalarAsync();

        Convert.ToInt64(result).Should().Be(1);
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("a")]
    public async Task SearchAsync_RejectsBlankOrShortTrimmedQueries(string query)
    {
        await using var fixture = await ContentFixture.CreateAsync();

        var result = await fixture.Service.SearchAsync(query);

        result.Error!.Code.Should().Be(ContentErrorCodes.ValidationError);
        result.Error.Message.Should().Be("The search request is invalid.");
    }

    [Fact]
    public async Task SearchAsync_RejectsQueriesLongerThanTwoHundredCharacters()
    {
        await using var fixture = await ContentFixture.CreateAsync();

        var result = await fixture.Service.SearchAsync(new string('q', 201));

        result.Error!.Code.Should().Be(ContentErrorCodes.ValidationError);
    }

    [Fact]
    public async Task SearchAsync_FiltersFolderAndCompletedTranscriptReadyProjects()
    {
        await using var fixture = await ContentFixture.CreateAsync();
        var includedFolder = Guid.NewGuid();
        var otherFolder = Guid.NewGuid();
        await fixture.SeedAsync(folderId: includedFolder, name: "included", plainText: "needle", segments: [Segment("needle")]);
        await fixture.SeedAsync(folderId: otherFolder, name: "other-folder", plainText: "needle", segments: [Segment("needle")]);
        await fixture.SeedAsync(folderId: includedFolder, name: "draft", status: ProjectStatus.Draft, plainText: "needle", segments: [Segment("needle")]);
        await fixture.SeedAsync(folderId: includedFolder, name: "no-transcript", includeTranscript: false);

        var result = await fixture.Service.SearchAsync("needle", includedFolder);

        result.Value!.Matches.Should().ContainSingle().Which.ProjectName.Should().Be("included");
        (await fixture.Service.SearchAsync("missing", includedFolder)).Value!.Matches.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_PagesByTranscriptUpdateDescendingThenProjectId()
    {
        await using var fixture = await ContentFixture.CreateAsync();
        var updated = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);
        await fixture.SeedAsync(projectId: Guid.Parse("00000000-0000-0000-0000-000000000002"), name: "second-guid", transcriptUpdatedAtUtc: updated, plainText: "needle", segments: [Segment("needle")]);
        await fixture.SeedAsync(projectId: Guid.Parse("00000000-0000-0000-0000-000000000001"), name: "first-guid", transcriptUpdatedAtUtc: updated, plainText: "needle", segments: [Segment("needle")]);
        await fixture.SeedAsync(name: "newest", transcriptUpdatedAtUtc: updated.AddMinutes(1), plainText: "needle", segments: [Segment("needle")]);

        var first = await fixture.Service.SearchAsync("needle", offset: 0, limit: 2);
        var second = await fixture.Service.SearchAsync("needle", offset: 2, limit: 2);

        first.Value!.Matches.Select(match => match.ProjectName).Should().Equal("newest", "first-guid");
        first.Value.HasMore.Should().BeTrue();
        first.Value.NextOffset.Should().Be(2);
        second.Value!.Matches.Select(match => match.ProjectName).Should().Equal("second-guid");
        second.Value.HasMore.Should().BeFalse();
        second.Value.NextOffset.Should().BeNull();
    }

    [Fact]
    public async Task SearchAsync_CapsOccurrencesAndExcerptLength()
    {
        await using var fixture = await ContentFixture.CreateAsync();
        var text = string.Join(" ", Enumerable.Repeat(new string('x', 260) + " needle " + new string('y', 260), 4));
        await fixture.SeedAsync(plainText: text, segments: [Segment(text)]);

        var match = (await fixture.Service.SearchAsync("needle")).Value!.Matches.Single();

        match.Occurrences.Should().HaveCount(3);
        match.Occurrences.Should().OnlyContain(occurrence => occurrence.Excerpt.Length <= 500);
        match.Occurrences.Should().OnlyContain(occurrence => occurrence.ExcerptTruncated);
    }

    [Fact]
    public async Task SearchAsync_ExcerptBoundariesNeverSplitEmojiSurrogatePairs()
    {
        await using var fixture = await ContentFixture.CreateAsync();
        var splitsAtStart = "🙂" + new string('x', 246) + "needle" + new string('y', 500);
        var splitsAtEnd = "needle" + new string('z', 493) + "🙂" + new string('w', 100);
        await fixture.SeedAsync(
            plainText: $"{splitsAtStart} {splitsAtEnd}",
            segments: [Segment(splitsAtStart), Segment(splitsAtEnd)]);

        var occurrences = (await fixture.Service.SearchAsync("needle")).Value!.Matches.Single().Occurrences;

        occurrences.Should().HaveCount(2);
        occurrences.Should().OnlyContain(occurrence => HasWellFormedBoundaries(occurrence.Excerpt));
        occurrences.Should().OnlyContain(occurrence => occurrence.Excerpt.Contains("needle", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SearchAsync_FallsBackWhenOnlyPlainTextContainsTheMatch()
    {
        await using var fixture = await ContentFixture.CreateAsync();
        await fixture.SeedAsync(plainText: "plain needle only", segments: [Segment("different structured text")]);

        var match = (await fixture.Service.SearchAsync("NEEDLE")).Value!.Matches.Single();

        match.Occurrences.Should().ContainSingle().Which.SegmentIndex.Should().BeNull();
        match.Warnings.PlainTextFallback.Should().BeTrue();
        match.Warnings.StructuredSegmentsInvalid.Should().BeFalse();
    }

    [Theory]
    [InlineData("[]", false, true, false)]
    [InlineData(" ", true, false, false)]
    [InlineData("{broken", false, false, true)]
    public async Task SearchAsync_ReturnsBoundedPlainTextFallbackAndWarnings(
        string structuredJson,
        bool absent,
        bool empty,
        bool invalid)
    {
        await using var fixture = await ContentFixture.CreateAsync();
        var secretTail = new string('z', 2000);
        await fixture.SeedAsync(plainText: $"prefix needle {secretTail}", structuredJson: structuredJson);

        var match = (await fixture.Service.SearchAsync("needle")).Value!.Matches.Single();

        match.Occurrences.Should().ContainSingle();
        match.Occurrences[0].SegmentIndex.Should().BeNull();
        match.Occurrences[0].Excerpt.Length.Should().BeLessThanOrEqualTo(500);
        match.Warnings.PlainTextFallback.Should().BeTrue();
        match.Warnings.StructuredSegmentsAbsent.Should().Be(absent);
        match.Warnings.StructuredSegmentsEmpty.Should().Be(empty);
        match.Warnings.StructuredSegmentsInvalid.Should().Be(invalid);
    }

    [Fact]
    public async Task StructuredJson_WithMalformedSurrogateIsRejectedAndSearchUsesPlainTextFallback()
    {
        await using var fixture = await ContentFixture.CreateAsync();
        const string malformedSurrogateJson =
            "[{\"startMs\":0,\"endMs\":1,\"text\":\"needle\\uD800\",\"speaker\":null}]";
        var projectId = await fixture.SeedAsync(plainText: "plain needle", structuredJson: malformedSurrogateJson);

        var search = await fixture.Service.SearchAsync("needle");
        var retrieval = await fixture.Service.GetTranscriptAsync(projectId);

        search.Value!.Matches.Single().Warnings.StructuredSegmentsInvalid.Should().BeTrue();
        search.Value.Matches.Single().Warnings.PlainTextFallback.Should().BeTrue();
        retrieval.Error!.Code.Should().Be(ContentErrorCodes.CorruptTranscript);
    }

    [Fact]
    public async Task GetTranscriptAsync_ReturnsSanitizedStableErrors()
    {
        await using var fixture = await ContentFixture.CreateAsync();
        var corruptId = await fixture.SeedAsync(name: "private-secret", plainText: "query-secret", structuredJson: "{broken");
        var draftId = await fixture.SeedAsync(status: ProjectStatus.Draft, plainText: "draft secret", segments: [Segment("draft secret")]);

        var missing = await fixture.Service.GetTranscriptAsync(Guid.NewGuid());
        var notReady = await fixture.Service.GetTranscriptAsync(draftId);
        var corrupt = await fixture.Service.GetTranscriptAsync(corruptId);

        missing.Error!.Code.Should().Be(ContentErrorCodes.NotFound);
        notReady.Error!.Code.Should().Be(ContentErrorCodes.TranscriptNotReady);
        corrupt.Error!.Code.Should().Be(ContentErrorCodes.CorruptTranscript);
        corrupt.Error.Message.Should().NotContain("private-secret").And.NotContain("query-secret").And.NotContain("broken");
    }

    [Fact]
    public async Task Operations_RejectInvalidPagingLimitsWithValidationError()
    {
        await using var fixture = await ContentFixture.CreateAsync();
        var projectId = await fixture.SeedAsync(plainText: "needle", segments: [Segment("needle")]);

        (await fixture.Service.SearchAsync("needle", offset: -1)).Error!.Code.Should().Be(ContentErrorCodes.ValidationError);
        (await fixture.Service.SearchAsync("needle", limit: 21)).Error!.Code.Should().Be(ContentErrorCodes.ValidationError);
        (await fixture.Service.GetTranscriptAsync(projectId, segmentLimit: 0)).Error!.Code.Should().Be(ContentErrorCodes.ValidationError);
        (await fixture.Service.GetTranscriptAsync(projectId, characterLimit: 999)).Error!.Code.Should().Be(ContentErrorCodes.ValidationError);
    }

    [Fact]
    public async Task GetTranscriptAsync_RejectsMalformedTamperedAndCrossProjectCursors()
    {
        await using var fixture = await ContentFixture.CreateAsync();
        var firstId = await fixture.SeedAsync(plainText: new string('a', 2000), segments: [Segment(new string('a', 2000))]);
        var secondId = await fixture.SeedAsync(plainText: new string('b', 2000), segments: [Segment(new string('b', 2000))]);
        var firstPage = await fixture.Service.GetTranscriptAsync(firstId, characterLimit: 1000);
        var cursor = firstPage.Value!.NextCursor!;
        var replacement = cursor[^1] == 'A' ? 'B' : 'A';
        var tampered = cursor[..^1] + replacement;

        (await fixture.Service.GetTranscriptAsync(firstId, "not-base64!", characterLimit: 1000)).Error!.Code.Should().Be(ContentErrorCodes.ValidationError);
        (await fixture.Service.GetTranscriptAsync(firstId, tampered, characterLimit: 1000)).Error!.Code.Should().Be(ContentErrorCodes.ValidationError);
        (await fixture.Service.GetTranscriptAsync(secondId, cursor, characterLimit: 1000)).Error!.Code.Should().Be(ContentErrorCodes.ValidationError);
    }

    [Fact]
    public async Task GetTranscriptAsync_RejectsCursorSignedWithPublicDomainDerivedKey()
    {
        await using var fixture = await ContentFixture.CreateAsync();
        var projectId = Guid.Parse("70000000-0000-0000-0000-000000000007");
        var transcriptVersion = new DateTime(2026, 8, 9, 12, 34, 56, DateTimeKind.Utc);
        await fixture.SeedAsync(
            projectId: projectId,
            plainText: new string('a', 2000),
            segments: [Segment(new string('a', 2000))],
            transcriptUpdatedAtUtc: transcriptVersion);
        var cursorDomain = Encoding.UTF8.GetBytes("TranscriptLab.Mcp.Cursor.v1\0");
        var publicDerivedKey = SHA256.HashData(cursorDomain);
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            Version = 1,
            Mode = "segments",
            ProjectId = projectId,
            TranscriptVersion = transcriptVersion.Ticks,
            SegmentIndex = 0,
            CharacterOffset = 1000,
        });
        byte[] signedData = [.. cursorDomain, .. payload];
        var integrity = HMACSHA256.HashData(publicDerivedKey, signedData);
        var forgedCursor = Convert.ToBase64String([.. payload, .. integrity])
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        var result = await fixture.Service.GetTranscriptAsync(projectId, forgedCursor, characterLimit: 1000);

        result.IsSuccess.Should().BeFalse("a cursor signed without the configured secret must not resume transcript content");
        result.Error!.Code.Should().Be(ContentErrorCodes.ValidationError);
        result.Error.Message.Should().Be("The transcript cursor is invalid.");
    }

    [Fact]
    public async Task GetTranscriptAsync_ReconstructsFiftyThousandPlainTextCharactersExactly()
    {
        await using var fixture = await ContentFixture.CreateAsync();
        var original = string.Concat(Enumerable.Range(0, 50_000).Select(index => (char)('a' + (index % 26))));
        var projectId = await fixture.SeedAsync(plainText: original, segments: []);

        var reconstructed = await ReconstructAsync(fixture.Service, projectId, segmentLimit: 7, characterLimit: 4096);

        reconstructed.Should().Be(original);
    }

    [Fact]
    public async Task GetTranscriptAsync_CursorSurvivesServiceRecreationOverTheSameSqliteData()
    {
        await using var fixture = await ContentFixture.CreateAsync();
        var projectId = Guid.Parse("60000000-0000-0000-0000-000000000006");
        var transcriptVersion = new DateTime(2026, 8, 9, 12, 34, 56, DateTimeKind.Utc);
        var original = string.Concat(Enumerable.Repeat("restart-stable-é-🙂-", 180));
        await fixture.SeedAsync(
            projectId: projectId,
            plainText: original,
            segments: [Segment(original)],
            transcriptUpdatedAtUtc: transcriptVersion);

        var firstPage = await fixture.Service.GetTranscriptAsync(projectId, segmentLimit: 1, characterLimit: 1000);
        firstPage.IsSuccess.Should().BeTrue();
        firstPage.Value!.NextCursor.Should().NotBeNullOrWhiteSpace();
        firstPage.Value.NextCursor.Should().MatchRegex("^[A-Za-z0-9_-]+$");
        var reconstructed = new List<string>(firstPage.Value.Chunks.Select(chunk => chunk.Text));
        var cursor = firstPage.Value.NextCursor;

        await fixture.RecreateServiceAsync();

        while (cursor is not null)
        {
            var page = await fixture.Service.GetTranscriptAsync(projectId, cursor, segmentLimit: 1, characterLimit: 1000);
            page.IsSuccess.Should().BeTrue();
            reconstructed.AddRange(page.Value!.Chunks.Select(chunk => chunk.Text));
            cursor = page.Value.NextCursor;
        }

        Encoding.UTF8.GetBytes(string.Concat(reconstructed)).Should().Equal(Encoding.UTF8.GetBytes(original));
    }

    [Fact]
    public async Task GetTranscriptAsync_SplitsOneOversizedSegmentWithoutLoss()
    {
        await using var fixture = await ContentFixture.CreateAsync();
        var original = string.Concat(Enumerable.Repeat("0123456789é", 2500));
        var projectId = await fixture.SeedAsync(plainText: original, segments: [Segment(original)]);

        var reconstructed = await ReconstructAsync(fixture.Service, projectId, segmentLimit: 1, characterLimit: 1000);

        reconstructed.Should().Be(original);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetTranscriptAsync_PageBoundariesNeverSplitEmojiSurrogatePairs(bool structured)
    {
        await using var fixture = await ContentFixture.CreateAsync();
        var original = new string('x', 999) + "🙂" + new string('y', 1200);
        var projectId = await fixture.SeedAsync(
            plainText: original,
            segments: structured ? [Segment(original)] : []);
        var chunks = new List<string>();
        string? cursor = null;

        do
        {
            var page = await fixture.Service.GetTranscriptAsync(projectId, cursor, segmentLimit: 1, characterLimit: 1000);
            page.IsSuccess.Should().BeTrue();
            page.Value!.Chunks.Should().OnlyContain(chunk => HasWellFormedBoundaries(chunk.Text));
            chunks.AddRange(page.Value.Chunks.Select(chunk => chunk.Text));
            cursor = page.Value.NextCursor;
        }
        while (cursor is not null);

        string.Concat(chunks).Should().Be(original);
    }

    [Fact]
    public async Task GetTranscriptAsync_HonorsSegmentAndAggregateCharacterLimitsWithStableProvenance()
    {
        await using var fixture = await ContentFixture.CreateAsync();
        var folderId = Guid.NewGuid();
        var projectId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        await fixture.SeedAsync(
            projectId: projectId,
            folderId: folderId,
            folderName: "Fólder",
            name: "Project",
            originalFileName: "lecture.wav",
            plainText: new string('a', 700) + new string('b', 700) + new string('c', 700),
            segments: [Segment(new string('a', 700)), Segment(new string('b', 700)), Segment(new string('c', 700))]);

        var result = await fixture.Service.GetTranscriptAsync(projectId, segmentLimit: 2, characterLimit: 1000);

        result.Value!.Chunks.Should().HaveCount(2);
        result.Value.Chunks.Sum(chunk => chunk.Text.Length).Should().Be(1000);
        result.Value.HasMore.Should().BeTrue();
        result.Value.Project.FolderId.Should().Be(folderId);
        result.Value.Project.FolderName.Should().Be("Fólder");
        result.Value.Project.SourcePath.Should().Be("/projects/10000000-0000-0000-0000-000000000001");
        result.Value.Project.SourceUrl.Should().Be($"https://example.com/base/projects/{projectId}");
    }

    [Fact]
    public async Task Operations_PropagatePreCanceledTokens()
    {
        await using var fixture = await ContentFixture.CreateAsync();
        var projectId = await fixture.SeedAsync(plainText: "needle", segments: [Segment("needle")]);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var search = () => fixture.Service.SearchAsync("needle", cancellationToken: cancellation.Token);
        var retrieve = () => fixture.Service.GetTranscriptAsync(projectId, cancellationToken: cancellation.Token);

        await search.Should().ThrowAsync<OperationCanceledException>();
        await retrieve.Should().ThrowAsync<OperationCanceledException>();
    }

    private static TranscriptSegmentDto Segment(string text, long startMs = 0, long endMs = 1000, string? speaker = null) =>
        new() { StartMs = startMs, EndMs = endMs, Text = text, Speaker = speaker };

    private static bool HasWellFormedBoundaries(string text) =>
        text.Length == 0 || (!char.IsLowSurrogate(text[0]) && !char.IsHighSurrogate(text[^1]));

    private static async Task<string> ReconstructAsync(
        McpContentService service,
        Guid projectId,
        int segmentLimit,
        int characterLimit)
    {
        var chunks = new List<string>();
        string? cursor = null;
        do
        {
            var page = await service.GetTranscriptAsync(projectId, cursor, segmentLimit, characterLimit);
            page.IsSuccess.Should().BeTrue();
            chunks.AddRange(page.Value!.Chunks.Select(chunk => chunk.Text));
            cursor = page.Value.NextCursor;
        }
        while (cursor is not null);

        return string.Concat(chunks);
    }

    private sealed class ContentFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private ContentFixture(SqliteConnection connection, AppDbContext db)
        {
            _connection = connection;
            Db = db;
            Service = new McpContentService(db, new Uri("https://example.com/base/"));
        }

        public AppDbContext Db { get; private set; }
        public McpContentService Service { get; private set; }

        public static async Task<ContentFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
            var db = new AppDbContext(options);
            await db.Database.EnsureCreatedAsync();
            return new ContentFixture(connection, db);
        }

        public async Task RecreateServiceAsync()
        {
            await Db.DisposeAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
            Db = new AppDbContext(options);
            Service = new McpContentService(Db, new Uri("https://example.com/base/"));
        }

        public async Task<Guid> SeedAsync(
            Guid? projectId = null,
            Guid? folderId = null,
            string folderName = "Folder",
            string name = "Project",
            string originalFileName = "source.wav",
            ProjectStatus status = ProjectStatus.Completed,
            bool includeTranscript = true,
            string plainText = "",
            IReadOnlyList<TranscriptSegmentDto>? segments = null,
            string? structuredJson = null,
            DateTime? transcriptUpdatedAtUtc = null)
        {
            var actualFolderId = folderId ?? Guid.NewGuid();
            var folder = await Db.Folders.FindAsync(actualFolderId);
            if (folder is null)
            {
                folder = new Folder
                {
                    Id = actualFolderId,
                    Name = folderName,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow,
                };
                Db.Folders.Add(folder);
            }

            var actualProjectId = projectId ?? Guid.NewGuid();
            var project = new Project
            {
                Id = actualProjectId,
                FolderId = actualFolderId,
                Folder = folder,
                Name = name,
                OriginalFileName = originalFileName,
                StoredFileName = "source.wav",
                FileExtension = ".wav",
                MediaPath = "uploads/source.wav",
                MediaType = MediaType.Audio,
                Status = status,
                Progress = status == ProjectStatus.Completed ? 100 : 0,
                DurationMs = 1000,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
                CompletedAtUtc = status == ProjectStatus.Completed ? DateTime.UtcNow : null,
            };
            Db.Projects.Add(project);

            if (includeTranscript)
            {
                var actualSegments = segments ?? [Segment(plainText)];
                Db.Transcripts.Add(new Transcript
                {
                    Id = Guid.NewGuid(),
                    ProjectId = actualProjectId,
                    PlainText = plainText,
                    StructuredSegmentsJson = structuredJson ?? JsonSerializer.Serialize(actualSegments),
                    DetectedLanguage = "en",
                    DurationMs = 1000,
                    SegmentCount = actualSegments.Count,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = transcriptUpdatedAtUtc ?? DateTime.UtcNow,
                });
            }

            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
            return actualProjectId;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
