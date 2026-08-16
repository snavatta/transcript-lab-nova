using System.Reflection;
using ClassTranscriber.Api.Contracts;
using ClassTranscriber.Api.Domain;
using ClassTranscriber.Api.Jobs;
using ClassTranscriber.Api.Media;
using ClassTranscriber.Api.Persistence;
using ClassTranscriber.Api.Storage;
using ClassTranscriber.Api.Transcription;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace ClassTranscriber.Api.Tests;

public sealed class HostedProcessingPersistenceTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"transcriptlab-checkpoints-{Guid.NewGuid():N}.db");
    private readonly string _storageRoot = Path.Combine(Path.GetTempPath(), $"transcriptlab-checkpoints-storage-{Guid.NewGuid():N}");
    private ServiceProvider _services = null!;
    private readonly ITestOutputHelper _output;

    public HostedProcessingPersistenceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_storageRoot);
        await Task.CompletedTask;
    }

    private async Task ConfigureServicesAsync(
        CheckpointEngine engine,
        FakeXaiDiarizationService xaiDiarization,
        RecordingSpeakerDiarizer speakerDiarizer,
        RecordingRoleAttributionService roleAttribution)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(options => options.UseSqlite($"Data Source={_databasePath}"));
        services.Configure<StorageOptions>(options => options.BasePath = _storageRoot);
        services.AddSingleton<IFileStorage, LocalFileStorage>();
        services.AddSingleton<IMediaInspector, NoOpMediaInspector>();
        services.AddSingleton<IAudioExtractor, NoOpAudioExtractor>();
        services.AddSingleton<IAudioNormalizer, NoOpAudioNormalizer>();
        services.AddSingleton<ISpeakerDiarizer>(speakerDiarizer);
        services.AddSingleton<IXaiDiarizationService>(xaiDiarization);
        services.AddSingleton<ISpeakerRoleAttributionService>(roleAttribution);
        services.AddSingleton<IRegisteredTranscriptionEngine>(engine);
        services.AddSingleton<ITranscriptionEngineRegistry, TranscriptionEngineRegistry>();
        _services = services.BuildServiceProvider();

        await using var scope = _services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        if (_services is not null)
            await _services.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
        if (Directory.Exists(_storageRoot))
            Directory.Delete(_storageRoot, recursive: true);
    }

    [Fact]
    public async Task ExplicitXaiFailure_IsFatalWithoutFallback()
    {
        var events = new List<string>();
        var engine = new CheckpointEngine(events);
        var xaiDiarization = new FakeXaiDiarizationService(events)
        {
            Exception = new InvalidOperationException(HostedLongFormTestFixtures.FatalProviderDetail),
        };
        var speakerDiarizer = new RecordingSpeakerDiarizer();
        await ConfigureServicesAsync(
            engine,
            xaiDiarization,
            speakerDiarizer,
            new RecordingRoleAttributionService(events));
        var projectId = await SeedProjectAsync();
        var committedCounts = new List<int?>();
        var committedIds = new List<Guid>();
        engine.AfterCheckpointAsync = async () =>
        {
            await using var scope = _services.CreateAsyncScope();
            var checkpoint = await scope.ServiceProvider.GetRequiredService<AppDbContext>()
                .Transcripts.AsNoTracking().SingleAsync(transcript => transcript.ProjectId == projectId);
            committedCounts.Add(checkpoint.HostedRequestCount);
            committedIds.Add(checkpoint.Id);
        };

        await RunWorkerAsync(projectId);

        await using var verificationScope = _services.CreateAsyncScope();
        var project = await verificationScope.ServiceProvider.GetRequiredService<AppDbContext>()
            .Projects.Include(candidate => candidate.Transcript).AsNoTracking()
            .SingleAsync(candidate => candidate.Id == projectId);
        project.Status.Should().Be(ProjectStatus.Failed);
        project.Transcript.Should().NotBeNull();
        project.Transcript!.PlainText.Should().Be("first second");
        project.Transcript.HostedRequestCount.Should().Be(2);
        project.Transcript.HostedSttCostMicroUsd.Should().Be(300_000);
        project.Transcript.HostedDiarizationCostMicroUsd.Should().BeNull();
        project.ErrorMessage.Should().Be(HostedLongFormTestFixtures.SanitizedFatalMessage);
        project.ErrorMessage.Should().NotContain("do-not-persist");
        speakerDiarizer.CallCount.Should().Be(0);
        xaiDiarization.CallCount.Should().Be(1);
        events.Should().Equal("checkpoint-1", "checkpoint-2", "xai");
        committedCounts.Should().Equal(1, 2);
        committedIds.Should().HaveCount(2);
        committedIds.Distinct().Should().ContainSingle();
        _output.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
        {
            scenario = "fatal-xai-checkpoint-preservation",
            projectStatus = project.Status.ToString(),
            checkpointText = project.Transcript.PlainText,
            hostedRequestCount = project.Transcript.HostedRequestCount,
            hostedSttCostMicroUsd = project.Transcript.HostedSttCostMicroUsd,
            hostedDiarizationCostMicroUsd = project.Transcript.HostedDiarizationCostMicroUsd,
            sanitizedError = project.ErrorMessage,
            providerFallbackUsed = speakerDiarizer.CallCount > 0,
        }));
    }

    [Fact]
    public async Task HybridSuccess_PersistsIndependentCosts()
    {
        var events = new List<string>();
        var engine = new CheckpointEngine(events);
        var xaiDiarization = new FakeXaiDiarizationService(events);
        var roleAttribution = new RecordingRoleAttributionService(events);
        await ConfigureServicesAsync(
            engine,
            xaiDiarization,
            new RecordingSpeakerDiarizer(),
            roleAttribution);
        var projectId = await SeedProjectAsync(speakerRoleAttributionEnabled: true);

        await RunWorkerAsync(projectId);

        await using var verificationScope = _services.CreateAsyncScope();
        var project = await verificationScope.ServiceProvider.GetRequiredService<AppDbContext>()
            .Projects.Include(candidate => candidate.Transcript).AsNoTracking()
            .SingleAsync(candidate => candidate.Id == projectId);
        project.Status.Should().Be(ProjectStatus.Completed);
        project.Transcript.Should().NotBeNull();
        project.Transcript!.HostedSttCostMicroUsd.Should().Be(300_000);
        project.Transcript.HostedSttCostClassification.Should().Be("Actual");
        project.Transcript.HostedDiarizationCostMicroUsd.Should().Be(700_000);
        project.Transcript.HostedDiarizationCostClassification.Should().Be("Estimated");
        project.Transcript.HostedDiarizationRequestCount.Should().Be(1);
        project.Transcript.NativeDiarizationUsed.Should().BeFalse();
        project.Transcript.StructuredSegmentsJson.Should().Contain("Speaker 1");
        project.Transcript.PlainText.Should().Be("first second");
        var persistedSegments = System.Text.Json.JsonSerializer.Deserialize<ClassTranscriber.Api.Contracts.TranscriptSegmentDto[]>(
            project.Transcript.StructuredSegmentsJson);
        string.Concat(persistedSegments!.Select(segment => segment.Text)).Should().Be("first second");
        xaiDiarization.CallCount.Should().Be(1);
        roleAttribution.CallCount.Should().Be(1);
        events.Should().Equal("checkpoint-1", "checkpoint-2", "xai", "role");
    }

    private async Task<Guid> SeedProjectAsync(bool speakerRoleAttributionEnabled = false)
    {
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        var folder = new Folder
        {
            Id = Guid.NewGuid(),
            Name = "Hosted processing",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };
        var projectId = Guid.NewGuid();
        var mediaPath = Path.Combine(storage.GetUploadsPath(), $"{projectId:N}.wav");
        await File.WriteAllBytesAsync(storage.GetFullPath(mediaPath), TranscriptionPipelineTests.CreateMinimalWavBytesForTests());
        db.Folders.Add(folder);
        db.Projects.Add(new Project
        {
            Id = projectId,
            FolderId = folder.Id,
            Name = "Hybrid",
            OriginalFileName = "hybrid.wav",
            StoredFileName = $"{projectId:N}.wav",
            FileExtension = ".wav",
            MediaPath = mediaPath,
            MediaType = MediaType.Audio,
            Status = ProjectStatus.PreparingMedia,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            Settings = new ProjectSettings
            {
                Engine = "OpenRouter",
                Model = "openai/whisper-large-v3",
                LanguageMode = "Auto",
                AudioNormalizationEnabled = false,
                DiarizationEnabled = true,
                DiarizationSource = "Xai",
                SpeakerRoleAttributionEnabled = speakerRoleAttributionEnabled,
            },
        });
        await db.SaveChangesAsync();
        return projectId;
    }

    private async Task RunWorkerAsync(Guid projectId)
    {
        var worker = new TranscriptionWorkerService(
            _services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new TranscriptionWorkerOptions()),
            _services.GetRequiredService<ISpeakerDiarizer>(),
            NullLogger<TranscriptionWorkerService>.Instance);
        var method = typeof(TranscriptionWorkerService)
            .GetMethod("ProcessClaimedProjectAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        await ((Task)method!.Invoke(worker, [projectId, CancellationToken.None])!);
    }

    private sealed class CheckpointEngine(ICollection<string> events) : IRegisteredTranscriptionEngine, IOpenRouterChunkProgressTranscriptionEngine
    {
        public string EngineId => "OpenRouter";
        public IReadOnlyCollection<string> SupportedModels { get; } = ["openai/whisper-large-v3"];
        public IReadOnlyCollection<string> WordTimestampModels => SupportedModels;
        public Func<Task>? AfterCheckpointAsync { get; set; }
        public string? GetAvailabilityError() => null;
        public string? GetProbeError() => null;

        public Task<TranscriptionResult> TranscribeAsync(string audioPath, ProjectSettings settings, CancellationToken ct = default)
            => TranscribeAsync(audioPath, settings, onChunkSucceeded: null, ct);

        public async Task<TranscriptionResult> TranscribeAsync(
            string audioPath,
            ProjectSettings settings,
            Func<OpenRouterChunkProgress, CancellationToken, ValueTask>? onChunkSucceeded,
            CancellationToken ct = default)
        {
            var first = Result(HostedLongFormTestFixtures.HybridCheckpoints[0]);
            var second = Result(HostedLongFormTestFixtures.HybridCheckpoints[1]);
            if (onChunkSucceeded is not null)
            {
                await onChunkSucceeded(new OpenRouterChunkProgress(0, 2, 0, 1_000, first), ct);
                events.Add("checkpoint-1");
                if (AfterCheckpointAsync is not null)
                    await AfterCheckpointAsync();
                await onChunkSucceeded(new OpenRouterChunkProgress(1, 2, 1_000, 2_000, second), ct);
                events.Add("checkpoint-2");
                if (AfterCheckpointAsync is not null)
                    await AfterCheckpointAsync();
            }

            return second;
        }

        private static TranscriptionResult Result(HostedLongFormTestFixtures.Checkpoint checkpoint)
        {
            var words = checkpoint.Words.ToArray();
            return new TranscriptionResult(
                checkpoint.Text,
                words.Select(word => new ClassTranscriber.Api.Contracts.TranscriptSegmentDto
                {
                    StartMs = word.StartMs,
                    EndMs = word.EndMs,
                    Text = word.Text,
                }).ToArray(),
                "en",
                2_000,
                new TranscriptionProcessingMetadata(
                    "OpenRouter",
                    "openai/whisper-large-v3",
                    checkpoint.RequestCount,
                    false,
                    checkpoint.SttCostMicroUsd,
                    SttCostClassification: "Actual"))
            {
                Words = words,
            };
        }
    }

    private sealed class FakeXaiDiarizationService(ICollection<string> events) : IXaiDiarizationService
    {
        public Exception? Exception { get; init; }
        public int CallCount { get; private set; }

        public Task<XaiDiarizationResult> DiarizeAsync(
            string audioPath,
            long? durationMs,
            CancellationToken ct = default)
        {
            CallCount += 1;
            events.Add("xai");
            if (Exception is not null)
                throw Exception;

            return Task.FromResult(new XaiDiarizationResult(
                HostedLongFormTestFixtures.HybridSpeakerIntervals,
                XaiTranscriptionEngine.PreferredModel,
                1,
                HostedLongFormTestFixtures.HybridDiarizationCostMicroUsd,
                HostedLongFormTestFixtures.HybridDiarizationRateMicroUsdPerHour,
                "Estimated"));
        }
    }

    private sealed class RecordingSpeakerDiarizer : ISpeakerDiarizer
    {
        public int CallCount { get; private set; }

        public ClassTranscriber.Api.Contracts.TranscriptSegmentDto[] AssignSpeakers(
            string audioPath,
            IReadOnlyList<ClassTranscriber.Api.Contracts.TranscriptSegmentDto> segments,
            string mode = "Basic",
            CancellationToken ct = default)
        {
            CallCount += 1;
            return [.. segments];
        }
    }

    private sealed class RecordingRoleAttributionService(ICollection<string> events) : ISpeakerRoleAttributionService
    {
        public bool IsAvailable => true;
        public string Model => "fake-role-model";
        public int CallCount { get; private set; }

        public Task<SpeakerRoleAttributionResult> AttributeAsync(
            ClassTranscriber.Api.Contracts.TranscriptSegmentDto[] segments,
            CancellationToken ct)
        {
            CallCount += 1;
            events.Add("role");
            return Task.FromResult(new SpeakerRoleAttributionResult(segments, "Completed"));
        }
    }
}
