using Microsoft.Extensions.Logging;

namespace ClassTranscriber.Api.Mcp;

internal static class McpToolOutcome
{
    internal const string Success = "success";
    internal const string Cancelled = "cancelled";
    internal const int EventId = 2400;
    internal const string EventName = "McpToolCompleted";

    internal static string ResolveServiceCode(ContentQueryError? error) => error?.Code switch
    {
        ContentErrorCodes.ValidationError => ContentErrorCodes.ValidationError,
        ContentErrorCodes.NotFound => ContentErrorCodes.NotFound,
        ContentErrorCodes.TranscriptNotReady => ContentErrorCodes.TranscriptNotReady,
        ContentErrorCodes.CorruptTranscript => ContentErrorCodes.CorruptTranscript,
        ContentErrorCodes.InternalError => ContentErrorCodes.InternalError,
        _ => ContentErrorCodes.InternalError,
    };

    internal static void Log(ILogger logger, string toolName, string outcomeCode)
    {
        var level = outcomeCode is Success or Cancelled ? LogLevel.Information : LogLevel.Warning;
        logger.Log(
            level,
            new EventId(EventId, EventName),
            "MCP tool {ToolName} completed with {OutcomeCode}.",
            toolName,
            outcomeCode);
    }
}
