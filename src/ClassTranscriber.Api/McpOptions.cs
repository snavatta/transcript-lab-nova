using System.Security;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace ClassTranscriber.Api;

public sealed class McpOptions
{
    public const int DefaultPrivatePort = 5001;
    public const int PublicApplicationPort = 5000;
    public const string SectionName = "Mcp";
    public const string CursorIntegrityConfigurationError =
        "Mcp cursor integrity configuration is invalid.";
    public const string PrivatePortConfigurationError =
        "Mcp private port configuration is invalid.";

    public bool Enabled { get; set; }

    public int PrivatePort { get; set; } = DefaultPrivatePort;

    public string? ApplicationBaseUrl { get; set; }

    public string? CursorIntegrityKey { get; set; }

    public string? CursorIntegrityKeyFile { get; set; }

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

internal sealed class McpOptionsValidator : IValidateOptions<McpOptions>
{
    private const int MinimumCursorIntegrityKeyBytes = 32;
    private const int MaximumCursorIntegrityKeyBytes = 4096;
    private const int MaximumCursorIntegrityKeyFileBytes = 4099;
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public ValidateOptionsResult Validate(string? name, McpOptions options)
    {
        if (!options.Enabled)
            return ValidateOptionsResult.Skip;

        if (options.PrivatePort is < 1 or > 65535 or McpOptions.PublicApplicationPort)
            return ValidateOptionsResult.Fail(McpOptions.PrivatePortConfigurationError);

        if (!TryResolveCursorIntegrityKey(options, out var cursorIntegrityKey))
            return InvalidCursorIntegrityConfiguration();

        options.CursorIntegrityKey = cursorIntegrityKey;

        if (!McpOptions.TryNormalizeApplicationBaseUrl(options.ApplicationBaseUrl, out var uri))
            return InvalidCursorIntegrityConfiguration();

        options.ApplicationBaseUrl = uri?.AbsoluteUri;
        return ValidateOptionsResult.Success;
    }

    private static ValidateOptionsResult InvalidCursorIntegrityConfiguration() =>
        ValidateOptionsResult.Fail(McpOptions.CursorIntegrityConfigurationError);

    private static bool TryResolveCursorIntegrityKey(
        McpOptions options,
        out string? cursorIntegrityKey)
    {
        cursorIntegrityKey = null;
        var hasDirectKey = options.CursorIntegrityKey is not null;
        var hasKeyFile = options.CursorIntegrityKeyFile is not null;
        if (hasDirectKey == hasKeyFile)
            return false;

        if (hasDirectKey)
            return IsValidCursorIntegrityKey(options.CursorIntegrityKey!, out cursorIntegrityKey);

        if (!TryReadCursorIntegrityKeyFile(options.CursorIntegrityKeyFile!, out var fileKey)
            || fileKey is null)
        {
            return false;
        }

        return IsValidCursorIntegrityKey(fileKey, out cursorIntegrityKey);
    }

    private static bool TryReadCursorIntegrityKeyFile(string path, out string? cursorIntegrityKey)
    {
        cursorIntegrityKey = null;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > MaximumCursorIntegrityKeyFileBytes)
                return false;

            var bytes = new byte[(int)stream.Length];
            var read = 0;
            while (read < bytes.Length)
            {
                var count = stream.Read(bytes, read, bytes.Length - read);
                if (count == 0)
                    return false;
                read += count;
            }

            if (HasUtf8Bom(bytes))
                return false;

            if (bytes.EndsWith("\r\n"u8))
                bytes = bytes[..^2];
            else if (bytes.EndsWith("\n"u8))
                bytes = bytes[..^1];

            cursorIntegrityKey = StrictUtf8.GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
        catch (SecurityException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsValidCursorIntegrityKey(string value, out string? cursorIntegrityKey)
    {
        cursorIntegrityKey = null;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        byte[] bytes;
        try
        {
            bytes = StrictUtf8.GetBytes(value);
        }
        catch (EncoderFallbackException)
        {
            return false;
        }

        if (bytes.Length is < MinimumCursorIntegrityKeyBytes or > MaximumCursorIntegrityKeyBytes
            || HasUtf8Bom(bytes)
            || bytes.IndexOfAny((byte)0, (byte)'\r', (byte)'\n') >= 0)
        {
            return false;
        }

        cursorIntegrityKey = value;
        return true;
    }

    private static bool HasUtf8Bom(ReadOnlySpan<byte> bytes) =>
        bytes.StartsWith(Encoding.UTF8.Preamble);
}

internal static class McpStartupConfiguration
{
    public static void Validate(IConfiguration configuration)
    {
        var options = configuration.GetSection(McpOptions.SectionName).Get<McpOptions>()
            ?? new McpOptions();
        var result = new McpOptionsValidator().Validate(Options.DefaultName, options);
        if (result.Failed)
            throw new OptionsValidationException(McpOptions.SectionName, typeof(McpOptions), result.Failures);
    }
}
