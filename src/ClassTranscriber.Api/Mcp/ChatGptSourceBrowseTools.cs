using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClassTranscriber.Api.Mcp;

public sealed class ChatGptSourceBrowseTools(
    IChatGptSourceCatalogService catalogService,
    ILogger<ChatGptSourceBrowseTools>? logger = null)
{
    private static readonly JsonSerializerOptions StructuredContentSerializerOptions = CreateStructuredContentSerializerOptions();
    private readonly ILogger<ChatGptSourceBrowseTools> logger = logger ?? NullLogger<ChatGptSourceBrowseTools>.Instance;

    [McpServerTool(
        Name = "list_folders",
        Title = "List transcript folders",
        ReadOnly = true,
        OpenWorld = false,
        Destructive = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ChatGptSourceFolderPage))]
    [Description("Lists all TranscriptLab folders in deterministic pages, including empty folders.")]
    public async Task<CallToolResult> ListFoldersAsync(
        [Description("Zero-based result offset.")][Range(0, int.MaxValue)] int offset = 0,
        [Description("Maximum folders to return, from 1 through 100.")][Range(1, 100)] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var outcomeCode = ContentErrorCodes.InternalError;

        try
        {
            if (offset < 0 || limit is < 1 or > 100)
            {
                outcomeCode = ContentErrorCodes.ValidationError;
                return ValidationError("Invalid list_folders arguments.");
            }

            var page = await catalogService.ListFoldersAsync(offset, limit, cancellationToken);
            outcomeCode = ChatGptSourceToolOutcome.Success;
            return Success(
                page,
                $"Returned {page.Folders.Count} {Pluralize(page.Folders.Count, "folder")} "
                + $"(offset {page.Offset}, limit {page.Limit}); {PageEnding(page.HasMore)}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            outcomeCode = ChatGptSourceToolOutcome.Cancelled;
            throw;
        }
        catch (OperationCanceledException)
        {
            outcomeCode = ContentErrorCodes.InternalError;
            return InternalError();
        }
        catch (Exception)
        {
            outcomeCode = ContentErrorCodes.InternalError;
            return InternalError();
        }
        finally
        {
            ChatGptSourceToolOutcome.Log(this.logger, "list_folders", outcomeCode);
        }
    }

    [McpServerTool(
        Name = "list_projects",
        Title = "List transcript-ready projects",
        ReadOnly = true,
        OpenWorld = false,
        Destructive = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ChatGptSourceProjectPage))]
    [Description("Lists completed TranscriptLab projects that have ready transcripts, with optional folder and name filters.")]
    public async Task<CallToolResult> ListProjectsAsync(
        [Description("Optional folder GUID used to restrict results.")] string? folderId = null,
        [Description("Optional project or original filename substring, trimmed and at most 200 characters.")][MaxLength(200)] string? nameQuery = null,
        [Description("Zero-based result offset.")][Range(0, int.MaxValue)] int offset = 0,
        [Description("Maximum projects to return, from 1 through 50.")][Range(1, 50)] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var outcomeCode = ContentErrorCodes.InternalError;

        try
        {
            if (!TryParseFolderId(folderId, out var parsedFolderId)
                || nameQuery?.Trim().Length > 200
                || offset < 0
                || limit is < 1 or > 50)
            {
                outcomeCode = ContentErrorCodes.ValidationError;
                return ValidationError("Invalid list_projects arguments.");
            }

            var page = await catalogService.ListProjectsAsync(
                parsedFolderId,
                nameQuery?.Trim(),
                offset,
                limit,
                cancellationToken);
            outcomeCode = ChatGptSourceToolOutcome.Success;
            return Success(
                page,
                $"Returned {page.Projects.Count} {Pluralize(page.Projects.Count, "project")} "
                + $"(offset {page.Offset}, limit {page.Limit}); {PageEnding(page.HasMore)}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            outcomeCode = ChatGptSourceToolOutcome.Cancelled;
            throw;
        }
        catch (OperationCanceledException)
        {
            outcomeCode = ContentErrorCodes.InternalError;
            return InternalError();
        }
        catch (Exception)
        {
            outcomeCode = ContentErrorCodes.InternalError;
            return InternalError();
        }
        finally
        {
            ChatGptSourceToolOutcome.Log(this.logger, "list_projects", outcomeCode);
        }
    }

    private static bool TryParseFolderId(string? folderId, out Guid? parsedFolderId)
    {
        parsedFolderId = null;
        if (folderId is null)
            return true;
        if (!Guid.TryParse(folderId, out var value))
            return false;
        parsedFolderId = value;
        return true;
    }

    private static CallToolResult Success<T>(T structuredContent, string summary)
        => new()
        {
            Content = [new TextContentBlock { Text = summary }],
            StructuredContent = JsonSerializer.SerializeToElement(
                structuredContent,
                StructuredContentSerializerOptions),
            IsError = false,
        };

    private static CallToolResult ValidationError(string message)
        => Error("validation_error", message);

    private static CallToolResult InternalError()
        => Error("internal_error", "The browse request could not be completed.");

    private static CallToolResult Error(string code, string message)
        => new()
        {
            Content = [new TextContentBlock { Text = $"{code}: {message}" }],
            IsError = true,
        };

    private static string Pluralize(int count, string singular)
        => count == 1 ? singular : singular + "s";

    private static string PageEnding(bool hasMore)
        => hasMore ? "more results are available." : "this is the final page.";

    private static JsonSerializerOptions CreateStructuredContentSerializerOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };
        options.Converters.Add(new UtcDateTimeJsonConverter());
        return options;
    }

    private sealed class UtcDateTimeJsonConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => reader.GetDateTime();

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
            => writer.WriteStringValue(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }
}
