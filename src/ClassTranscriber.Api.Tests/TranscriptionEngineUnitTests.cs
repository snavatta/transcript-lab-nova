using System.Diagnostics;
using System.Net;
using ClassTranscriber.Api.Contracts;
using ClassTranscriber.Api.Domain;
using ClassTranscriber.Api.Transcription;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ApiSherpaOnnxBackend = ClassTranscriber.Api.Transcription.SherpaOnnxBackend;
using ApiSherpaOnnxWorkerRequest = ClassTranscriber.Api.Transcription.SherpaOnnxWorkerRequest;
using ApiSherpaOnnxWorkerResponse = ClassTranscriber.Api.Transcription.SherpaOnnxWorkerResponse;
using ApiTranscriptSegmentDto = ClassTranscriber.Api.Contracts.TranscriptSegmentDto;

namespace ClassTranscriber.Api.Tests;

public class TranscriptionEngineUnitTests
{
    [Fact]
    public async Task WhisperNetCpuEngine_UsesCpuWorkerMode_ForAutoLanguage()
    {
        var runner = new RecordingWhisperNetWorkerRunner();
        var workerPath = CreateTempWorkerFile("whisper-worker.exe");
        var engine = new WhisperNetCpuTranscriptionEngine(
            Options.Create(new WhisperNetOptions
            {
                WorkerPath = workerPath,
                LogSegments = true,
            }),
            runner,
            NullLogger<WhisperNetCpuTranscriptionEngine>.Instance);

        await engine.TranscribeAsync("/tmp/audio.wav", new ProjectSettings
        {
            Engine = "WhisperNet",
            Model = "small",
            LanguageMode = "Auto",
        });

        runner.Requests.Should().ContainSingle();
        runner.Requests[0].Mode.Should().Be(WhisperNetWorkerMode.Cpu);
        runner.Requests[0].LanguageMode.Should().Be("Auto");
        runner.Requests[0].LanguageCode.Should().BeNull();
        runner.Requests[0].LogSegments.Should().BeTrue();
    }

    [Fact]
    public async Task WhisperNetCudaEngine_UsesCudaWorkerMode_ForAutoLanguage()
    {
        var runner = new RecordingWhisperNetWorkerRunner();
        var workerPath = CreateTempWorkerFile("whisper-cuda.exe");
        var engine = new WhisperNetCudaTranscriptionEngine(
            Options.Create(new WhisperNetOptions
            {
                WorkerPath = workerPath,
            }),
            runner,
            new AvailableCudaEnvironmentProbe(),
            NullLogger<WhisperNetCudaTranscriptionEngine>.Instance);

        await engine.TranscribeAsync("/tmp/audio.wav", new ProjectSettings
        {
            Engine = "WhisperNetCuda",
            Model = "small",
            LanguageMode = "Auto",
        });

        runner.Requests.Should().ContainSingle();
        runner.Requests[0].Mode.Should().Be(WhisperNetWorkerMode.Cuda);
        runner.Requests[0].LanguageMode.Should().Be("Auto");
        runner.Requests[0].LanguageCode.Should().BeNull();
    }

    [Fact]
    public void SherpaOnnxEngine_OnlyAdvertisesModelsWithInstalledAssets()
    {
        var modelsRoot = CreateTempDirectory();
        var smallDir = Path.Combine(modelsRoot, "small");
        Directory.CreateDirectory(smallDir);
        File.WriteAllText(Path.Combine(smallDir, "config.json"), """
        {
          "backend": "whisper",
          "encoder": "small-encoder.onnx",
          "decoder": "small-decoder.onnx",
          "tokens": "small-tokens.txt"
        }
        """);
        File.WriteAllText(Path.Combine(smallDir, "small-encoder.onnx"), "x");
        File.WriteAllText(Path.Combine(smallDir, "small-decoder.onnx"), "x");
        File.WriteAllText(Path.Combine(smallDir, "small-tokens.txt"), "x");

        var mediumDir = Path.Combine(modelsRoot, "medium");
        Directory.CreateDirectory(mediumDir);
        File.WriteAllText(Path.Combine(mediumDir, "config.json"), """
        {
          "backend": "whisper",
          "encoder": "missing-encoder.onnx",
          "decoder": "missing-decoder.onnx",
          "tokens": "missing-tokens.txt"
        }
        """);

        var engine = new SherpaOnnxTranscriptionEngine(
            Options.Create(new SherpaOnnxOptions
            {
                ModelsPath = modelsRoot,
                AutoDownloadModels = false,
                WorkerPath = CreateTempWorkerFile("sherpa-worker.exe"),
            }),
            new StubHttpClientFactory(_ => throw new InvalidOperationException("HTTP should not be used.")),
            new RecordingSherpaOnnxWorkerRunner(),
            NullLogger<SherpaOnnxTranscriptionEngine>.Instance);

        engine.SupportedModels.Should().Equal("small");
    }

    [Fact]
    public void WhisperNetCudaEngine_RemainsSelectableEvenWhenProbeFails()
    {
        var engine = new WhisperNetCudaTranscriptionEngine(
            Options.Create(new WhisperNetOptions
            {
                WorkerPath = CreateTempWorkerFile("whisper-cuda.exe"),
            }),
            new RecordingWhisperNetWorkerRunner(),
            new FailingCudaEnvironmentProbe("missing cuda"),
            NullLogger<WhisperNetCudaTranscriptionEngine>.Instance);

        engine.GetAvailabilityError().Should().BeNull();
    }

    [Fact]
    public async Task WhisperNetCudaEngine_FailsTranscription_WhenProbeFails()
    {
        var runner = new RecordingWhisperNetWorkerRunner();
        var engine = new WhisperNetCudaTranscriptionEngine(
            Options.Create(new WhisperNetOptions
            {
                WorkerPath = CreateTempWorkerFile("whisper-cuda.exe"),
            }),
            runner,
            new FailingCudaEnvironmentProbe("missing cuda"),
            NullLogger<WhisperNetCudaTranscriptionEngine>.Instance);

        var act = () => engine.TranscribeAsync("/tmp/audio.wav", new ProjectSettings
        {
            Engine = "WhisperNetCuda",
            Model = "small",
            LanguageMode = "Auto",
        });

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*missing cuda*");
        runner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task WhisperNetWorkerRunner_ParsesFramedJson_WhenStdoutContainsNativeNoise()
    {
        var workerPath = CreateTempShellWorker("""
            #!/bin/sh
            cat >/dev/null
            printf 'info: OpenVINO runtime initialized\n'
            printf '%s\n' '__TRANSCRIPTLAB_WHISPERNET_RESPONSE_BEGIN__'
            printf '%s\n' '{"plainText":"ok","segments":[{"startMs":0,"endMs":10,"text":"ok","speaker":null}],"detectedLanguage":"en","durationMs":10}'
            printf '%s\n' '__TRANSCRIPTLAB_WHISPERNET_RESPONSE_END__'
            """);

        var runner = new WhisperNetWorkerRunner(NullLogger<WhisperNetWorkerRunner>.Instance);
        var response = await runner.RunAsync(new WhisperNetWorkerRequest
        {
            Mode = WhisperNetWorkerMode.Cpu,
            AudioPath = "/tmp/audio.wav",
            Model = "small",
            LanguageMode = "Auto",
            ModelsPath = "/tmp/models",
            AutoDownloadModels = false,
            LogSegments = false,
        }, workerPath, dotNetHostPath: null);

        response.PlainText.Should().Be("ok");
        response.DetectedLanguage.Should().Be("en");
        response.DurationMs.Should().Be(10);
        response.Segments.Should().ContainSingle();
        response.Segments[0].Text.Should().Be("ok");
    }

    [Fact]
    public async Task OpenVinoGenAiEngine_BuildsWorkerRequest_ForFixedLanguage()
    {
        var runner = new RecordingOpenVinoGenAiWorkerRunner();
        var workerScriptPath = CreateTempWorkerFile("openvino-genai-worker.py");
        var pythonPath = CreateTempWorkerFile("python3");
        var modelsRoot = CreateTempDirectory();
        var modelPath = Path.Combine(modelsRoot, "base-int8");
        Directory.CreateDirectory(modelPath);
        foreach (var relativePath in OpenVinoGenAiModelCatalog.GetRequired("base-int8").RequiredFiles)
        {
            var fullPath = Path.Combine(modelPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, "x");
        }

        var engine = new OpenVinoGenAiTranscriptionEngine(
            Options.Create(new OpenVinoGenAiOptions
            {
                ModelsPath = modelsRoot,
                PythonPath = pythonPath,
                WorkerScriptPath = workerScriptPath,
                Device = "GPU.1",
                LogSegments = true,
                AutoDownloadModels = false,
            }),
            new StubHttpClientFactory(_ => throw new InvalidOperationException("HTTP should not be used.")),
            runner,
            new AvailableOpenVinoGenAiEnvironmentProbe(),
            NullLogger<OpenVinoGenAiTranscriptionEngine>.Instance);

        await engine.TranscribeAsync("/tmp/audio.wav", new ProjectSettings
        {
            Engine = "OpenVinoGenAi",
            Model = "base-int8",
            LanguageMode = "Fixed",
            LanguageCode = "en",
        });

        runner.Requests.Should().ContainSingle();
        runner.Requests[0].Model.Should().Be("base-int8");
        runner.Requests[0].ModelPath.Should().Be(modelPath);
        runner.Requests[0].Device.Should().Be("GPU.1");
        runner.Requests[0].LanguageMode.Should().Be("Fixed");
        runner.Requests[0].LanguageCode.Should().Be("en");
        runner.Requests[0].LogSegments.Should().BeTrue();
    }

    [Fact]
    public async Task OpenVinoGenAiWorkerRunner_ParsesFramedJson_WhenStdoutContainsRuntimeNoise()
    {
        var workerPath = CreateTempShellWorker("""
            #!/bin/sh
            cat >/dev/null
            printf 'info: OpenVINO GenAI initialized\n'
            printf '%s\n' '__TRANSCRIPTLAB_OPENVINO_GENAI_RESPONSE_BEGIN__'
            printf '%s\n' '{"plainText":"ok","segments":[{"startMs":0,"endMs":10,"text":"ok","speaker":null}],"detectedLanguage":"en","durationMs":10}'
            printf '%s\n' '__TRANSCRIPTLAB_OPENVINO_GENAI_RESPONSE_END__'
            """);

        var runner = new OpenVinoGenAiWorkerRunner(NullLogger<OpenVinoGenAiWorkerRunner>.Instance);
        var response = await runner.RunAsync(new OpenVinoGenAiWorkerRequest
        {
            AudioPath = "/tmp/audio.wav",
            Model = "base-int8",
            ModelPath = "/tmp/models/base-int8",
            Device = "GPU",
            LanguageMode = "Auto",
            LanguageCode = null,
            LogSegments = false,
        }, workerPath, workerPath);

        response.PlainText.Should().Be("ok");
        response.DetectedLanguage.Should().Be("en");
        response.DurationMs.Should().Be(10);
        response.Segments.Should().ContainSingle();
        response.Segments[0].Text.Should().Be("ok");
    }

    [Fact]
    public async Task SherpaOnnxEngine_BuildsWorkerRequest_FromResolvedModel()
    {
        var modelsRoot = CreateTempDirectory();
        var modelDir = Path.Combine(modelsRoot, "small");
        Directory.CreateDirectory(modelDir);
        File.WriteAllText(Path.Combine(modelDir, "config.json"), """
        {
          "backend": "whisper",
          "encoder": "small-encoder.onnx",
          "decoder": "small-decoder.onnx",
          "tokens": "small-tokens.txt",
          "use_itn": false
        }
        """);
        File.WriteAllText(Path.Combine(modelDir, "small-encoder.onnx"), "x");
        File.WriteAllText(Path.Combine(modelDir, "small-decoder.onnx"), "x");
        File.WriteAllText(Path.Combine(modelDir, "small-tokens.txt"), "x");

        var runner = new RecordingSherpaOnnxWorkerRunner();
        var engine = new SherpaOnnxTranscriptionEngine(
            Options.Create(new SherpaOnnxOptions
            {
                ModelsPath = modelsRoot,
                Provider = "cpu",
                NumThreads = 6,
                LogSegments = true,
                AutoDownloadModels = false,
                WorkerPath = CreateTempWorkerFile("sherpa-worker.exe"),
            }),
            new StubHttpClientFactory(_ => throw new InvalidOperationException("HTTP should not be used.")),
            runner,
            NullLogger<SherpaOnnxTranscriptionEngine>.Instance);

        await engine.TranscribeAsync("/tmp/audio.wav", new ProjectSettings
        {
            Engine = "SherpaOnnx",
            Model = "small",
            LanguageMode = "Fixed",
            LanguageCode = "es",
        });

        runner.Requests.Should().ContainSingle();
        runner.Requests[0].Backend.Should().Be(ApiSherpaOnnxBackend.Whisper);
        runner.Requests[0].Provider.Should().Be("cpu");
        runner.Requests[0].NumThreads.Should().Be(6);
        runner.Requests[0].LanguageCode.Should().Be("es");
        runner.Requests[0].LogSegments.Should().BeTrue();
        runner.Requests[0].UseInverseTextNormalization.Should().BeTrue();
    }

    [Fact]
    public async Task SherpaOnnxSenseVoiceEngine_BuildsWorkerRequest_FromResolvedModel()
    {
        var modelsRoot = CreateTempDirectory();
        var modelDir = Path.Combine(modelsRoot, "small");
        Directory.CreateDirectory(modelDir);
        File.WriteAllText(Path.Combine(modelDir, "config.json"), """
        {
          "backend": "sense_voice",
          "model": "model.int8.onnx",
          "tokens": "tokens.txt",
          "use_itn": true
        }
        """);
        File.WriteAllText(Path.Combine(modelDir, "model.int8.onnx"), "x");
        File.WriteAllText(Path.Combine(modelDir, "tokens.txt"), "x");

        var runner = new RecordingSherpaOnnxWorkerRunner();
        var engine = new SherpaOnnxSenseVoiceTranscriptionEngine(
            Options.Create(new SherpaOnnxSenseVoiceOptions
            {
                ModelsPath = modelsRoot,
                Provider = "cpu",
                NumThreads = 3,
                AutoDownloadModels = false,
                WorkerPath = CreateTempWorkerFile("sherpa-worker.exe"),
            }),
            new StubHttpClientFactory(_ => throw new InvalidOperationException("HTTP should not be used.")),
            runner,
            NullLogger<SherpaOnnxSenseVoiceTranscriptionEngine>.Instance);

        await engine.TranscribeAsync("/tmp/audio.wav", new ProjectSettings
        {
            Engine = "SherpaOnnxSenseVoice",
            Model = "small",
            LanguageMode = "Fixed",
            LanguageCode = "ja",
        });

        runner.Requests.Should().ContainSingle();
        runner.Requests[0].Backend.Should().Be(ApiSherpaOnnxBackend.SenseVoice);
        runner.Requests[0].ModelPath.Should().Be(Path.Combine(modelDir, "model.int8.onnx"));
        runner.Requests[0].TokensPath.Should().Be(Path.Combine(modelDir, "tokens.txt"));
        runner.Requests[0].LanguageCode.Should().Be("ja");
        runner.Requests[0].UseInverseTextNormalization.Should().BeTrue();
    }

    [Fact]
    public async Task SherpaOnnxEngine_DownloadsMissingModel_OnFirstUse()
    {
        var modelsRoot = CreateTempDirectory();
        var runner = new RecordingSherpaOnnxWorkerRunner();
        var archiveBytes = CreateTarBz2Archive(new Dictionary<string, string>
        {
            ["medium-encoder.onnx"] = "encoder",
            ["medium-decoder.onnx"] = "decoder",
            ["medium-tokens.txt"] = "tokens",
        });

        var engine = new SherpaOnnxTranscriptionEngine(
            Options.Create(new SherpaOnnxOptions
            {
                ModelsPath = modelsRoot,
                AutoDownloadModels = true,
                ModelDownloadBaseUrl = "https://models.example.test",
                WorkerPath = CreateTempWorkerFile("sherpa-worker.exe"),
            }),
            new StubHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(archiveBytes),
            }),
            runner,
            NullLogger<SherpaOnnxTranscriptionEngine>.Instance);

        await engine.TranscribeAsync("/tmp/audio.wav", new ProjectSettings
        {
            Engine = "SherpaOnnx",
            Model = "medium",
            LanguageMode = "Auto",
        });

        var modelDir = Path.Combine(modelsRoot, "medium");
        File.Exists(Path.Combine(modelDir, "medium-encoder.onnx")).Should().BeTrue();
        File.Exists(Path.Combine(modelDir, "medium-decoder.onnx")).Should().BeTrue();
        File.Exists(Path.Combine(modelDir, "medium-tokens.txt")).Should().BeTrue();
        File.Exists(Path.Combine(modelDir, "config.json")).Should().BeTrue();
        runner.Requests.Should().ContainSingle();
        runner.Requests[0].EncoderPath.Should().Be(Path.Combine(modelDir, "medium-encoder.onnx"));
        runner.Requests[0].DecoderPath.Should().Be(Path.Combine(modelDir, "medium-decoder.onnx"));
        runner.Requests[0].TokensPath.Should().Be(Path.Combine(modelDir, "medium-tokens.txt"));
    }

    [Fact]
    public async Task SherpaOnnxEngine_ReplacesLegacyEnglishOnlySmallModel_OnFirstUse()
    {
        var modelsRoot = CreateTempDirectory();
        var modelDir = Path.Combine(modelsRoot, "small");
        Directory.CreateDirectory(modelDir);
        File.WriteAllText(Path.Combine(modelDir, "config.json"), """
        {
          "backend": "whisper",
          "encoder": "small.en-encoder.onnx",
          "decoder": "small.en-decoder.onnx",
          "tokens": "small.en-tokens.txt"
        }
        """);
        File.WriteAllText(Path.Combine(modelDir, "small.en-encoder.onnx"), "legacy-encoder");
        File.WriteAllText(Path.Combine(modelDir, "small.en-decoder.onnx"), "legacy-decoder");
        File.WriteAllText(Path.Combine(modelDir, "small.en-tokens.txt"), "legacy-tokens");

        var runner = new RecordingSherpaOnnxWorkerRunner();
        var archiveBytes = CreateTarBz2Archive(new Dictionary<string, string>
        {
            ["small-encoder.onnx"] = "encoder",
            ["small-decoder.onnx"] = "decoder",
            ["small-tokens.txt"] = "tokens",
        });

        var engine = new SherpaOnnxTranscriptionEngine(
            Options.Create(new SherpaOnnxOptions
            {
                ModelsPath = modelsRoot,
                AutoDownloadModels = true,
                ModelDownloadBaseUrl = "https://models.example.test",
                WorkerPath = CreateTempWorkerFile("sherpa-worker.exe"),
            }),
            new StubHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(archiveBytes),
            }),
            runner,
            NullLogger<SherpaOnnxTranscriptionEngine>.Instance);

        await engine.TranscribeAsync("/tmp/audio.wav", new ProjectSettings
        {
            Engine = "SherpaOnnx",
            Model = "small",
            LanguageMode = "Auto",
        });

        runner.Requests.Should().ContainSingle();
        runner.Requests[0].EncoderPath.Should().Be(Path.Combine(modelDir, "small-encoder.onnx"));
        runner.Requests[0].DecoderPath.Should().Be(Path.Combine(modelDir, "small-decoder.onnx"));
        runner.Requests[0].TokensPath.Should().Be(Path.Combine(modelDir, "small-tokens.txt"));
        File.ReadAllText(Path.Combine(modelDir, "config.json")).Should().Contain("small-encoder.onnx");
    }

    [Fact]
    public async Task SherpaOnnxSenseVoiceEngine_DownloadsMissingModel_OnFirstUse()
    {
        var modelsRoot = CreateTempDirectory();
        var runner = new RecordingSherpaOnnxWorkerRunner();
        var archiveBytes = CreateTarBz2Archive(new Dictionary<string, string>
        {
            ["model.int8.onnx"] = "model",
            ["tokens.txt"] = "tokens",
        });

        var engine = new SherpaOnnxSenseVoiceTranscriptionEngine(
            Options.Create(new SherpaOnnxSenseVoiceOptions
            {
                ModelsPath = modelsRoot,
                AutoDownloadModels = true,
                ModelDownloadBaseUrl = "https://models.example.test",
                WorkerPath = CreateTempWorkerFile("sherpa-worker.exe"),
            }),
            new StubHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(archiveBytes),
            }),
            runner,
            NullLogger<SherpaOnnxSenseVoiceTranscriptionEngine>.Instance);

        await engine.TranscribeAsync("/tmp/audio.wav", new ProjectSettings
        {
            Engine = "SherpaOnnxSenseVoice",
            Model = "small",
            LanguageMode = "Auto",
        });

        var modelDir = Path.Combine(modelsRoot, "small");
        File.Exists(Path.Combine(modelDir, "model.int8.onnx")).Should().BeTrue();
        File.Exists(Path.Combine(modelDir, "tokens.txt")).Should().BeTrue();
        File.Exists(Path.Combine(modelDir, "config.json")).Should().BeTrue();
        runner.Requests.Should().ContainSingle();
        runner.Requests[0].Backend.Should().Be(ApiSherpaOnnxBackend.SenseVoice);
        runner.Requests[0].ModelPath.Should().Be(Path.Combine(modelDir, "model.int8.onnx"));
        runner.Requests[0].TokensPath.Should().Be(Path.Combine(modelDir, "tokens.txt"));
    }

    [Fact]
    public void SherpaOnnxEngine_DoesNotAdvertiseLegacyEnglishOnlySmallModel_WhenAutoDownloadDisabled()
    {
        var modelsRoot = CreateTempDirectory();
        var modelDir = Path.Combine(modelsRoot, "small");
        Directory.CreateDirectory(modelDir);
        File.WriteAllText(Path.Combine(modelDir, "config.json"), """
        {
          "backend": "whisper",
          "encoder": "small.en-encoder.onnx",
          "decoder": "small.en-decoder.onnx",
          "tokens": "small.en-tokens.txt"
        }
        """);
        File.WriteAllText(Path.Combine(modelDir, "small.en-encoder.onnx"), "legacy-encoder");
        File.WriteAllText(Path.Combine(modelDir, "small.en-decoder.onnx"), "legacy-decoder");
        File.WriteAllText(Path.Combine(modelDir, "small.en-tokens.txt"), "legacy-tokens");

        var engine = new SherpaOnnxTranscriptionEngine(
            Options.Create(new SherpaOnnxOptions
            {
                ModelsPath = modelsRoot,
                AutoDownloadModels = false,
                WorkerPath = CreateTempWorkerFile("sherpa-worker.exe"),
            }),
            new StubHttpClientFactory(_ => throw new InvalidOperationException("HTTP should not be used.")),
            new RecordingSherpaOnnxWorkerRunner(),
            NullLogger<SherpaOnnxTranscriptionEngine>.Instance);

        engine.SupportedModels.Should().BeEmpty();
        engine.GetAvailabilityError().Should().Contain("no valid model assets");
    }

    [Fact]
    public void SherpaOnnxEngine_ReportsMissingModelAssets()
    {
        var engine = new SherpaOnnxTranscriptionEngine(
            Options.Create(new SherpaOnnxOptions
            {
                ModelsPath = CreateTempDirectory(),
                AutoDownloadModels = false,
                WorkerPath = CreateTempWorkerFile("sherpa-worker.exe"),
            }),
            new StubHttpClientFactory(_ => throw new InvalidOperationException("HTTP should not be used.")),
            new RecordingSherpaOnnxWorkerRunner(),
            NullLogger<SherpaOnnxTranscriptionEngine>.Instance);

        engine.GetAvailabilityError().Should().Contain("no valid model assets");
    }

    [Fact]
    public void SherpaOnnxEngine_AdvertisesDownloadableModels_WhenAutoDownloadEnabled()
    {
        var engine = new SherpaOnnxTranscriptionEngine(
            Options.Create(new SherpaOnnxOptions
            {
                ModelsPath = CreateTempDirectory(),
                AutoDownloadModels = true,
                WorkerPath = CreateTempWorkerFile("sherpa-worker.exe"),
            }),
            new StubHttpClientFactory(_ => throw new InvalidOperationException("HTTP should not be used.")),
            new RecordingSherpaOnnxWorkerRunner(),
            NullLogger<SherpaOnnxTranscriptionEngine>.Instance);

        engine.GetAvailabilityError().Should().BeNull();
        engine.SupportedModels.Should().Contain(["small", "medium"]);
    }

    private static string CreateTempWorkerFile(string fileName)
    {
        var path = Path.Combine(CreateTempDirectory(), fileName);
        File.WriteAllText(path, "worker");
        return path;
    }

    private static string CreateTempShellWorker(string content)
    {
        var path = Path.Combine(CreateTempDirectory(), "worker.sh");
        File.WriteAllText(path, content.ReplaceLineEndings("\n"));
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return path;
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"transcriptlab-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static byte[] CreateTarBz2Archive(Dictionary<string, string> files)
    {
        var root = CreateTempDirectory();
        var contentDir = Path.Combine(root, "content");
        Directory.CreateDirectory(contentDir);

        foreach (var (fileName, fileContent) in files)
            File.WriteAllText(Path.Combine(contentDir, fileName), fileContent);

        var archivePath = Path.Combine(root, "archive.tar.bz2");
        var arguments = $"-cjf \"{archivePath}\" -C \"{contentDir}\" {string.Join(" ", files.Keys.Select(key => $"\"{key}\""))}";
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "tar",
            Arguments = arguments,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        }) ?? throw new InvalidOperationException("Failed to start tar.");

        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            var stderr = process.StandardError.ReadToEnd();
            throw new InvalidOperationException($"tar failed with exit code {process.ExitCode}: {stderr}");
        }

        var bytes = File.ReadAllBytes(archivePath);
        Directory.Delete(root, recursive: true);
        return bytes;
    }

    private sealed class RecordingWhisperNetWorkerRunner : IWhisperNetWorkerRunner
    {
        public List<WhisperNetWorkerRequest> Requests { get; } = [];

        public Task<WhisperNetWorkerResponse> RunAsync(WhisperNetWorkerRequest request, string workerPath, string? dotNetHostPath, string? extraLibraryDirectory = null, CancellationToken ct = default)
        {
            Requests.Add(request);
            return Task.FromResult(new WhisperNetWorkerResponse
            {
                PlainText = "ok",
                Segments = [new ApiTranscriptSegmentDto { StartMs = 0, EndMs = 10, Text = "ok", Speaker = null }],
                DetectedLanguage = "en",
                DurationMs = 10,
            });
        }
    }

    private sealed class RecordingSherpaOnnxWorkerRunner : ISherpaOnnxWorkerRunner
    {
        public List<ApiSherpaOnnxWorkerRequest> Requests { get; } = [];

        public Task<ApiSherpaOnnxWorkerResponse> RunAsync(ApiSherpaOnnxWorkerRequest request, string workerPath, string? dotNetHostPath, CancellationToken ct = default)
        {
            Requests.Add(request);
            return Task.FromResult(new ApiSherpaOnnxWorkerResponse
            {
                PlainText = "hola",
                Segments = [new ApiTranscriptSegmentDto { StartMs = 0, EndMs = 10, Text = "hola", Speaker = null }],
                DetectedLanguage = "es",
                DurationMs = 10,
            });
        }
    }

    private sealed class RecordingOpenVinoGenAiWorkerRunner : IOpenVinoGenAiWorkerRunner
    {
        public List<OpenVinoGenAiWorkerRequest> Requests { get; } = [];

        public Task<OpenVinoGenAiWorkerResponse> RunAsync(
            OpenVinoGenAiWorkerRequest request,
            string pythonPath,
            string workerScriptPath,
            CancellationToken ct = default)
        {
            Requests.Add(request);
            return Task.FromResult(new OpenVinoGenAiWorkerResponse
            {
                PlainText = "ok",
                Segments = [new ApiTranscriptSegmentDto { StartMs = 0, EndMs = 10, Text = "ok", Speaker = null }],
                DetectedLanguage = "en",
                DurationMs = 10,
            });
        }
    }

    private sealed class StubHttpClientFactory(Func<string, HttpResponseMessage> createResponse) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => new(new StubHttpMessageHandler(() => createResponse(name)))
            {
                BaseAddress = new Uri("https://example.test"),
            };
    }

    private sealed class StubHttpMessageHandler(Func<HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responseFactory());
    }

    private sealed class AvailableCudaEnvironmentProbe : ICudaEnvironmentProbe
    {
        public string? GetAvailabilityError() => null;
    }

    private sealed class AvailableOpenVinoGenAiEnvironmentProbe : IOpenVinoGenAiEnvironmentProbe
    {
        public string? GetAvailabilityError() => null;
    }

    private sealed class FailingCudaEnvironmentProbe(string message) : ICudaEnvironmentProbe
    {
        public string? GetAvailabilityError() => message;
    }
}
