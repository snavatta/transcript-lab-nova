using System.Text.Json;
using ClassTranscriber.Api.Contracts;
using ClassTranscriber.Api.Domain;
using ClassTranscriber.Api.Mcp;
using ClassTranscriber.Api.Persistence;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ClassTranscriber.Api.Tests;

public sealed class McpCatalogServiceTests
{
    [Fact]
    public async Task ListFolders_IncludesEmptyFoldersInCaseInsensitiveGuidOrderAndPages()
    {
        await using var fixture = await CatalogFixture.CreateAsync();
        await fixture.AddFolderAsync(Guid.Parse("00000000-0000-0000-0000-000000000002"), "alpha");
        await fixture.AddFolderAsync(Guid.Parse("00000000-0000-0000-0000-000000000001"), "ALPHA");
        await fixture.AddFolderAsync(Guid.Parse("00000000-0000-0000-0000-000000000003"), "Bravo", projectCount: 1);

        var first = await fixture.Service.ListFoldersAsync(limit: 2);
        var empty = await fixture.Service.ListFoldersAsync(offset: 10, limit: 2);

        first.Folders.Select(folder => folder.FolderId).Should().Equal(
            Guid.Parse("00000000-0000-0000-0000-000000000001"),
            Guid.Parse("00000000-0000-0000-0000-000000000002"));
        first.Folders[0].ProjectCount.Should().Be(0);
        first.HasMore.Should().BeTrue();
        first.NextOffset.Should().Be(2);
        empty.Folders.Should().BeEmpty();
        empty.HasMore.Should().BeFalse();
        empty.NextOffset.Should().BeNull();
    }

    [Fact]
    public async Task ListProjects_FiltersReadyProjectsOrdersAndPagesWithMetadataAndSourceUrl()
    {
        await using var fixture = await CatalogFixture.CreateAsync("https://example.com/transcriptlab");
        var folderId = Guid.NewGuid();
        var completedAt = new DateTime(2026, 8, 9, 12, 0, 0, DateTimeKind.Utc);
        var firstId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var secondId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        await fixture.AddProjectAsync(secondId, folderId, "Unicode élan", ProjectStatus.Completed, completedAt, includeTranscript: true);
        await fixture.AddProjectAsync(firstId, folderId, "Unicode Alpha", ProjectStatus.Completed, completedAt, includeTranscript: true);
        var result = await fixture.Service.ListProjectsAsync(folderId: folderId, nameQuery: "  unicode ", limit: 1);

        result.Projects.Should().ContainSingle();
        result.Projects[0].ProjectId.Should().Be(firstId);
        result.Projects[0].ProjectName.Should().Be("Unicode Alpha");
        result.Projects[0].FolderName.Should().Be("Fólder 日本語");
        result.Projects[0].SourcePath.Should().Be("/projects/00000000-0000-0000-0000-000000000001");
        result.Projects[0].SourceUrl.Should().Be($"https://example.com/transcriptlab/projects/{firstId}");
        result.HasMore.Should().BeTrue();
        result.NextOffset.Should().Be(1);

        var filtered = await fixture.Service.ListProjectsAsync(nameQuery: "élan");
        filtered.Projects.Select(project => project.ProjectName).Should().Equal("Unicode élan");
    }

    [Fact]
    public async Task ListProjects_ExcludesEveryIneligibleProjectWhenAllMatchTheActiveFilters()
    {
        await using var fixture = await CatalogFixture.CreateAsync();
        var includedFolderId = Guid.NewGuid();
        var otherFolderId = Guid.NewGuid();
        var completedAt = new DateTime(2026, 8, 9, 12, 0, 0, DateTimeKind.Utc);
        var readyProjectId = Guid.NewGuid();
        await fixture.AddProjectAsync(readyProjectId, includedFolderId, "catalog target ready", ProjectStatus.Completed, completedAt, includeTranscript: true);
        await fixture.AddProjectAsync(Guid.NewGuid(), includedFolderId, "catalog target draft", ProjectStatus.Draft, completedAt, includeTranscript: true);
        await fixture.AddProjectAsync(Guid.NewGuid(), includedFolderId, "catalog target queued", ProjectStatus.Queued, completedAt, includeTranscript: true);
        await fixture.AddProjectAsync(Guid.NewGuid(), includedFolderId, "catalog target failed", ProjectStatus.Failed, completedAt, includeTranscript: true);
        await fixture.AddProjectAsync(Guid.NewGuid(), includedFolderId, "catalog target transcriptless", ProjectStatus.Completed, completedAt, includeTranscript: false);
        await fixture.AddProjectAsync(Guid.NewGuid(), otherFolderId, "catalog target other folder", ProjectStatus.Completed, completedAt, includeTranscript: true);

        var result = await fixture.Service.ListProjectsAsync(
            folderId: includedFolderId,
            nameQuery: "catalog target");

        result.Projects.Select(project => project.ProjectId).Should().Equal(readyProjectId);
    }

    [Theory]
    [InlineData("%", "literal % marker", "literal percent marker")]
    [InlineData("_", "literal _ marker", "literal underscore marker")]
    [InlineData("\\", "literal \\ marker", "literal slash marker")]
    public async Task ListProjects_TreatsLikeWildcardsAsLiteralText(
        string query, string matchingName, string nonMatchingName)
    {
        await using var fixture = await CatalogFixture.CreateAsync();
        var folderId = Guid.NewGuid();
        var completedAt = new DateTime(2026, 8, 9, 12, 0, 0, DateTimeKind.Utc);
        var matchingId = Guid.NewGuid();
        await fixture.AddProjectAsync(matchingId, folderId, matchingName, ProjectStatus.Completed, completedAt, includeTranscript: true);
        await fixture.AddProjectAsync(Guid.NewGuid(), folderId, nonMatchingName, ProjectStatus.Completed, completedAt, includeTranscript: true);

        var result = await fixture.Service.ListProjectsAsync(folderId: folderId, nameQuery: query);

        result.Projects.Select(project => project.ProjectId).Should().Equal(matchingId);
    }

    [Fact]
    public async Task ListProjects_RejectsEmptyFolderIdAtTypedCatalogBoundary()
    {
        await using var fixture = await CatalogFixture.CreateAsync();

        var action = () => fixture.Service.ListProjectsAsync(folderId: Guid.Empty);

        await action.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("folderId");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-guid")]
    public async Task ListProjectsTool_RejectsMalformedFolderIdBeforeCatalogQuery(string folderId)
    {
        await using var fixture = await CatalogFixture.CreateAsync();
        var tool = new McpBrowseTools(fixture.Service);

        var result = await tool.ListProjectsAsync(folderId: folderId);

        result.IsError.Should().BeTrue();
        result.Content.Should().ContainSingle();
        result.Content[0].Should().BeOfType<ModelContextProtocol.Protocol.TextContentBlock>()
            .Which.Text.Should().StartWith("validation_error:");
    }

    [Theory]
    [InlineData("https://example.com/base?tenant=value")]
    [InlineData("https://example.com/base#section")]
    [InlineData("https://user@example.com/base")]
    public void EnabledApplicationBaseUrl_RejectsUnsafeUriComponents(string applicationBaseUrl)
    {
        var action = () => new TestWebApplicationFactory(
            configuration: new Dictionary<string, string?>
            {
                ["Mcp:Enabled"] = "true",
                ["Mcp:ApplicationBaseUrl"] = applicationBaseUrl,
            });

        action.Should().Throw<Exception>();
    }

    [Fact]
    public async Task ListProjects_WithNoApplicationBaseUrlReturnsNullSourceUrlAndNeverTranscriptText()
    {
        await using var fixture = await CatalogFixture.CreateAsync();
        var projectId = await fixture.AddProjectAsync(
            Guid.NewGuid(), Guid.NewGuid(), "safe name", ProjectStatus.Completed,
            DateTime.UtcNow, includeTranscript: true, plainText: "DO NOT EXPOSE THIS TRANSCRIPT");

        var result = await fixture.Service.ListProjectsAsync();
        var json = JsonSerializer.Serialize(result);

        result.Projects.Single().SourceUrl.Should().BeNull();
        json.Should().NotContain("DO NOT EXPOSE THIS TRANSCRIPT");
        typeof(McpProjectCatalog).GetProperties().Select(property => property.Name)
            .Should().NotContain("PlainText");
    }

    [Fact]
    public async Task ListProjects_PropagatesAlreadyCanceledToken()
    {
        await using var fixture = await CatalogFixture.CreateAsync();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var action = () => fixture.Service.ListProjectsAsync(cancellationToken: cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    private sealed class CatalogFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private CatalogFixture(SqliteConnection connection, AppDbContext db, string? applicationBaseUrl)
        {
            _connection = connection;
            Db = db;
            Service = new McpCatalogService(db, new McpOptions { ApplicationBaseUrl = applicationBaseUrl });
        }

        public AppDbContext Db { get; }
        public McpCatalogService Service { get; }

        public static async Task<CatalogFixture> CreateAsync(string? applicationBaseUrl = null)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
            var db = new AppDbContext(options);
            await db.Database.EnsureCreatedAsync();
            return new CatalogFixture(connection, db, applicationBaseUrl);
        }

        public async Task AddFolderAsync(Guid id, string name, int projectCount = 0)
        {
            var folder = new Folder { Id = id, Name = name, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow };
            Db.Folders.Add(folder);
            for (var index = 0; index < projectCount; index++)
                await AddProjectAsync(Guid.NewGuid(), id, $"folder project {index}", ProjectStatus.Draft, DateTime.UtcNow, includeTranscript: false);
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
        }

        public async Task<Guid> AddProjectAsync(
            Guid projectId, Guid folderId, string name, ProjectStatus status, DateTime completedAtUtc,
            bool includeTranscript, string plainText = "fixture transcript")
        {
            var folder = await Db.Folders.FindAsync(folderId);
            if (folder is null)
            {
                folder = new Folder { Id = folderId, Name = "Fólder 日本語", CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow };
                Db.Folders.Add(folder);
            }

            Db.Projects.Add(new Project
            {
                Id = projectId, FolderId = folderId, Folder = folder, Name = name,
                OriginalFileName = "lecture é.wav", StoredFileName = "lecture.wav", FileExtension = ".wav",
                MediaPath = "uploads/lecture.wav", MediaType = MediaType.Audio, Status = status,
                DurationMs = 1234, CreatedAtUtc = completedAtUtc.AddMinutes(-1), UpdatedAtUtc = completedAtUtc,
                CompletedAtUtc = status == ProjectStatus.Completed ? completedAtUtc : null,
            });
            if (includeTranscript)
                Db.Transcripts.Add(new Transcript
                {
                    Id = Guid.NewGuid(), ProjectId = projectId, PlainText = plainText,
                    StructuredSegmentsJson = "[]", DetectedLanguage = "日本語", DurationMs = 1234,
                    SegmentCount = 0, CreatedAtUtc = completedAtUtc, UpdatedAtUtc = completedAtUtc.AddSeconds(1),
                });
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
            return projectId;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
