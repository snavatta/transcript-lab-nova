using System.Net;
using System.Net.Http.Json;
using ClassTranscriber.Api.Domain;
using FluentAssertions;

namespace ClassTranscriber.Api.Tests;

public sealed class OpenRouterOrdinaryErrorTests : OpenRouterTestFixture
{
    [Fact]
    public async Task TranscribeAsync_RetriesWithJson_WhenProviderRejectsVerboseJson()
    {
        var audioPath = Path.Combine(CreateTempDirectory(), "lecture.wav");
        await File.WriteAllBytesAsync(audioPath, [1, 2, 3]);
        var responseFormats = new List<string>();
        var factory = new RecordingHttpClientFactory(request =>
        {
            var multipart = request.Content.Should().BeOfType<MultipartFormDataContent>().Subject;
            var responseFormat = ReadPartAsync(multipart, "response_format").GetAwaiter().GetResult();
            responseFormats.Add(responseFormat);
            return responseFormat == "verbose_json"
                ? new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = JsonContent.Create(new { error = new { message = "response_format 'verbose_json' is unsupported" } }),
                }
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { text = "provider fallback", usage = new { seconds = 4.2 } }),
                };
        });
        var engine = CreateEngine(factory);

        var result = await engine.TranscribeAsync(audioPath, new ProjectSettings
        {
            Engine = "OpenRouter",
            Model = "deepgram/nova-3",
            LanguageMode = "Auto",
        });

        responseFormats.Should().Equal("verbose_json", "json");
        result.PlainText.Should().Be("provider fallback");
        result.Segments.Should().ContainSingle();
        result.Segments[0].EndMs.Should().Be(4200);
        result.DurationMs.Should().Be(4200);
    }

    [Fact]
    public async Task TranscribeAsync_DoesNotRetryUnrelatedBadRequests()
    {
        var audioPath = Path.Combine(CreateTempDirectory(), "lecture.wav");
        await File.WriteAllBytesAsync(audioPath, [1, 2, 3]);
        var factory = new RecordingHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = JsonContent.Create(new { error = new { message = "Invalid language" } }),
        });
        var engine = CreateEngine(factory);

        var act = () => engine.TranscribeAsync(audioPath, new ProjectSettings
        {
            Engine = "OpenRouter",
            Model = "deepgram/nova-3",
            LanguageMode = "Fixed",
            LanguageCode = "invalid",
        });

        await act.Should().ThrowAsync<InvalidOperationException>();
        factory.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task TranscribeAsync_DoesNotLeakApiKey_WhenOpenRouterReturnsAnError()
    {
        const string apiKey = "sensitive-openrouter-key";
        var audioPath = Path.Combine(CreateTempDirectory(), "lecture.wav");
        await File.WriteAllBytesAsync(audioPath, [1, 2, 3]);
        var factory = new RecordingHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = JsonContent.Create(new { error = new { message = $"Invalid API key: {apiKey}" } }),
        });
        var engine = CreateEngine(factory, apiKey);

        var act = () => engine.TranscribeAsync(audioPath, new ProjectSettings
        {
            Engine = "OpenRouter",
            Model = "deepgram/nova-3",
            LanguageMode = "Auto",
        });

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().NotContain(apiKey);
        factory.Requests.Should().ContainSingle();
        factory.Requests[0].AuthorizationParameter.Should().Be(apiKey);
    }
}
