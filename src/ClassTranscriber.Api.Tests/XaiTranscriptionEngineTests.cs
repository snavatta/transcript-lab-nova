using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using ClassTranscriber.Api.Domain;
using ClassTranscriber.Api.Transcription;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ClassTranscriber.Api.Tests;

public sealed class XaiTranscriptionEngineTests
{
    private const long XaiMaximumFileSizeBytes = 500_000_000;

    [Fact]
    public void Availability_RequiresApiKeyAndHttps()
    {
        using var audio = new TestAudioContext();
        CreateEngine(audio, new TestFactory(_ => new(HttpStatusCode.OK)), apiKey: string.Empty)
            .GetAvailabilityError().Should().Contain("ApiKey");
        CreateEngine(audio, new TestFactory(_ => new(HttpStatusCode.OK)), baseUrl: "http://api.x.ai/v1")
            .GetAvailabilityError().Should().Contain("HTTPS");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Probe_OversizedResponseBody_IsNeverBuffered(bool declareLength)
    {
        using var audio = new TestAudioContext();
        var oversized = new OversizedProviderResponseContent(declareLength);
        var factory = new TestFactory(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = oversized });

        var error = CreateEngine(audio, factory).GetProbeError();

        error.Should().BeNull();
        oversized.Source.BytesRead.Should().Be(0);
        factory.RequestCount.Should().Be(1);
    }

    [Fact]
    public void Probe_TimeoutIsBoundedAndSanitized()
    {
        using var audio = new TestAudioContext();
        var observedCancelableToken = false;
        var factory = new TestFactory((_, ct) =>
        {
            observedCancelableToken = ct.CanBeCanceled;
            return Task.FromException<HttpResponseMessage>(
                new TaskCanceledException("provider-secret-sentinel"));
        });

        var error = CreateEngine(audio, factory, apiKey: "provider-secret-sentinel").GetProbeError();

        error.Should().Be("xAI models endpoint is not reachable.");
        error.Should().NotContain("provider-secret-sentinel");
        observedCancelableToken.Should().BeTrue();
        factory.RequestCount.Should().Be(1);
    }

    [Fact]
    public async Task DirectProviderDiarization_UsesSingleWholeFlacRequest()
    {
        using var audio = new TestAudioContext();
        var factory = new TestFactory(request =>
        {
            request.RequestUri.Should().Be("https://api.x.ai/v1/stt");
            var parts = request.Content.Should().BeOfType<MultipartFormDataContent>().Subject.ToArray();
            parts.Select(GetPartName)
                .Should().Equal("language", "format", "diarize", "filler_words", "vad_threshold", "file");
            parts[^1].Headers.ContentDisposition?.FileName?.Trim('"').Should().Be("whole.flac");
            parts[^1].Headers.ContentType?.MediaType.Should().Be("audio/flac");
            parts[^1].ReadAsByteArrayAsync().GetAwaiter().GetResult().Should().StartWith([0x66, 0x4c, 0x61, 0x43]);
            return SuccessfulSpeakerResponse();
        });

        var result = await CreateEngine(audio, factory).TranscribeAsync(audio.InputPath, ProviderSettings());

        audio.PrepareWholeCallCount.Should().Be(1);
        factory.RequestCount.Should().Be(1);
        audio.InputPath.Should().EndWith(".wav");
        File.Exists(audio.InputPath).Should().BeTrue("the project input is not a hosted temporary artifact");
        audio.PreparedPaths.Should().ContainSingle();
        audio.PreparedPaths.Should().OnlyContain(path => !File.Exists(path));
        result.PlainText.Should().Be("Hola, mundo. Sí.");
        result.DetectedLanguage.Should().Be("es-ES");
        result.Segments.Select(segment => segment.Speaker).Should().Equal("Speaker 1", "Speaker 2", "Speaker 1");
        result.Segments.Select(segment => segment.Text).Should().Equal("Hola,", "mundo.", "Sí.");
        result.Words.Select(word => word.Text).Should().Equal("Hola", ",", "mundo", ".", "Sí", ".");
        result.Words.Select(word => (word.StartMs, word.EndMs)).Should().Equal(
            (0L, 400L), (400L, 450L), (500L, 1000L), (1000L, 1100L), (2000L, 2400L), (2400L, 2500L));
        result.ProcessingMetadata.Should().NotBeNull();
        var metadata = result.ProcessingMetadata!;
        metadata.NativeDiarizationUsed.Should().BeTrue();
        metadata.RequestCount.Should().Be(1);
        metadata.SttCostClassification.Should().Be("Estimated");
        metadata.SttCostMicroUsd.Should().Be(83);
        metadata.SttRateMicroUsdPerHour.Should().Be(100_000);
        metadata.DiarizationSource.Should().BeNull();
        metadata.DiarizationProvider.Should().BeNull();
        metadata.DiarizationModel.Should().BeNull();
        metadata.DiarizationRequestCount.Should().Be(0);
        metadata.DiarizationCostMicroUsd.Should().BeNull();
    }

    [Fact]
    public async Task ExternalDiarization_UsesExactlyOneWholeFlacRequest_AndDiscardsWording()
    {
        using var audio = new TestAudioContext();
        var factory = new TestFactory(request =>
        {
            var parts = request.Content.Should().BeOfType<MultipartFormDataContent>().Subject.ToArray();
            parts.Select(GetPartName).Should().Equal("diarize", "filler_words", "vad_threshold", "file");
            parts[^1].Headers.ContentType?.MediaType.Should().Be("audio/flac");
            return SuccessfulSpeakerResponse();
        });

        var result = await CreateEngine(audio, factory).DiarizeAsync(audio.InputPath, 3_000);

        factory.RequestCount.Should().Be(1);
        audio.PrepareWholeCallCount.Should().Be(1);
        audio.PreparedPaths.Should().ContainSingle();
        audio.PreparedPaths.Should().OnlyContain(path => !File.Exists(path));
        result.Intervals.Select(interval => (interval.SpeakerId, interval.StartMs, interval.EndMs)).Should().Equal(
            ("8", 0L, 400L),
            ("8", 400L, 450L),
            ("2", 500L, 1_000L),
            ("2", 1_000L, 1_100L),
            ("8", 2_000L, 2_400L),
            ("8", 2_400L, 2_500L));
        result.RequestCount.Should().Be(1);
        result.CostMicroUsd.Should().Be(83);
        result.RateMicroUsdPerHour.Should().Be(100_000);
        result.CostClassification.Should().Be("Estimated");
        result.GetType().GetProperties().Select(property => property.Name)
            .Should().NotContain(["Text", "PlainText", "Words"]);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, 1)]
    [InlineData(HttpStatusCode.TooManyRequests, 3)]
    public async Task ExternalDiarization_HttpFailureIsSanitizedAndCleansArtifact(
        HttpStatusCode statusCode,
        int expectedRequests)
    {
        const string forbidden = "provider-secret-body";
        using var audio = new TestAudioContext();
        var factory = new TestFactory(_ =>
        {
            var response = new HttpResponseMessage(statusCode) { Content = new StringContent(forbidden) };
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
            return response;
        });

        var action = () => CreateEngine(audio, factory, apiKey: forbidden)
            .DiarizeAsync(audio.InputPath, 3_000);

        var exception = await action.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Be($"xAI transcription API returned HTTP {(int)statusCode}.");
        exception.Which.ToString().Should().NotContain(forbidden).And.NotContain(audio.InputPath);
        factory.RequestCount.Should().Be(expectedRequests);
        audio.PreparedPaths.Should().OnlyContain(path => !File.Exists(path));
    }

    [Fact]
    public async Task ExternalDiarization_InvalidResponseIsFatalSanitizedAndCleansArtifact()
    {
        using var audio = new TestAudioContext();
        var factory = new TestFactory(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                text = "discard me",
                duration = 3.0,
                words = new[] { new { word = "secret wording", start = 0.0, end = 1.0 } },
            }),
        });

        var action = () => CreateEngine(audio, factory).DiarizeAsync(audio.InputPath, 3_000);

        var exception = await action.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Be("xAI returned an invalid diarization response.");
        exception.Which.ToString().Should().NotContain("secret wording").And.NotContain(audio.InputPath);
        factory.RequestCount.Should().Be(1);
        audio.PreparedPaths.Should().OnlyContain(path => !File.Exists(path));
    }

    [Fact]
    public async Task ExternalDiarization_CancellationPropagatesAndCleansArtifact()
    {
        using var audio = new TestAudioContext();
        var providerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factory = new TestFactory(async (_, ct) =>
        {
            providerEntered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return SuccessfulSpeakerResponse();
        });
        using var cts = new CancellationTokenSource();

        var action = CreateEngine(audio, factory).DiarizeAsync(audio.InputPath, 3_000, cts.Token);
        await providerEntered.Task;
        cts.Cancel();

        Func<Task> awaitAction = async () => await action;
        await awaitAction.Should().ThrowAsync<OperationCanceledException>();
        factory.RequestCount.Should().Be(1);
        audio.PreparedPaths.Should().ContainSingle();
        audio.PreparedPaths.Should().OnlyContain(path => !File.Exists(path));
    }

    [Fact]
    public async Task LocalDiarization_UsesWholeFlacButIgnoresProviderSpeakers()
    {
        using var audio = new TestAudioContext();
        var factory = new TestFactory(request =>
        {
            var parts = request.Content.Should().BeOfType<MultipartFormDataContent>().Subject.ToArray();
            parts.Select(GetPartName).Should().NotContain("diarize");
            parts[^1].Headers.ContentType?.MediaType.Should().Be("audio/flac");
            return SuccessfulSpeakerResponse();
        });

        var result = await CreateEngine(audio, factory).TranscribeAsync(audio.InputPath, new ProjectSettings
        {
            Engine = "Xai",
            Model = XaiTranscriptionEngine.PreferredModel,
            LanguageMode = "Auto",
            DiarizationEnabled = true,
            DiarizationSource = "Local",
        });

        result.ProcessingMetadata!.NativeDiarizationUsed.Should().BeFalse();
        result.Segments.Should().OnlyContain(segment => segment.Speaker == null);
        result.Words.Should().HaveCount(6);
        factory.RequestCount.Should().Be(1);
        audio.PreparedPaths.Should().OnlyContain(path => !File.Exists(path));
    }

    [Fact]
    public async Task AutoLanguage_OmitsLanguageFields()
    {
        using var audio = new TestAudioContext();
        var factory = new TestFactory(request =>
        {
            var names = request.Content.Should().BeOfType<MultipartFormDataContent>().Subject
                .Select(GetPartName).ToArray();
            names.Should().NotContain("language").And.NotContain("format");
            names[^1].Should().Be("file");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { text = "ok", language = "en", duration = 1.0, words = Array.Empty<object>() }),
            };
        });

        await CreateEngine(audio, factory).TranscribeAsync(audio.InputPath, DefaultSettings());

        factory.RequestCount.Should().Be(1);
    }

    [Fact]
    public async Task OversizedPreparedFlac_FailsBeforeProviderAndCleansArtifact()
    {
        using var audio = new TestAudioContext { EncodedLength = XaiMaximumFileSizeBytes + 1 };
        var factory = new TestFactory(_ => throw new InvalidOperationException("provider must not be called"));

        var action = () => CreateEngine(audio, factory).TranscribeAsync(audio.InputPath, DefaultSettings());

        var exception = await action.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Be("The prepared recording exceeds xAI's 500 MB file limit.");
        factory.RequestCount.Should().Be(0);
        audio.PreparedPaths.Should().ContainSingle();
        audio.PreparedPaths.Should().OnlyContain(path => !File.Exists(path));
        File.Exists(audio.InputPath).Should().BeTrue();
    }

    [Fact]
    public async Task PreparationFailure_IsFatalSanitizedAndDoesNotCallProvider()
    {
        using var audio = new TestAudioContext { FailPreparation = true };
        var factory = new TestFactory(_ => throw new InvalidOperationException("provider must not be called"));

        var action = () => CreateEngine(audio, factory).TranscribeAsync(audio.InputPath, DefaultSettings());

        var exception = await action.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Be("xAI audio preparation failed.");
        exception.Which.InnerException.Should().BeNull();
        exception.Which.ToString().Should().NotContain(audio.InputPath);
        factory.RequestCount.Should().Be(0);
        Directory.EnumerateDirectories(audio.HostedTempRoot).Should().BeEmpty();
    }

    [Fact]
    public async Task XaiFatalErrors_AreSanitizedAndCleanFlac()
    {
        const string forbidden = "forbidden-provider-detail";
        using var unauthorizedAudio = new TestAudioContext();
        var unauthorizedFactory = new TestFactory(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent(forbidden),
        });

        var unauthorizedAction = () => CreateEngine(unauthorizedAudio, unauthorizedFactory, apiKey: forbidden)
            .TranscribeAsync(unauthorizedAudio.InputPath, DefaultSettings());
        var unauthorized = await unauthorizedAction.Should().ThrowAsync<InvalidOperationException>();

        unauthorized.Which.Message.Should().Be("xAI transcription API returned HTTP 401.");
        unauthorized.Which.ToString().Should().NotContain(forbidden).And.NotContain(unauthorizedAudio.InputPath);
        unauthorizedFactory.RequestCount.Should().Be(1);
        unauthorizedAudio.PreparedPaths.Should().OnlyContain(path => !File.Exists(path));

        using var timeoutAudio = new TestAudioContext();
        var timeoutFactory = new TestFactory((_, _) =>
            Task.FromException<HttpResponseMessage>(new TaskCanceledException($"{forbidden} {timeoutAudio.InputPath}")));

        var timeoutAction = () => CreateEngine(timeoutAudio, timeoutFactory, apiKey: forbidden)
            .TranscribeAsync(timeoutAudio.InputPath, DefaultSettings());
        var timeout = await timeoutAction.Should().ThrowAsync<InvalidOperationException>();

        timeout.Which.Message.Should().Be("xAI transcription request timed out.");
        timeout.Which.InnerException.Should().BeNull();
        timeout.Which.ToString().Should().NotContain(forbidden).And.NotContain(timeoutAudio.InputPath);
        timeoutFactory.RequestCount.Should().Be(1);
        timeoutAudio.PreparedPaths.Should().OnlyContain(path => !File.Exists(path));
    }

    [Fact]
    public async Task InvalidJson_IsFatalSanitizedAndDoesNotRetry()
    {
        using var audio = new TestAudioContext();
        var factory = new TestFactory(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("provider-body-secret {"),
        });

        var action = () => CreateEngine(audio, factory).TranscribeAsync(audio.InputPath, DefaultSettings());

        var exception = await action.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Be("xAI returned an invalid transcription response.");
        exception.Which.ToString().Should().NotContain("provider-body-secret").And.NotContain(audio.InputPath);
        factory.RequestCount.Should().Be(1);
        audio.PreparedPaths.Should().OnlyContain(path => !File.Exists(path));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OversizedProviderResponse_IsFatalSanitizedBoundedAndCleansFlac(bool declareLength)
    {
        using var audio = new TestAudioContext();
        var oversized = new OversizedProviderResponseContent(declareLength);
        var factory = new TestFactory(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = oversized });

        var action = () => CreateEngine(audio, factory, apiKey: "provider-secret-sentinel")
            .TranscribeAsync(audio.InputPath, DefaultSettings());

        var exception = await action.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Be("xAI transcription response exceeded the maximum allowed size.");
        exception.Which.ToString().Should().NotContain("provider-secret-sentinel").And.NotContain(audio.InputPath);
        oversized.Source.BytesRead.Should().Be(declareLength ? 0 : OversizedProviderResponseContent.ResponseLimitBytes + 1);
        factory.RequestCount.Should().Be(1);
        audio.PreparedPaths.Should().OnlyContain(path => !File.Exists(path));
    }

    [Fact]
    public async Task RetriesOnly429And503_UpToThreeAttempts_AndHonorsRetryAfter()
    {
        using var audio = new TestAudioContext();
        var responseNumber = 0;
        var factory = new TestFactory(_ =>
        {
            responseNumber += 1;
            if (responseNumber < 3)
            {
                var transient = new HttpResponseMessage(responseNumber == 1
                    ? HttpStatusCode.TooManyRequests
                    : HttpStatusCode.ServiceUnavailable);
                transient.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(
                    responseNumber == 1 ? TimeSpan.FromMilliseconds(20) : TimeSpan.Zero);
                return transient;
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { text = "ok", language = "en", duration = 1.0, words = Array.Empty<object>() }),
            };
        });
        var stopwatch = Stopwatch.StartNew();

        var result = await CreateEngine(audio, factory).TranscribeAsync(audio.InputPath, DefaultSettings());

        stopwatch.Stop();
        result.PlainText.Should().Be("ok");
        factory.RequestCount.Should().Be(3);
        stopwatch.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(15));
        audio.PrepareWholeCallCount.Should().Be(1, "retries must reuse one prepared whole-file request artifact");
        audio.PreparedPaths.Should().ContainSingle();
        audio.PreparedPaths.Should().OnlyContain(path => !File.Exists(path));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, 1)]
    [InlineData(HttpStatusCode.TooManyRequests, 3)]
    [InlineData(HttpStatusCode.ServiceUnavailable, 3)]
    public async Task RetryPolicy_StopsAtExactAttemptLimit(HttpStatusCode statusCode, int expectedAttempts)
    {
        using var audio = new TestAudioContext();
        var factory = new TestFactory(_ =>
        {
            var response = new HttpResponseMessage(statusCode);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
            return response;
        });

        var action = () => CreateEngine(audio, factory).TranscribeAsync(audio.InputPath, DefaultSettings());

        var exception = await action.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Be($"xAI transcription API returned HTTP {(int)statusCode}.");
        factory.RequestCount.Should().Be(expectedAttempts);
    }

    [Fact]
    public async Task DirectXai_RejectsExternalXaiDiarizationSourceBeforePreparation()
    {
        using var audio = new TestAudioContext();
        var factory = new TestFactory(_ => throw new InvalidOperationException("provider must not be called"));
        var settings = ProviderSettings();
        settings.DiarizationSource = "Xai";

        var action = () => CreateEngine(audio, factory).TranscribeAsync(audio.InputPath, settings);

        await action.Should().ThrowAsync<ArgumentException>()
            .WithMessage("Direct xAI transcription does not support Xai as an external diarization source.*");
        audio.PrepareWholeCallCount.Should().Be(0);
        factory.RequestCount.Should().Be(0);
    }

    private static string? GetPartName(HttpContent part) =>
        part.Headers.ContentDisposition?.Name?.Trim('"');

    private static HttpResponseMessage SuccessfulSpeakerResponse() => new(HttpStatusCode.OK)
    {
        Content = JsonContent.Create(new
        {
            text = "Hola, mundo. Sí.",
            language = "es-ES",
            duration = 3.0,
            words = new object[]
            {
                new { word = "Hola", start = 0.0, end = 0.4, confidence = 0.99, speaker = 8 },
                new { word = ",", start = 0.4, end = 0.45, confidence = 0.98, speaker = 8 },
                new { text = "mundo", start = 0.5, end = 1.0, confidence = 0.97, speaker = 2 },
                new { text = ".", start = 1.0, end = 1.1, confidence = 0.97, speaker = 2 },
                new { text = "Sí", start = 2.0, end = 2.4, confidence = 0.96, speaker = 8 },
                new { text = ".", start = 2.4, end = 2.5, confidence = 0.95, speaker = 8 },
            },
        }),
    };

    private static ProjectSettings DefaultSettings() => new()
    {
        Engine = "Xai",
        Model = XaiTranscriptionEngine.PreferredModel,
        LanguageMode = "Auto",
    };

    private static ProjectSettings ProviderSettings() => new()
    {
        Engine = "Xai",
        Model = XaiTranscriptionEngine.PreferredModel,
        LanguageMode = "Fixed",
        LanguageCode = "es",
        DiarizationEnabled = true,
        DiarizationSource = "Provider",
    };

    private static XaiTranscriptionEngine CreateEngine(
        TestAudioContext audio,
        TestFactory factory,
        string apiKey = "test-key",
        string baseUrl = "https://api.x.ai/v1") => new(
            Options.Create(new XaiOptions { BaseUrl = baseUrl, ApiKey = apiKey }),
            audio.PreparationService,
            factory,
            NullLogger<XaiTranscriptionEngine>.Instance);

    private sealed class TestAudioContext : IDisposable
    {
        private readonly RecordingHostedFlacEncoder _encoder;

        public TestAudioContext()
        {
            Root = Path.Combine(Path.GetTempPath(), $"xai-tests-{Guid.NewGuid():N}");
            HostedTempRoot = Path.Combine(Root, "hosted");
            Directory.CreateDirectory(Root);
            InputPath = Path.Combine(Root, "tiny.wav");
            File.WriteAllBytes(InputPath, [1, 2, 3]);
            _encoder = new RecordingHostedFlacEncoder(this);
            PreparationService = new HostedAudioPreparationService(
                _encoder,
                HostedTempRoot,
                NullLogger<HostedAudioPreparationService>.Instance);
        }

        public string Root { get; }
        public string HostedTempRoot { get; }
        public string InputPath { get; }
        public HostedAudioPreparationService PreparationService { get; }
        public long EncodedLength { get; set; } = 4;
        public bool FailPreparation { get; set; }
        public int PrepareWholeCallCount => _encoder.PrepareWholeCallCount;
        public IReadOnlyList<string> PreparedPaths => _encoder.PreparedPaths;

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }

        private sealed class RecordingHostedFlacEncoder(TestAudioContext owner) : IHostedFlacEncoder
        {
            public int PrepareWholeCallCount { get; private set; }
            public List<string> PreparedPaths { get; } = [];

            public Task EncodeWholeAsync(string inputPath, string outputPath, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                PrepareWholeCallCount += 1;
                PreparedPaths.Add(outputPath);
                if (owner.FailPreparation)
                    throw new InvalidOperationException($"sensitive preparation failure for {inputPath}");

                using var stream = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                stream.Write([0x66, 0x4c, 0x61, 0x43]);
                stream.SetLength(owner.EncodedLength);
                return Task.CompletedTask;
            }

            public Task EncodeIntervalAsync(
                string inputPath,
                string outputPath,
                HostedAudioInterval interval,
                CancellationToken ct) => throw new InvalidOperationException("xAI must not request chunks.");
        }
    }

    private sealed class TestFactory : IHttpClientFactory
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;

        public TestFactory(Func<HttpRequestMessage, HttpResponseMessage> responder)
            : this((request, _) => Task.FromResult(responder(request)))
        {
        }

        public TestFactory(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        {
            _responder = responder;
        }

        public int RequestCount { get; private set; }

        public HttpClient CreateClient(string name) => new(new TestHandler(async (request, ct) =>
        {
            RequestCount += 1;
            return await _responder(request, ct);
        }))
        {
            BaseAddress = new Uri("https://api.x.ai/v1/"),
        };
    }

    private sealed class TestHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => responder(request, cancellationToken);
    }
}
