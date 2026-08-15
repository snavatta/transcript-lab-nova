using ClassTranscriber.Api.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ClassTranscriber.Api.Mcp;

public static class McpBrowseRegistrationExtensions
{
    public static IMcpServerBuilder WithMcpTools(this IMcpServerBuilder builder)
    {
        builder.Services.AddScoped<IMcpCatalogService>(services =>
            new McpCatalogService(
                services.GetRequiredService<AppDbContext>(),
                services.GetRequiredService<IOptions<McpOptions>>().Value));
        builder.Services.AddScoped<McpContentService>(services =>
        {
            var options = services.GetRequiredService<IOptions<McpOptions>>().Value;
            var configuredBaseUrl = options.ApplicationBaseUrl;
            var applicationBaseUrl = string.IsNullOrWhiteSpace(configuredBaseUrl)
                ? null
                : new Uri(configuredBaseUrl, UriKind.Absolute);
            return new McpContentService(
                services.GetRequiredService<AppDbContext>(),
                applicationBaseUrl,
                options.CursorIntegrityKey);
        });
        builder.Services.AddScoped<IMcpContentToolService, McpContentToolService>();

        return builder
            .WithTools<McpBrowseTools>()
            .WithTools<SearchTranscriptsTool>()
            .WithTools<GetTranscriptTool>();
    }
}
