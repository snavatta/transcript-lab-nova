using Microsoft.Extensions.Options;

namespace ClassTranscriber.Api;

public sealed class ChatGptSourceOptions
{
    public const string SectionName = "ChatGptSource";

    public bool Enabled { get; set; }

    public string? ApplicationBaseUrl { get; set; }

    public string? CursorIntegrityKey { get; set; }

    internal static bool TryNormalizeApplicationBaseUrl(string? value, out Uri? normalizedUri)
    {
        normalizedUri = null;
        if (string.IsNullOrWhiteSpace(value))
            return true;

        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        var builder = new UriBuilder(uri)
        {
            Path = uri.AbsolutePath.TrimEnd('/') + "/",
        };
        normalizedUri = builder.Uri;
        return true;
    }
}

internal sealed class ChatGptSourceOptionsValidator : IValidateOptions<ChatGptSourceOptions>
{
    public ValidateOptionsResult Validate(string? name, ChatGptSourceOptions options)
    {
        if (!options.Enabled)
            return ValidateOptionsResult.Skip;

        if (string.IsNullOrWhiteSpace(options.CursorIntegrityKey) || options.CursorIntegrityKey.Length < 32)
        {
            return ValidateOptionsResult.Fail(
                "ChatGptSource:CursorIntegrityKey must be a non-empty string of at least 32 characters when ChatGptSource is enabled.");
        }

        if (!ChatGptSourceOptions.TryNormalizeApplicationBaseUrl(options.ApplicationBaseUrl, out var uri))
        {
            return ValidateOptionsResult.Fail(
                "ChatGptSource:ApplicationBaseUrl must be an absolute HTTP or HTTPS URL without user-info, query, or fragment components when ChatGptSource is enabled.");
        }

        options.ApplicationBaseUrl = uri?.AbsoluteUri;
        return ValidateOptionsResult.Success;
    }
}
