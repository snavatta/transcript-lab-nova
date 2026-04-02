using System.Reflection;
using ClassTranscriber.Api.Transcription;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ClassTranscriber.Api.Tests;

/// <summary>
/// Unit tests for WhisperCliTranscriptionEngine's internal parsing logic.
/// </summary>
public class WhisperParsingTests
{
    [Theory]
    [InlineData("00:00:00.000", 0)]
    [InlineData("00:00:01.000", 1000)]
    [InlineData("00:01:30.500", 90500)]
    [InlineData("01:02:03.456", 3723456)]
    [InlineData("00:00:00,500", 500)]
    [InlineData("00:00:05", 5000)]
    [InlineData("invalid", 0)]
    [InlineData("", 0)]
    public void ParseTimestampMs_ParsesCorrectly(string input, long expectedMs)
    {
        // Use reflection to access the private static method
        var method = typeof(WhisperCliTranscriptionEngine)
            .GetMethod("ParseTimestampMs", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull("ParseTimestampMs should exist as a private static method");

        var result = (long)method!.Invoke(null, [input])!;
        result.Should().Be(expectedMs);
    }

    [Fact]
    public void WhisperOptions_HasCorrectDefaults()
    {
        var options = new WhisperOptions();
        options.WhisperCliPath.Should().Be("whisper-cli");
        options.ModelsPath.Should().Be("/data/models");
        options.AutoDownloadModels.Should().BeTrue();
        options.ModelDownloadBaseUrl.Should().Be("https://huggingface.co/ggerganov/whisper.cpp/resolve/main");
    }

    [Fact]
    public async Task ResolveModelPathAsync_DownloadsMissingModel_WhenAutoDownloadEnabled()
    {
        var modelsPath = Path.Combine(Path.GetTempPath(), $"whisper-models-{Guid.NewGuid():N}");
        var options = Options.Create(new WhisperOptions
        {
            ModelsPath = modelsPath,
            AutoDownloadModels = true,
            ModelDownloadBaseUrl = "https://example.test/models"
        });

        var downloadedBytes = "fake whisper model"u8.ToArray();
        var engine = new WhisperCliTranscriptionEngine(
            options,
            new StubHttpClientFactory(new StubHttpMessageHandler(_ =>
                new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(downloadedBytes)
                })),
            NullLogger<WhisperCliTranscriptionEngine>.Instance);

        var method = typeof(WhisperCliTranscriptionEngine)
            .GetMethod("ResolveModelPathAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull();

        try
        {
            var task = (Task<string>)method!.Invoke(engine, ["medium", CancellationToken.None])!;
            var resolvedPath = await task;
            var savedBytes = await File.ReadAllBytesAsync(resolvedPath);

            resolvedPath.Should().Be(Path.Combine(modelsPath, "ggml-medium.bin"));
            File.Exists(resolvedPath).Should().BeTrue();
            savedBytes.Should().BeEquivalentTo(downloadedBytes);
        }
        finally
        {
            if (Directory.Exists(modelsPath))
                Directory.Delete(modelsPath, recursive: true);
        }
    }

    [Fact]
    public async Task ResolveModelPathAsync_LogsDownloadProgress()
    {
        var modelsPath = Path.Combine(Path.GetTempPath(), $"whisper-models-{Guid.NewGuid():N}");
        var options = Options.Create(new WhisperOptions
        {
            ModelsPath = modelsPath,
            AutoDownloadModels = true,
            ModelDownloadBaseUrl = "https://example.test/models"
        });

        var downloadedBytes = new byte[128 * 1024];
        Array.Fill(downloadedBytes, (byte)42);
        var logger = new ListLogger<WhisperCliTranscriptionEngine>();
        var engine = new WhisperCliTranscriptionEngine(
            options,
            new StubHttpClientFactory(new StubHttpMessageHandler(_ =>
                new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(downloadedBytes)
                })),
            logger);

        var method = typeof(WhisperCliTranscriptionEngine)
            .GetMethod("ResolveModelPathAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull();

        try
        {
            var task = (Task<string>)method!.Invoke(engine, ["medium", CancellationToken.None])!;
            _ = await task;

            logger.Messages.Should().Contain(message => message.Contains("Downloading Whisper model medium. size=", StringComparison.Ordinal));
            logger.Messages.Should().Contain(message => message.Contains("Downloading Whisper model medium: 10%", StringComparison.Ordinal));
            logger.Messages.Should().Contain(message => message.Contains("Downloading Whisper model medium: completed.", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(modelsPath))
                Directory.Delete(modelsPath, recursive: true);
        }
    }
}

internal sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}

internal sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(responseFactory(request));
}

internal sealed class ListLogger<T> : ILogger<T>
{
    public List<string> Messages { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Messages.Add(formatter(state, exception));
    }
}
