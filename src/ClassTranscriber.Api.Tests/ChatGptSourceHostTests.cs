using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClassTranscriber.Api.Contracts;
using ClassTranscriber.Api.Domain;
using ClassTranscriber.Api.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using ClassTranscriber.Api;

namespace ClassTranscriber.Api.Tests;

public sealed class ChatGptSourceHostTests
{
    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    public async Task Disabled_mcp_paths_return_non_html_404(string methodName)
    {
        await using var factory = new TestWebApplicationFactory(
            includeFrontendAppShell: true,
            configuration: new Dictionary<string, string?>
            {
                ["ChatGptSource:Enabled"] = "false",
            });

        foreach (var path in new[] { "/mcp", "/mcp/" })
        {
            using var request = new HttpRequestMessage(new HttpMethod(methodName), path);
            if (methodName == "POST")
            {
                request.Content = JsonContent.Create(new
                {
                    jsonrpc = "2.0",
                    id = 1,
                    method = "initialize",
                    @params = new { protocolVersion = "2025-06-18" },
                });
            }

            using var response = await factory.Client.SendAsync(request);

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            response.Content.Headers.ContentType?.MediaType.Should().NotBe("text/html");
        }
    }

    [Fact]
    public async Task Enabled_mcp_supports_initialize_and_tools_list_over_http()
    {
        await using var factory = new TestWebApplicationFactory(
            configuration: new Dictionary<string, string?>
            {
                ["ChatGptSource:Enabled"] = "true",
                ["ChatGptSource:ApplicationBaseUrl"] = "https://example.com/transcriptlab/",
            });
        factory.Client.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/event-stream");
        factory.Services.GetRequiredService<IOptions<ChatGptSourceOptions>>().Value.ApplicationBaseUrl
            .Should().Be("https://example.com/transcriptlab/");

        using var initializeResponse = await factory.Client.PostAsJsonAsync(
            "/mcp",
            new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "initialize",
                @params = new
                {
                    protocolVersion = "2025-06-18",
                    capabilities = new { },
                    clientInfo = new { name = "host-test", version = "1.0" },
                },
            });

        initializeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var initializeJson = JsonDocument.Parse(await ReadMcpJsonAsync(initializeResponse));
        initializeJson.RootElement.GetProperty("result").GetProperty("serverInfo").GetProperty("name")
            .GetString().Should().Be("TranscriptLab Nova ChatGPT Transcript Source");
        factory.Client.DefaultRequestHeaders.Remove("MCP-Protocol-Version");
        factory.Client.DefaultRequestHeaders.Add("MCP-Protocol-Version", "2025-06-18");

        using var toolsResponse = await factory.Client.PostAsJsonAsync(
            "/mcp",
            new
            {
                jsonrpc = "2.0",
                id = 2,
                method = "tools/list",
                @params = new { },
            });

        toolsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var toolsBody = await ReadMcpJsonAsync(toolsResponse);
        using var toolsJson = JsonDocument.Parse(toolsBody);
        var tools = toolsJson.RootElement.GetProperty("result").GetProperty("tools");
        tools.EnumerateArray().Select(tool => tool.GetProperty("name").GetString()).Should().BeEquivalentTo(
            "list_folders",
            "list_projects",
            "search_transcripts",
            "get_transcript");
        tools.EnumerateArray().Select(tool => tool.GetProperty("name").GetString())
            .Should().OnlyHaveUniqueItems();
        foreach (var tool in tools.EnumerateArray())
        {
            var annotations = tool.GetProperty("annotations");
            annotations.GetProperty("readOnlyHint").GetBoolean().Should().BeTrue();
            annotations.GetProperty("openWorldHint").GetBoolean().Should().BeFalse();
            annotations.GetProperty("destructiveHint").GetBoolean().Should().BeFalse();
            tool.TryGetProperty("outputSchema", out _).Should().BeTrue();
        }

        var (folderId, projectId) = await SeedTranscriptAsync(factory.Services);
        var calls = new (string Name, object Arguments)[]
        {
            ("list_folders", new { limit = 1 }),
            ("list_projects", new { folderId, limit = 1 }),
            ("search_transcripts", new { query = "fixture", folderId, limit = 1 }),
            ("get_transcript", new { projectId, segmentLimit = 1, characterLimit = 1000 }),
        };
        var requestId = 10;
        foreach (var call in calls)
        {
            using var result = await CallToolAsync(factory.Client, requestId++, call.Name, call.Arguments);
            var toolResult = result.RootElement.GetProperty("result");
            toolResult.GetProperty("isError").GetBoolean().Should().BeFalse(call.Name);
            toolResult.TryGetProperty("structuredContent", out _).Should().BeTrue(call.Name);
        }
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    public async Task Enabled_mcp_accepts_ipv4_and_ipv6_loopback(string remoteAddress)
    {
        await using var factory = new TestWebApplicationFactory(
            configuration: new Dictionary<string, string?>
            {
                ["ChatGptSource:Enabled"] = "true",
            },
            remoteIpAddressOverride: IPAddress.Parse(remoteAddress));
        factory.Client.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/event-stream");

        using var response = await SendInitializeAsync(factory.Client);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Enabled_mcp_rejects_non_loopback_client_with_non_html_403()
    {
        await using var factory = new TestWebApplicationFactory(
            configuration: new Dictionary<string, string?>
            {
                ["ChatGptSource:Enabled"] = "true",
            },
            remoteIpAddressOverride: IPAddress.Parse("192.0.2.1"));
        factory.Client.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/event-stream");

        using var response = await SendInitializeAsync(factory.Client);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
        (await response.Content.ReadAsStringAsync()).Should().Be("{\"error\":\"forbidden\"}");
    }

    private static async Task<(Guid FolderId, Guid ProjectId)> SeedTranscriptAsync(IServiceProvider services)
    {
        var folderId = Guid.Parse("30000000-0000-0000-0000-000000000003");
        var projectId = Guid.Parse("40000000-0000-0000-0000-000000000004");
        var completedAtUtc = new DateTime(2026, 8, 9, 12, 0, 0, DateTimeKind.Utc);
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Folders.Add(new Folder
        {
            Id = folderId,
            Name = "Publishable fixtures",
            CreatedAtUtc = completedAtUtc.AddHours(-1),
            UpdatedAtUtc = completedAtUtc,
        });
        db.Projects.Add(new Project
        {
            Id = projectId,
            FolderId = folderId,
            Name = "Protocol fixture",
            OriginalFileName = "fixture.wav",
            StoredFileName = "fixture.wav",
            FileExtension = ".wav",
            MediaPath = "uploads/fixture.wav",
            MediaType = MediaType.Audio,
            Status = ProjectStatus.Completed,
            Progress = 100,
            DurationMs = 1000,
            CreatedAtUtc = completedAtUtc.AddMinutes(-10),
            UpdatedAtUtc = completedAtUtc,
            CompletedAtUtc = completedAtUtc,
        });
        db.Transcripts.Add(new Transcript
        {
            Id = Guid.Parse("50000000-0000-0000-0000-000000000005"),
            ProjectId = projectId,
            PlainText = "Publishable fixture transcript.",
            StructuredSegmentsJson = JsonSerializer.Serialize(new[]
            {
                new TranscriptSegmentDto
                {
                    StartMs = 0,
                    EndMs = 1000,
                    Text = "Publishable fixture transcript.",
                    Speaker = "Teacher",
                },
            }),
            DetectedLanguage = "en",
            DurationMs = 1000,
            SegmentCount = 1,
            CreatedAtUtc = completedAtUtc,
            UpdatedAtUtc = completedAtUtc,
        });
        await db.SaveChangesAsync();
        return (folderId, projectId);
    }

    private static async Task<JsonDocument> CallToolAsync(
        HttpClient client,
        int id,
        string name,
        object arguments)
    {
        using var response = await client.PostAsJsonAsync(
            "/mcp",
            new { jsonrpc = "2.0", id, method = "tools/call", @params = new { name, arguments } });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return JsonDocument.Parse(await ReadMcpJsonAsync(response));
    }

    private static Task<HttpResponseMessage> SendInitializeAsync(HttpClient client) => client.PostAsJsonAsync(
        "/mcp",
        new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
            @params = new
            {
                protocolVersion = "2025-06-18",
                capabilities = new { },
                clientInfo = new { name = "network-boundary-test", version = "1.0" },
            },
        });

    private static async Task<string> ReadMcpJsonAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (response.Content.Headers.ContentType?.MediaType == "text/event-stream")
        {
            var dataLine = body.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .First(line => line.StartsWith("data: ", StringComparison.Ordinal));
            return dataLine[6..].Trim();
        }

        return body;
    }

    [Fact]
    public void Enabled_invalid_application_base_url_fails_startup_validation()
    {
        var action = () => new TestWebApplicationFactory(
            configuration: new Dictionary<string, string?>
            {
                ["ChatGptSource:Enabled"] = "true",
                ["ChatGptSource:ApplicationBaseUrl"] = "not-a-url",
            });

        action.Should().Throw<Exception>();
    }

    [Fact]
    public async Task Disabled_invalid_application_base_url_does_not_break_startup()
    {
        await using var factory = new TestWebApplicationFactory(
            configuration: new Dictionary<string, string?>
            {
                ["ChatGptSource:Enabled"] = "false",
                ["ChatGptSource:ApplicationBaseUrl"] = "not-a-url",
            });

        (await factory.Client.GetAsync("/api/health")).IsSuccessStatusCode.Should().BeTrue();
    }
}
