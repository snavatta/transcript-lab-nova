using ClassTranscriber.Api.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ClassTranscriber.Api.Mcp;

public static class ChatGptSourceBrowseRegistrationExtensions
{
    public static IMcpServerBuilder WithChatGptSourceTools(this IMcpServerBuilder builder)
    {
        builder.Services.AddScoped<IChatGptSourceCatalogService>(services =>
            new ChatGptSourceCatalogService(
                services.GetRequiredService<AppDbContext>(),
                services.GetRequiredService<IOptions<ChatGptSourceOptions>>().Value));
        builder.Services.AddScoped<ChatGptSourceContentService>(services =>
        {
            var options = services.GetRequiredService<IOptions<ChatGptSourceOptions>>().Value;
            var configuredBaseUrl = options.ApplicationBaseUrl;
            var applicationBaseUrl = string.IsNullOrWhiteSpace(configuredBaseUrl)
                ? null
                : new Uri(configuredBaseUrl, UriKind.Absolute);
            return new ChatGptSourceContentService(
                services.GetRequiredService<AppDbContext>(),
                applicationBaseUrl,
                options.CursorIntegrityKey);
        });
        builder.Services.AddScoped<IChatGptSourceContentToolService, ChatGptSourceContentToolService>();

        return builder
            .WithTools<ChatGptSourceBrowseTools>()
            .WithTools<SearchTranscriptsTool>()
            .WithTools<GetTranscriptTool>();
    }
}
