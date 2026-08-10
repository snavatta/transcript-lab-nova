using System.Diagnostics;
using System.Text;
using ClassTranscriber.Api;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ClassTranscriber.Api.Tests;

public sealed class ChatGptSourceStartupTests : IDisposable
{
    private const string ConfigurationError = "ChatGptSource cursor integrity configuration is invalid.";
    private readonly string _temporaryDirectory = Path.Combine(Path.GetTempPath(), $"transcriptlab-mcp-startup-{Guid.NewGuid():N}");

    public ChatGptSourceStartupTests() => Directory.CreateDirectory(_temporaryDirectory);

    [Fact]
    public void Disabled_mode_does_not_read_a_configured_key_file()
    {
        var options = ResolveOptions(new Dictionary<string, string?>
        {
            ["ChatGptSource:Enabled"] = "false",
            ["ChatGptSource:CursorIntegrityKeyFile"] = Path.Combine(_temporaryDirectory, "does-not-exist"),
        });

        options.Enabled.Should().BeFalse();
    }

    [Fact]
    public void Enabled_mode_accepts_a_direct_key()
    {
        var key = CreateKey(32);

        var options = ResolveOptions(new Dictionary<string, string?>
        {
            ["ChatGptSource:Enabled"] = "true",
            ["ChatGptSource:CursorIntegrityKey"] = key,
        });

        options.CursorIntegrityKey.Should().Be(key);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Enabled_mode_accepts_a_file_key_with_one_final_newline(string finalNewline)
    {
        var key = CreateKey(32);
        var keyFile = WriteKeyFile(Encoding.UTF8.GetBytes(key + finalNewline));

        var options = ResolveOptions(EnabledFileConfiguration(keyFile));

        options.CursorIntegrityKey.Should().Be(key);
    }

    [Theory]
    [InlineData(31)]
    [InlineData(4097)]
    public void Enabled_mode_rejects_direct_keys_outside_byte_bounds(int length)
    {
        AssertInvalid(EnabledDirectConfiguration(CreateKey(length)));
    }

    [Theory]
    [InlineData(32)]
    [InlineData(4096)]
    public void Enabled_mode_accepts_direct_keys_at_byte_bounds(int length)
    {
        var key = CreateKey(length);

        ResolveOptions(EnabledDirectConfiguration(key)).CursorIntegrityKey.Should().Be(key);
    }

    [Theory]
    [InlineData("\r")]
    [InlineData("\n\n")]
    [InlineData("\r\n\r\n")]
    public void Enabled_mode_rejects_file_keys_with_disallowed_line_endings(string ending)
    {
        var keyFile = WriteKeyFile(Encoding.UTF8.GetBytes(CreateKey(32) + ending));

        AssertInvalid(EnabledFileConfiguration(keyFile));
    }

    [Fact]
    public void Enabled_mode_rejects_invalid_utf8_file_content()
    {
        var keyFile = WriteKeyFile([0xC3, 0x28]);

        AssertInvalid(EnabledFileConfiguration(keyFile));
    }

    [Fact]
    public void Enabled_mode_rejects_a_utf8_bom()
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(CreateKey(32))).ToArray();
        var keyFile = WriteKeyFile(bytes);

        AssertInvalid(EnabledFileConfiguration(keyFile));
    }

    [Fact]
    public void Enabled_mode_rejects_whitespace_and_nul_keys()
    {
        AssertInvalid(EnabledDirectConfiguration(new string(' ', 32)));
        AssertInvalid(EnabledDirectConfiguration(CreateKey(31) + "\0"));
    }

    [Fact]
    public void Enabled_mode_requires_exactly_one_key_source()
    {
        var keyFile = WriteKeyFile(Encoding.UTF8.GetBytes(CreateKey(32)));

        AssertInvalid(new Dictionary<string, string?> { ["ChatGptSource:Enabled"] = "true" });
        AssertInvalid(new Dictionary<string, string?>
        {
            ["ChatGptSource:Enabled"] = "true",
            ["ChatGptSource:CursorIntegrityKey"] = CreateKey(32),
            ["ChatGptSource:CursorIntegrityKeyFile"] = keyFile,
        });
        AssertInvalid(new Dictionary<string, string?>
        {
            ["ChatGptSource:Enabled"] = "true",
            ["ChatGptSource:CursorIntegrityKey"] = string.Empty,
            ["ChatGptSource:CursorIntegrityKeyFile"] = keyFile,
        });
    }

    [Fact]
    public void Enabled_mode_rejects_an_unreadable_key_file_without_leaking_its_path()
    {
        AssertInvalid(EnabledFileConfiguration(_temporaryDirectory));
    }

    [Fact]
    public async Task Enabled_mode_with_missing_key_exits_one_with_only_the_stable_error()
    {
        var storagePath = Path.Combine(_temporaryDirectory, "storage");
        var databasePath = Path.Combine(_temporaryDirectory, "startup.db");
        var startInfo = new ProcessStartInfo("dotnet")
        {
            Arguments = $"\"{typeof(ChatGptSourceOptions).Assembly.Location}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.Environment["ChatGptSource__Enabled"] = "true";
        startInfo.Environment["ChatGptSource__ApplicationBaseUrl"] = "https://example.com";
        startInfo.Environment["Storage__BasePath"] = storagePath;
        startInfo.Environment["ConnectionStrings__DefaultConnection"] = $"Data Source={databasePath}";
        startInfo.Environment["ASPNETCORE_URLS"] = "http://127.0.0.1:0";

        using var process = Process.Start(startInfo)!;
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await process.WaitForExitAsync(timeout.Token);

        process.ExitCode.Should().Be(1);
        (await standardOutput).Should().BeEmpty();
        (await standardError).Trim().Should().Be(ConfigurationError);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
            Directory.Delete(_temporaryDirectory, recursive: true);
    }

    private string WriteKeyFile(byte[] contents)
    {
        var path = Path.Combine(_temporaryDirectory, $"key-{Guid.NewGuid():N}");
        File.WriteAllBytes(path, contents);
        return path;
    }

    private static string CreateKey(int length) => new('K', length);

    private static Dictionary<string, string?> EnabledDirectConfiguration(string key) => new()
    {
        ["ChatGptSource:Enabled"] = "true",
        ["ChatGptSource:CursorIntegrityKey"] = key,
    };

    private static Dictionary<string, string?> EnabledFileConfiguration(string path) => new()
    {
        ["ChatGptSource:Enabled"] = "true",
        ["ChatGptSource:CursorIntegrityKeyFile"] = path,
    };

    private static ChatGptSourceOptions ResolveOptions(IReadOnlyDictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddChatGptSource(configuration);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<ChatGptSourceOptions>>().Value;
    }

    private static void AssertInvalid(IReadOnlyDictionary<string, string?> values)
    {
        var action = () => ResolveOptions(values);

        var exception = action.Should().Throw<OptionsValidationException>().Which;
        exception.Failures.Should().ContainSingle().Which.Should().Be(ConfigurationError);
        exception.Message.Should().NotContain("CursorIntegrityKey");
    }
}
