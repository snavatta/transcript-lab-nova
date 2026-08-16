using System.Net;
using ClassTranscriber.Api.Contracts;
using ClassTranscriber.Api.Domain;
using ClassTranscriber.Api.Transcription;
using FluentAssertions;

namespace ClassTranscriber.Api.Tests;

public sealed class OpenRouterLongFormTranscriptionTests : OpenRouterTestFixture
{
    [Fact]
    public async Task LongFormVerifiedModel_RebuildsReadableTextWhenWordsLackSpaces()
    {
        var audioPath = Path.Combine(CreateTempDirectory(), "lecture.wav");
        await File.WriteAllBytesAsync(audioPath, [1]);
        var factory = new RecordingHttpClientFactory(_ => WordResponse(
            0.000001m,
            new { word = "Hola", start = 1.0, end = 1.2 },
            new { word = ",", start = 1.2, end = 1.3 },
            new { word = "¿alguien", start = 1.3, end = 1.6 },
            new { word = "me", start = 1.6, end = 1.8 },
            new { word = "escucha", start = 1.8, end = 2.1 },
            new { word = "?", start = 2.1, end = 2.2 }));
        var engine = CreateEngine(
            factory,
            preparationService: CreatePreparationService(),
            mediaInspector: new StubMediaInspector(60_000));

        var result = await engine.TranscribeAsync(audioPath, VerifiedSettings());

        result.PlainText.Should().Be("Hola, ¿alguien me escucha?");
        result.Segments.Should().ContainSingle().Which.Text.Should().Be("Hola, ¿alguien me escucha?");
    }

    [Fact]
    public async Task LongFormVerifiedModel_UsesCurlCompatibleMultipartHeaders()
    {
        var audioPath = Path.Combine(CreateTempDirectory(), "lecture.wav");
        await File.WriteAllBytesAsync(audioPath, [1]);
        var factory = new RecordingHttpClientFactory(request =>
        {
            var contentType = request.Content!.Headers.ContentType!;
            var boundary = contentType.Parameters.Single(parameter => parameter.Name == "boundary").Value;
            boundary.Should().NotBeNullOrWhiteSpace().And.NotStartWith("\"").And.NotEndWith("\"");
            var multipart = request.Content.Should().BeOfType<MultipartFormDataContent>().Subject;
            var file = multipart.Single(item => item.Headers.ContentDisposition?.Name?.Trim('"') == "file");
            multipart.Select(item => item.Headers.GetValues("Content-Disposition").Single())
                .Should().OnlyContain(value => value.Contains("name=\"", StringComparison.Ordinal));
            file.Headers.GetValues("Content-Disposition").Single()
                .Should().Be("form-data; name=\"file\"; filename=\"audio.flac\"");
            file.Headers.ContentDisposition!.FileNameStar.Should().BeNull();
            return WordResponse(0.000001m, new { word = "ok", start = 0.0, end = 1.0 });
        });
        var engine = CreateEngine(
            factory,
            preparationService: CreatePreparationService(),
            mediaInspector: new StubMediaInspector(60_000));

        var result = await engine.TranscribeAsync(audioPath, VerifiedSettings());

        result.PlainText.Should().Be("ok");
        factory.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task LongFormVerifiedModel_ChunksRebasesAndDeduplicates()
    {
        var audioPath = Path.Combine(CreateTempDirectory(), "lecture.wav");
        await File.WriteAllBytesAsync(audioPath, [1, 2, 3]);
        var requestIndex = 0;
        var requestModels = new List<string>();
        var factory = new RecordingHttpClientFactory(request =>
        {
            var multipart = request.Content.Should().BeOfType<MultipartFormDataContent>().Subject;
            ReadPartAsync(multipart, "response_format").GetAwaiter().GetResult().Should().Be("verbose_json");
            ReadPartAsync(multipart, "timestamp_granularities[]").GetAwaiter().GetResult().Should().Be("word");
            requestModels.Add(ReadPartAsync(multipart, "model").GetAwaiter().GetResult());
            var file = multipart.Single(item => item.Headers.ContentDisposition?.Name?.Trim('"') == "file");
            file.Headers.ContentType?.MediaType.Should().Be("audio/flac");
            file.Headers.ContentDisposition?.FileName?.Trim('"').Should().EndWith(".flac");
            return requestIndex++ switch
            {
                0 => WordResponse(0.000001m,
                    new { word = "A ", start = 599.0, end = 599.8 },
                    new { word = "duplicate", start = 599.8, end = 600.2 }),
                1 => WordResponse(0.000002m,
                    new { word = "duplicate", start = 1.8, end = 2.2 },
                    new { word = ", B! ", start = 2.2, end = 3.0 },
                    new { word = "next-core", start = 601.8, end = 602.2 }),
                2 => WordResponse(0.000003m,
                    new { word = "D", start = 2.0, end = 2.0 },
                    new { word = ".", start = 2.1, end = 2.1 }),
                _ => throw new InvalidOperationException("Unexpected OpenRouter request."),
            };
        });
        var progress = new List<OpenRouterChunkProgress>();
        var engine = CreateEngine(
            factory,
            preparationService: CreatePreparationService(),
            mediaInspector: new StubMediaInspector(1_201_000));

        var result = await engine.TranscribeAsync(
            audioPath,
            new ProjectSettings
            {
                Engine = "OpenRouter",
                Model = "openai/whisper-large-v3",
                LanguageMode = "Auto",
            },
            (chunk, _) =>
            {
                progress.Add(chunk);
                return ValueTask.CompletedTask;
            });

        requestModels.Should().Equal(
            "openai/whisper-large-v3",
            "openai/whisper-large-v3",
            "openai/whisper-large-v3");
        result.PlainText.Should().Be("A duplicate, B! D.");
        result.Words.Should().Equal(
            new TranscriptionWord("A ", 599_000, 599_800),
            new TranscriptionWord("duplicate", 599_800, 600_200),
            new TranscriptionWord(", B! ", 600_200, 601_000),
            new TranscriptionWord("D", 1_200_000, 1_200_000),
            new TranscriptionWord(".", 1_200_100, 1_200_100));
        result.Segments.Should().Equal(
            new ClassTranscriber.Api.Contracts.TranscriptSegmentDto
            {
                StartMs = 599_000,
                EndMs = 601_000,
                Text = "A duplicate, B!",
            },
            new ClassTranscriber.Api.Contracts.TranscriptSegmentDto
            {
                StartMs = 1_200_000,
                EndMs = 1_200_100,
                Text = "D.",
            });
        result.ProcessingMetadata!.RequestCount.Should().Be(3);
        result.ProcessingMetadata.SttCostMicroUsd.Should().Be(6);
        progress.Select(item => item.ChunkIndex).Should().Equal(0, 1, 2);
        progress.Select(item => item.CumulativeResult.ProcessingMetadata!.RequestCount).Should().Equal(1, 2, 3);
        progress.Select(item => item.CumulativeResult.ProcessingMetadata!.SttCostMicroUsd).Should().Equal(1, 3, 6);
        progress[1].CumulativeResult.PlainText.Should().Be("A duplicate, B!");
    }

    [Fact]
    public async Task LongFormVerifiedModel_CheckpointFailureStopsLaterRequests()
    {
        var audioPath = Path.Combine(CreateTempDirectory(), "lecture.wav");
        await File.WriteAllBytesAsync(audioPath, [1]);
        var factory = new RecordingHttpClientFactory(_ =>
            WordResponse(0.000001m, new { word = "first", start = 0.0, end = 1.0 }));
        var engine = CreateEngine(
            factory,
            preparationService: CreatePreparationService(),
            mediaInspector: new StubMediaInspector(1_201_000));

        var act = () => engine.TranscribeAsync(
            audioPath,
            VerifiedSettings(),
            (_, _) => ValueTask.FromException(new InvalidOperationException("checkpoint failed")));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("checkpoint failed");
        factory.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task LongFormVerifiedModel_AdaptiveSplitUsesStrictSubThresholdParts()
    {
        var audioPath = Path.Combine(CreateTempDirectory(), "lecture.wav");
        await File.WriteAllBytesAsync(audioPath, [1]);
        var encoder = new IntervalLengthEncoder(interval =>
            interval.CoreEndMs - interval.CoreStartMs >= HostedAudioPreparationService.CoreDurationMs
                ? HostedAudioPreparationService.MaximumEncodedPartBytes
                : 1);
        var preparation = CreatePreparationService(encoder: encoder);
        var requestIndex = 0;
        var factory = new RecordingHttpClientFactory(_ =>
        {
            var isFirst = requestIndex++ == 0;
            return WordResponse(
                0.000001m,
                new
                {
                    word = isFirst ? "left " : "right",
                    start = isFirst ? 1.0 : 2.0,
                    end = isFirst ? 2.0 : 3.0,
                });
        });
        var engine = CreateEngine(
            factory,
            preparationService: preparation,
            mediaInspector: new StubMediaInspector(600_000));

        var result = await engine.TranscribeAsync(audioPath, VerifiedSettings());

        encoder.Intervals.Select(interval => (interval.CoreStartMs, interval.CoreEndMs)).Should().Equal(
            (0L, 600_000L),
            (0L, 300_000L),
            (300_000L, 600_000L));
        factory.Requests.Should().HaveCount(2);
        result.ProcessingMetadata!.RequestCount.Should().Be(2);
    }
}
