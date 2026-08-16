using System.Net;
using System.Net.Http.Json;
using ClassTranscriber.Api.Domain;
using ClassTranscriber.Api.Transcription;
using FluentAssertions;

namespace ClassTranscriber.Api.Tests;

public sealed class OpenRouterLongFormFailureTests : OpenRouterTestFixture
{
    [Fact]
    public async Task LongFormBoundaryFailures_AreFailClosed()
    {
        var audioPath = Path.Combine(CreateTempDirectory(), "private-lecture.wav");
        await File.WriteAllBytesAsync(audioPath, [1]);

        var exactlyThresholdFactory = new RecordingHttpClientFactory(_ =>
            throw new InvalidOperationException("The threshold-sized chunk must not be sent."));
        var thresholdEncoder = new IntervalLengthEncoder(_ =>
            HostedAudioPreparationService.MaximumEncodedPartBytes);
        var thresholdEngine = CreateEngine(
            exactlyThresholdFactory,
            preparationService: CreatePreparationService(encoder: thresholdEncoder),
            mediaInspector: new StubMediaInspector(60_000));
        var thresholdAct = () => thresholdEngine.TranscribeAsync(audioPath, VerifiedSettings());
        var thresholdError = await thresholdAct.Should().ThrowAsync<HostedAudioPreparationException>();
        thresholdError.Which.Message.Should().NotContain(audioPath);
        thresholdEncoder.Intervals.Should().ContainSingle().Which.Should().Be(
            new HostedAudioInterval(0, 60_000, 0, 60_000, isFinal: true));
        exactlyThresholdFactory.Requests.Should().BeEmpty();

        var missingWordsFactory = new RecordingHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { text = "provider wording", usage = new { cost = 0.000001m } }),
        });
        var missingWordsEngine = CreateEngine(
            missingWordsFactory,
            preparationService: CreatePreparationService(),
            mediaInspector: new StubMediaInspector(60_000));
        var missingWordsAct = () => missingWordsEngine.TranscribeAsync(audioPath, VerifiedSettings());
        var missingWordsError = await missingWordsAct.Should().ThrowAsync<InvalidOperationException>();
        missingWordsError.Which.Message.Should().Be("OpenRouter did not return usable word timestamps.");
        missingWordsError.Which.Message.Should().NotContain("provider wording").And.NotContain(audioPath);

        var timeoutFactory = new RecordingHttpClientFactory((_, _) =>
            Task.FromException<HttpResponseMessage>(new TaskCanceledException("sentinel-provider-timeout-detail")));
        var timeoutEngine = CreateEngine(
            timeoutFactory,
            preparationService: CreatePreparationService(),
            mediaInspector: new StubMediaInspector(60_000));
        var timeoutAct = () => timeoutEngine.TranscribeAsync(audioPath, VerifiedSettings());
        var timeoutError = await timeoutAct.Should().ThrowAsync<InvalidOperationException>();
        timeoutError.Which.Message.Should().Be("OpenRouter transcription timed out.");
        timeoutError.Which.ToString().Should().NotContain("sentinel-provider-timeout-detail");
        timeoutFactory.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task LongFormVerifiedModel_RetriesOnly429And503_UpToThreeAttempts()
    {
        var audioPath = Path.Combine(CreateTempDirectory(), "lecture.wav");
        await File.WriteAllBytesAsync(audioPath, [1]);
        var responseIndex = 0;
        var factory = new RecordingHttpClientFactory(_ =>
        {
            responseIndex++;
            if (responseIndex < 3)
            {
                var transient = new HttpResponseMessage(responseIndex == 1
                    ? HttpStatusCode.TooManyRequests
                    : HttpStatusCode.ServiceUnavailable);
                transient.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
                transient.Content = JsonContent.Create(new { error = new { message = "do not expose" } });
                return transient;
            }

            return WordResponse(0.000004m, new { word = "ok", start = 0.0, end = 1.0 });
        });
        var progress = new List<OpenRouterChunkProgress>();
        var engine = CreateEngine(
            factory,
            preparationService: CreatePreparationService(),
            mediaInspector: new StubMediaInspector(60_000));

        var result = await engine.TranscribeAsync(
            audioPath,
            VerifiedSettings(),
            (chunk, _) =>
            {
                progress.Add(chunk);
                return ValueTask.CompletedTask;
            });

        factory.Requests.Should().HaveCount(3);
        result.ProcessingMetadata!.RequestCount.Should().Be(1);
        result.ProcessingMetadata.SttCostMicroUsd.Should().Be(4);
        progress.Should().ContainSingle();
    }

    [Fact]
    public async Task LongFormVerifiedModel_CallerCancellationIsNotWrappedAsProviderTimeout()
    {
        var audioPath = Path.Combine(CreateTempDirectory(), "lecture.wav");
        await File.WriteAllBytesAsync(audioPath, [1]);
        using var cancellation = new CancellationTokenSource();
        var factory = new RecordingHttpClientFactory((_, requestCancellation) =>
        {
            cancellation.Cancel();
            return Task.FromCanceled<HttpResponseMessage>(requestCancellation);
        });
        var engine = CreateEngine(
            factory,
            preparationService: CreatePreparationService(),
            mediaInspector: new StubMediaInspector(60_000));

        var act = () => engine.TranscribeAsync(audioPath, VerifiedSettings(), cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        factory.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task LongFormVerifiedModel_StopsAfterThreeTransientAttempts_AndDoesNotRetryOtherStatuses()
    {
        var audioPath = Path.Combine(CreateTempDirectory(), "lecture.wav");
        await File.WriteAllBytesAsync(audioPath, [1]);
        var transientFactory = new RecordingHttpClientFactory(_ =>
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var transientEngine = CreateEngine(
            transientFactory,
            preparationService: CreatePreparationService(),
            mediaInspector: new StubMediaInspector(60_000));

        var transientAct = () => transientEngine.TranscribeAsync(audioPath, VerifiedSettings());

        await transientAct.Should().ThrowAsync<InvalidOperationException>();
        transientFactory.Requests.Should().HaveCount(3);

        var serverErrorFactory = new RecordingHttpClientFactory(_ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var serverErrorEngine = CreateEngine(
            serverErrorFactory,
            preparationService: CreatePreparationService(),
            mediaInspector: new StubMediaInspector(60_000));

        var serverErrorAct = () => serverErrorEngine.TranscribeAsync(audioPath, VerifiedSettings());

        await serverErrorAct.Should().ThrowAsync<InvalidOperationException>();
        serverErrorFactory.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task LongFormVerifiedModel_ClassifiesBadRequestWithoutLeakingProviderBody()
    {
        const string privateProviderText = "timestamp_granularities[] is not supported; private transcript sentinel";
        var audioPath = Path.Combine(CreateTempDirectory(), "lecture.wav");
        await File.WriteAllBytesAsync(audioPath, [1]);
        var factory = new RecordingHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = JsonContent.Create(new { error = new { message = privateProviderText } }),
        });
        var engine = CreateEngine(
            factory,
            preparationService: CreatePreparationService(),
            mediaInspector: new StubMediaInspector(60_000));

        var act = () => engine.TranscribeAsync(audioPath, VerifiedSettings());

        var error = await act.Should().ThrowAsync<InvalidOperationException>();
        error.Which.Message.Should().Be("OpenRouter rejected word timestamps (HTTP 400).");
        error.Which.ToString().Should().NotContain(privateProviderText).And.NotContain(audioPath);
        factory.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task LongFormVerifiedModel_ClassifiesNestedProviderMetadataWithoutLeakingIt()
    {
        const string privateProviderText = "File format FLAC is not supported; private transcript sentinel";
        var audioPath = Path.Combine(CreateTempDirectory(), "lecture.wav");
        await File.WriteAllBytesAsync(audioPath, [1]);
        var factory = new RecordingHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = JsonContent.Create(new
            {
                error = new
                {
                    message = "Provider returned error",
                    metadata = new { raw = $"{{\"error\":{{\"message\":\"{privateProviderText}\"}}}}" },
                },
            }),
        });
        var engine = CreateEngine(
            factory,
            preparationService: CreatePreparationService(),
            mediaInspector: new StubMediaInspector(60_000));

        var act = () => engine.TranscribeAsync(audioPath, VerifiedSettings());

        var error = await act.Should().ThrowAsync<InvalidOperationException>();
        error.Which.Message.Should().Be("OpenRouter rejected the FLAC audio format (HTTP 400).");
        error.Which.ToString().Should().NotContain(privateProviderText).And.NotContain(audioPath);
        factory.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task LongFormVerifiedModel_ClassifiesInvalidMultipartBodyWithoutLeakingIt()
    {
        const string privateProviderText = "Invalid multipart/form-data body";
        var audioPath = Path.Combine(CreateTempDirectory(), "lecture.wav");
        await File.WriteAllBytesAsync(audioPath, [1]);
        var factory = new RecordingHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = JsonContent.Create(new { error = new { code = 400, message = privateProviderText } }),
        });
        var engine = CreateEngine(
            factory,
            preparationService: CreatePreparationService(),
            mediaInspector: new StubMediaInspector(60_000));

        var act = () => engine.TranscribeAsync(audioPath, VerifiedSettings());

        var error = await act.Should().ThrowAsync<InvalidOperationException>();
        error.Which.Message.Should().Be("OpenRouter rejected multipart upload framing (HTTP 400).");
        error.Which.ToString().Should().NotContain(privateProviderText).And.NotContain(audioPath);
        factory.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task XaiWordPath_RejectsNonAllowlistedModel_WithoutFilteringOrdinaryCatalog()
    {
        var factory = new RecordingHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                data = new[]
                {
                    new { id = "deepgram/nova-3" },
                    new { id = "openai/whisper-large-v3" },
                },
            }),
        });
        var engine = CreateEngine(factory);
        engine.SupportedModels.Should().Contain("deepgram/nova-3");
        var audioPath = Path.Combine(CreateTempDirectory(), "lecture.wav");
        await File.WriteAllBytesAsync(audioPath, [1]);

        var act = () => engine.TranscribeAsync(audioPath, new ProjectSettings
        {
            Engine = "OpenRouter",
            Model = "deepgram/nova-3",
            LanguageMode = "Auto",
            DiarizationEnabled = true,
            DiarizationSource = "Xai",
        });

        var error = await act.Should().ThrowAsync<InvalidOperationException>();
        error.Which.Message.Should().Be("OpenRouter word timestamps require a verified model.");
        factory.Requests.Should().ContainSingle();
        factory.Requests[0].RequestUri.Should().Contain("models?output_modalities=transcription");
    }
}
