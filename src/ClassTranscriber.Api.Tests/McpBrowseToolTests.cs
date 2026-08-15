using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using ClassTranscriber.Api.Contracts;
using ClassTranscriber.Api.Domain;
using ClassTranscriber.Api.Mcp;
using ClassTranscriber.Api.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ClassTranscriber.Api.Tests;

public sealed class McpBrowseToolTests
{
    [Fact]
    public async Task BrowseTools_AreDiscoverableWithExactSchemasAndReadOnlyAnnotations()
    {
        await using var fixture = await BrowseToolFixture.CreateAsync();

        using var response = await fixture.SendAsync(1, "tools/list", new { });
        using var json = await ReadMcpJsonAsync(response);

        var tools = json.RootElement.GetProperty("result").GetProperty("tools");
        tools.EnumerateArray().Select(tool => tool.GetProperty("name").GetString())
            .Should().BeEquivalentTo("list_folders", "list_projects", "search_transcripts", "get_transcript");
        foreach (var tool in tools.EnumerateArray())
        {
            var annotations = tool.GetProperty("annotations");
            annotations.GetProperty("readOnlyHint").GetBoolean().Should().BeTrue();
            annotations.GetProperty("openWorldHint").GetBoolean().Should().BeFalse();
            annotations.GetProperty("destructiveHint").GetBoolean().Should().BeFalse();
            tool.TryGetProperty("outputSchema", out _).Should().BeTrue();
        }

        var foldersTool = tools.EnumerateArray().Single(tool => tool.GetProperty("name").GetString() == "list_folders");
        var foldersSchema = foldersTool.GetProperty("inputSchema").GetProperty("properties");
        foldersSchema.GetProperty("offset").GetProperty("default").GetInt32().Should().Be(0);
        foldersSchema.GetProperty("offset").GetProperty("minimum").GetInt32().Should().Be(0);
        foldersSchema.GetProperty("limit").GetProperty("default").GetInt32().Should().Be(50);
        foldersSchema.GetProperty("limit").GetProperty("minimum").GetInt32().Should().Be(1);
        foldersSchema.GetProperty("limit").GetProperty("maximum").GetInt32().Should().Be(100);

        var projectsTool = tools.EnumerateArray().Single(tool => tool.GetProperty("name").GetString() == "list_projects");
        var projectsSchema = projectsTool.GetProperty("inputSchema").GetProperty("properties");
        projectsSchema.GetProperty("offset").GetProperty("default").GetInt32().Should().Be(0);
        projectsSchema.GetProperty("limit").GetProperty("default").GetInt32().Should().Be(20);
        projectsSchema.GetProperty("limit").GetProperty("maximum").GetInt32().Should().Be(50);
        projectsSchema.GetProperty("nameQuery").GetProperty("maxLength").GetInt32().Should().Be(200);
    }

    [Fact]
    public async Task BrowseTools_PageAndFollowReturnedFolderIdOverStreamableHttp()
    {
        await using var fixture = await BrowseToolFixture.CreateAsync();
        var firstFolderId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var secondFolderId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        await fixture.AddFolderAsync(firstFolderId, "Alpha");
        await fixture.AddFolderAsync(secondFolderId, "Bravo");
        await fixture.AddReadyProjectAsync(firstFolderId, "Lecture one");

        using var foldersResponse = await fixture.SendAsync(
            2, "tools/call", new { name = "list_folders", arguments = new { limit = 1 } });
        using var foldersJson = await ReadMcpJsonAsync(foldersResponse);
        var foldersResult = foldersJson.RootElement.GetProperty("result");
        foldersResult.GetProperty("isError").GetBoolean().Should().BeFalse();
        foldersResult.GetProperty("content")[0].GetProperty("text").GetString()
            .Should().Be("Returned 1 folder (offset 0, limit 1); more results are available.");
        var folders = foldersResult.GetProperty("structuredContent");
        folders.GetProperty("hasMore").GetBoolean().Should().BeTrue();
        folders.GetProperty("nextOffset").GetInt32().Should().Be(1);
        var returnedFolderId = folders.GetProperty("folders")[0].GetProperty("folderId").GetGuid();
        returnedFolderId.Should().Be(firstFolderId);

        using var projectsResponse = await fixture.SendAsync(
            3,
            "tools/call",
            new
            {
                name = "list_projects",
                arguments = new { folderId = returnedFolderId.ToString(), nameQuery = " lecture ", limit = 1 },
            });
        using var projectsJson = await ReadMcpJsonAsync(projectsResponse);
        var projectsResult = projectsJson.RootElement.GetProperty("result");
        projectsResult.GetProperty("isError").GetBoolean().Should().BeFalse();
        projectsResult.GetProperty("content")[0].GetProperty("text").GetString()
            .Should().Be("Returned 1 project (offset 0, limit 1); this is the final page.");
        var structuredProjects = projectsResult.GetProperty("structuredContent");
        structuredProjects.GetProperty("nextOffset").ValueKind.Should().Be(JsonValueKind.Null);
        var project = structuredProjects.GetProperty("projects")[0];
        project.GetProperty("folderId").GetGuid().Should().Be(firstFolderId);
        project.GetProperty("projectName").GetString().Should().Be("Lecture one");
        project.GetProperty("completedAtUtc").GetString().Should().EndWith("Z");
        project.GetProperty("sourcePath").GetString().Should().StartWith("/projects/");
        project.GetProperty("sourceUrl").GetString().Should().StartWith("https://example.com/transcriptlab/projects/");
    }

    [Theory]
    [InlineData("list_folders", "{\"offset\":-1}")]
    [InlineData("list_folders", "{\"limit\":0}")]
    [InlineData("list_folders", "{\"limit\":101}")]
    [InlineData("list_projects", "{\"folderId\":\"not-a-guid-private-value\"}")]
    [InlineData("list_projects", "{\"limit\":51}")]
    [InlineData("list_projects", "{\"nameQuery\":\"xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx\"}")]
    public async Task BrowseTools_ReturnStableSanitizedValidationErrors(string toolName, string argumentsJson)
    {
        await using var fixture = await BrowseToolFixture.CreateAsync();
        using var arguments = JsonDocument.Parse(argumentsJson);

        using var response = await fixture.SendAsync(
            4, "tools/call", new { name = toolName, arguments = arguments.RootElement });
        var responseBody = await response.Content.ReadAsStringAsync();
        using var json = ParseMcpJson(response, responseBody);
        var result = json.RootElement.GetProperty("result");

        result.GetProperty("isError").GetBoolean().Should().BeTrue();
        result.GetProperty("content")[0].GetProperty("text").GetString()
            .Should().StartWith("validation_error:");
        responseBody.Should().NotContain("not-a-guid-private-value");
        responseBody.Should().NotContain(new string('x', 201));
    }

    [Fact]
    public async Task Browse_handlers_log_one_sanitized_outcome_for_success_validation_fault_and_cancellation()
    {
        var entries = new ConcurrentQueue<CapturedLogEntry>();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(new CapturingLoggerProvider(entries)));
        var success = new StubCatalogService(
            folders: _ => Task.FromResult(new McpFolderPage
            {
                Folders = [], Offset = 0, Limit = 50, HasMore = false, NextOffset = null,
            }),
            projects: _ => Task.FromResult(new McpProjectPage
            {
                Projects = [], Offset = 0, Limit = 20, HasMore = false, NextOffset = null,
            }));
        var successTools = new McpBrowseTools(success, loggerFactory.CreateLogger<McpBrowseTools>());

        (await successTools.ListFoldersAsync()).IsError.Should().BeFalse();
        (await successTools.ListProjectsAsync()).IsError.Should().BeFalse();
        (await successTools.ListFoldersAsync(offset: -1)).IsError.Should().BeTrue();
        (await successTools.ListProjectsAsync(folderId: "private-invalid-folder-id")).IsError.Should().BeTrue();

        var faulted = new StubCatalogService(
            folders: _ => Task.FromException<McpFolderPage>(new InvalidOperationException("private exception")),
            projects: _ => Task.FromException<McpProjectPage>(new OperationCanceledException("private cancellation")));
        var faultedTools = new McpBrowseTools(faulted, loggerFactory.CreateLogger<McpBrowseTools>());
        (await faultedTools.ListFoldersAsync()).IsError.Should().BeTrue();
        (await faultedTools.ListProjectsAsync()).IsError.Should().BeTrue();

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelledService = new StubCatalogService(
            folders: _ => Task.FromException<McpFolderPage>(new OperationCanceledException()),
            projects: _ => throw new InvalidOperationException());
        var cancelledTools = new McpBrowseTools(
            cancelledService,
            loggerFactory.CreateLogger<McpBrowseTools>());
        var cancelled = () => cancelledTools.ListFoldersAsync(cancellationToken: cancellation.Token);
        await cancelled.Should().ThrowAsync<OperationCanceledException>();

        entries.Should().HaveCount(7);
        foreach (var entry in entries)
        {
            entry.EventId.Should().Be(new EventId(2400, "McpToolCompleted"));
            entry.Properties.Keys.Should().BeEquivalentTo("ToolName", "OutcomeCode");
            entry.Exception.Should().BeNull();
            entry.Message.Should().NotContain("private");
        }
        entries.Select(entry => $"{entry.Properties["ToolName"]}|{entry.Properties["OutcomeCode"]}|{entry.Level}")
            .Should().BeEquivalentTo(
                "list_folders|success|Information",
                "list_projects|success|Information",
                "list_folders|validation_error|Warning",
                "list_projects|validation_error|Warning",
                "list_folders|internal_error|Warning",
                "list_projects|internal_error|Warning",
                "list_folders|cancelled|Information");
    }

    [Fact]
    public async Task MutationTool_IsNeitherDiscoverableNorCallable()
    {
        await using var fixture = await BrowseToolFixture.CreateAsync();

        using var listResponse = await fixture.SendAsync(5, "tools/list", new { });
        var listBody = await listResponse.Content.ReadAsStringAsync();
        listBody.Should().NotContain("delete_project");

        using var callResponse = await fixture.SendAsync(
            6, "tools/call", new { name = "delete_project", arguments = new { } });
        using var callJson = await ReadMcpJsonAsync(callResponse);
        callJson.RootElement.GetProperty("error").GetProperty("message").GetString()
            .Should().Contain("Unknown tool");
    }

    private static async Task<JsonDocument> ReadMcpJsonAsync(HttpResponseMessage response)
        => ParseMcpJson(response, await response.Content.ReadAsStringAsync());

    private static JsonDocument ParseMcpJson(HttpResponseMessage response, string body)
    {
        response.IsSuccessStatusCode.Should().BeTrue(body);
        if (response.Content.Headers.ContentType?.MediaType == "text/event-stream")
        {
            var dataLine = body.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .First(line => line.StartsWith("data: ", StringComparison.Ordinal));
            body = dataLine[6..].Trim();
        }

        return JsonDocument.Parse(body);
    }

    private sealed class BrowseToolFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly WebApplication _app;

        private BrowseToolFixture(SqliteConnection connection, WebApplication app, HttpClient client)
        {
            _connection = connection;
            _app = app;
            Client = client;
        }

        private HttpClient Client { get; }

        public static async Task<BrowseToolFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                Args = [],
                EnvironmentName = "Testing",
            });
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Mcp:Enabled"] = "true",
                ["Mcp:ApplicationBaseUrl"] = "https://example.com/transcriptlab/",
            });
            builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(connection));
            builder.Services.AddMcp(builder.Configuration);
            builder.Services.Configure<McpOptions>(options =>
            {
                options.CursorIntegrityKey = "test-cursor-integrity-key-0123456789abcdef";
            });

            var app = builder.Build();
            app.MapMcp();
            await app.StartAsync();
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await db.Database.EnsureCreatedAsync();
            }

            var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/event-stream");
            client.DefaultRequestHeaders.Add("MCP-Protocol-Version", "2025-06-18");
            return new BrowseToolFixture(connection, app, client);
        }

        public async Task AddFolderAsync(Guid folderId, string name)
        {
            await using var scope = _app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Folders.Add(new Folder
            {
                Id = folderId,
                Name = name,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        public async Task AddReadyProjectAsync(Guid folderId, string name)
        {
            await using var scope = _app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var projectId = Guid.NewGuid();
            var completedAtUtc = new DateTime(2026, 8, 9, 12, 0, 0, DateTimeKind.Utc);
            db.Projects.Add(new Project
            {
                Id = projectId,
                FolderId = folderId,
                Name = name,
                OriginalFileName = "lecture.wav",
                StoredFileName = "lecture.wav",
                FileExtension = ".wav",
                MediaPath = "uploads/lecture.wav",
                MediaType = MediaType.Audio,
                Status = ProjectStatus.Completed,
                DurationMs = 1234,
                CreatedAtUtc = completedAtUtc.AddMinutes(-1),
                UpdatedAtUtc = completedAtUtc,
                CompletedAtUtc = completedAtUtc,
            });
            db.Transcripts.Add(new Transcript
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                PlainText = "publishable fixture transcript",
                StructuredSegmentsJson = "[]",
                DetectedLanguage = "en",
                DurationMs = 1234,
                SegmentCount = 0,
                CreatedAtUtc = completedAtUtc,
                UpdatedAtUtc = completedAtUtc.AddSeconds(1),
            });
            await db.SaveChangesAsync();
        }

        public async Task<HttpResponseMessage> SendAsync(int id, string method, object parameters)
            => await Client.PostAsJsonAsync(
                "/mcp",
                new { jsonrpc = "2.0", id, method, @params = parameters });

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class StubCatalogService(
        Func<CancellationToken, Task<McpFolderPage>> folders,
        Func<CancellationToken, Task<McpProjectPage>> projects)
        : IMcpCatalogService
    {
        public Task<McpFolderPage> ListFoldersAsync(
            int offset = 0,
            int limit = 50,
            CancellationToken cancellationToken = default) => folders(cancellationToken);

        public Task<McpProjectPage> ListProjectsAsync(
            Guid? folderId = null,
            string? nameQuery = null,
            int offset = 0,
            int limit = 20,
            CancellationToken cancellationToken = default) => projects(cancellationToken);
    }
}
