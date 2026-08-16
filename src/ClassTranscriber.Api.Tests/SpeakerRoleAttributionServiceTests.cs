using System.Net;
using System.Net.Http.Json;
using ClassTranscriber.Api.Contracts;
using ClassTranscriber.Api.Transcription;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ClassTranscriber.Api.Tests;

public sealed class SpeakerRoleAttributionServiceTests
{
    [Fact]
    public async Task AttributeAsync_AppliesThresholdSingleProfessorAndStudentOrder()
    {
        var service = CreateService(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                choices = new[] { new { message = new { content = "{\"assignments\":[{\"speaker\":\"Speaker 1\",\"role\":\"professor\",\"confidence\":0.94},{\"speaker\":\"Speaker 2\",\"role\":\"professor\",\"confidence\":0.81},{\"speaker\":\"Speaker 3\",\"role\":\"student\",\"confidence\":0.91},{\"speaker\":\"Speaker 4\",\"role\":\"student\",\"confidence\":0.79}]}" } } },
                usage = new { prompt_tokens = 100, completion_tokens = 20, cost = 0.0012m },
            }),
        });
        var segments = new[]
        {
            Segment("Speaker 3", 0), Segment("Speaker 1", 1000), Segment("Speaker 4", 2000), Segment("Speaker 2", 3000),
        };

        var result = await service.AttributeAsync(segments, CancellationToken.None);

        result.Status.Should().Be("Completed");
        result.Segments.Select(segment => segment.Speaker).Should().Equal("Student 1", "Professor", "Speaker 4", "Speaker 2");
        result.PromptTokens.Should().Be(100);
        result.OutputTokens.Should().Be(20);
        result.CostMicroUsd.Should().Be(1200);
    }

    [Fact]
    public async Task AttributeAsync_FailsOpenForMalformedOutput()
    {
        var service = CreateService(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { choices = new[] { new { message = new { content = "not json" } } } }),
        });
        var segments = new[] { Segment("Speaker 1", 0) };

        var result = await service.AttributeAsync(segments, CancellationToken.None);

        result.Status.Should().Be("Failed");
        result.Segments.Should().BeEquivalentTo(segments);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AttributeAsync_OversizedProviderResponse_FailsOpenSanitizedAndBounded(bool declareLength)
    {
        var oversized = new OversizedProviderResponseContent(declareLength);
        var service = CreateService(new HttpResponseMessage(HttpStatusCode.OK) { Content = oversized });
        var segments = new[] { Segment("Speaker 1", 0) };

        var result = await service.AttributeAsync(segments, CancellationToken.None);

        result.Status.Should().Be("Failed");
        result.Segments.Should().BeEquivalentTo(segments);
        oversized.Source.BytesRead.Should().Be(declareLength ? 0 : OversizedProviderResponseContent.ResponseLimitBytes + 1);
    }

    private static ClassTranscriber.Api.Contracts.TranscriptSegmentDto Segment(string speaker, long startMs) => new()
    {
        StartMs = startMs,
        EndMs = startMs + 500,
        Speaker = speaker,
        Text = "sample",
    };

    private static OpenRouterSpeakerRoleAttributionService CreateService(HttpResponseMessage response)
        => new(
            Options.Create(new OpenRouterOptions { ApiKey = "key", BaseUrl = "https://openrouter.ai/api/v1" }),
            new SingleResponseFactory(response),
            NullLogger<OpenRouterSpeakerRoleAttributionService>.Instance);

    private sealed class SingleResponseFactory(HttpResponseMessage response) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new Handler(response))
        {
            BaseAddress = new Uri("https://openrouter.ai/api/v1/"),
        };
    }

    private sealed class Handler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(response);
    }
}
