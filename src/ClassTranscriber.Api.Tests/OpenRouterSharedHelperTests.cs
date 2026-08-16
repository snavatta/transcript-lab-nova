using System.Net;
using System.Net.Http.Json;
using ClassTranscriber.Api.Transcription;
using ClassTranscriber.Api.Transcription.SpeechToText;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace ClassTranscriber.Api.Tests;

public sealed class OpenRouterSharedHelperTests : OpenRouterTestFixture
{
    [Fact]
    public async Task SharedHelper_DefaultMultipartRemainsWavWithoutWordTimestampParameter()
    {
        var factory = new RecordingHttpClientFactory(request =>
        {
            var multipart = request.Content.Should().BeOfType<MultipartFormDataContent>().Subject;
            var file = multipart.Single(item => item.Headers.ContentDisposition?.Name?.Trim('"') == "file");
            file.Headers.ContentType?.MediaType.Should().Be("audio/wav");
            file.Headers.ContentDisposition?.FileName?.Trim('"').Should().Be("audio.wav");
            multipart.Any(item =>
                item.Headers.ContentDisposition?.Name?.Trim('"') == "timestamp_granularities[]").Should().BeFalse();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { text = "wav default", duration = 1.0 }),
            };
        });
        using var client = new OpenAiCompatibleSpeechToTextClient(factory);
        await using var audio = new MemoryStream([1, 2, 3]);

        var response = await client.GetTextAsync(audio, cancellationToken: CancellationToken.None);

        response.Text.Should().Be("wav default");
        factory.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task SharedHelper_OpenVinoMultipartRemainsWavWithoutWordTimestampParameter()
    {
        var factory = new RecordingHttpClientFactory(request =>
        {
            var multipart = request.Content.Should().BeOfType<MultipartFormDataContent>().Subject;
            var file = multipart.Single(item => item.Headers.ContentDisposition?.Name?.Trim('"') == "file");
            file.Headers.ContentType?.MediaType.Should().Be("audio/wav");
            file.Headers.ContentDisposition?.FileName?.Trim('"').Should().Be("audio.wav");
            ReadPartAsync(multipart, "device").GetAwaiter().GetResult().Should().Be("CPU");
            multipart.Any(item =>
                item.Headers.ContentDisposition?.Name?.Trim('"') == "timestamp_granularities[]").Should().BeFalse();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { text = "openvino wav default", duration = 1.0 }),
            };
        });
        var manager = new StubOpenVinoSidecarManager();
        using var client = new OpenVinoSidecarSpeechToTextClient(
            factory,
            manager,
            Options.Create(new OpenVinoWhisperSidecarOptions { Device = "CPU" }));
        await using var audio = new MemoryStream([1, 2, 3]);

        var response = await client.GetTextAsync(audio, cancellationToken: CancellationToken.None);

        response.Text.Should().Be("openvino wav default");
        factory.Requests.Should().ContainSingle();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SharedHelper_OversizedProviderResponse_IsFatalSanitizedAndBounded(bool declareLength)
    {
        var oversized = new OversizedProviderResponseContent(declareLength);
        var factory = new RecordingHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = oversized,
        });
        using var client = new OpenAiCompatibleSpeechToTextClient(factory);
        await using var audio = new MemoryStream([1, 2, 3]);

        var action = () => client.GetTextAsync(audio, cancellationToken: CancellationToken.None);

        var exception = await action.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Be(
            "OpenAI-compatible transcription API response exceeded the maximum allowed size.");
        exception.Which.ToString().Should().NotContain("provider-secret-sentinel");
        oversized.Source.BytesRead.Should().Be(declareLength ? 0 : OversizedProviderResponseContent.ResponseLimitBytes + 1);
        factory.Requests.Should().ContainSingle();
    }
}
