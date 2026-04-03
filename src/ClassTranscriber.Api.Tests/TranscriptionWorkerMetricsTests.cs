using System.Reflection;
using ClassTranscriber.Api.Contracts;
using ClassTranscriber.Api.Domain;
using ClassTranscriber.Api.Jobs;
using ClassTranscriber.Api.Media;
using ClassTranscriber.Api.Persistence;
using ClassTranscriber.Api.Storage;
using ClassTranscriber.Api.Transcription;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ClassTranscriber.Api.Tests;

public sealed class TranscriptionWorkerMetricsTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private ServiceProvider _services = null!;
    private string _storageRoot = null!;

    public async Task InitializeAsync()
    {
        _storageRoot = Path.Combine(Path.GetTempPath(), $"transcriptlab-worker-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_storageRoot);

        _connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(options => options.UseSqlite(_connection));
        services.Configure<StorageOptions>(options => options.BasePath = _storageRoot);
        services.AddSingleton<IFileStorage, LocalFileStorage>();
        services.AddSingleton<IMediaInspector, NoOpMediaInspector>();
        services.AddSingleton<IAudioExtractor, NoOpAudioExtractor>();
        services.AddSingleton<IAudioNormalizer, NoOpAudioNormalizer>();
        services.AddSingleton<IRegisteredTranscriptionEngine>(new DelayedTranscriptionEngine(delayMs: 60));
        services.AddSingleton<ITranscriptionEngineRegistry, TranscriptionEngineRegistry>();
        services.AddSingleton<ISpeakerDiarizer, BasicSpeakerDiarizer>();

        _services = services.BuildServiceProvider();

        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        if (_services is not null)
            await _services.DisposeAsync();

        await _connection.DisposeAsync();

        if (Directory.Exists(_storageRoot))
            Directory.Delete(_storageRoot, recursive: true);
    }

    [Fact]
    public async Task ProcessClaimedProjectAsync_PersistsTranscriptionElapsedMs()
    {
        var projectId = Guid.NewGuid();

        await using (var scope = _services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();

            var folder = new Folder
            {
                Id = Guid.NewGuid(),
                Name = "Metrics",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
            };

            var mediaRelativePath = Path.Combine(storage.GetUploadsPath(), "metrics.wav");
            await File.WriteAllBytesAsync(storage.GetFullPath(mediaRelativePath), CreateMinimalWavBytes());

            db.Folders.Add(folder);
            db.Projects.Add(new Project
            {
                Id = projectId,
                FolderId = folder.Id,
                Name = "Benchmark Project",
                OriginalFileName = "metrics.wav",
                StoredFileName = "metrics.wav",
                FileExtension = ".wav",
                MediaPath = mediaRelativePath,
                MediaType = MediaType.Audio,
                Status = ProjectStatus.PreparingMedia,
                Progress = 30,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
                Settings = new ProjectSettings
                {
                    Engine = "WhisperNet",
                    Model = "small",
                    LanguageMode = "Auto",
                    AudioNormalizationEnabled = false,
                    DiarizationEnabled = false,
                },
            });

            await db.SaveChangesAsync();
        }

        var worker = new TranscriptionWorkerService(
            _services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new TranscriptionWorkerOptions()),
            _services.GetRequiredService<ISpeakerDiarizer>(),
            NullLogger<TranscriptionWorkerService>.Instance);

        var method = typeof(TranscriptionWorkerService)
            .GetMethod("ProcessClaimedProjectAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var task = (Task?)method!.Invoke(worker, new object[] { projectId, CancellationToken.None });
        task.Should().NotBeNull();
        await task!;

        await using var verificationScope = _services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var project = await verificationDb.Projects.Include(p => p.Transcript).SingleAsync(p => p.Id == projectId);

        project.Status.Should().Be(ProjectStatus.Completed);
        project.TranscriptionElapsedMs.Should().NotBeNull();
        project.TranscriptionElapsedMs.Should().BeGreaterThanOrEqualTo(40);
        project.Transcript.Should().NotBeNull();
    }

    private static byte[] CreateMinimalWavBytes()
        => new byte[]
        {
            0x52, 0x49, 0x46, 0x46, 0x24, 0x08, 0x00, 0x00, 0x57, 0x41, 0x56, 0x45,
            0x66, 0x6D, 0x74, 0x20, 0x10, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00,
            0x40, 0x1F, 0x00, 0x00, 0x80, 0x3E, 0x00, 0x00, 0x02, 0x00, 0x10, 0x00,
            0x64, 0x61, 0x74, 0x61, 0x00, 0x08, 0x00, 0x00,
        }
        .Concat(new byte[2048])
        .ToArray();

    private sealed class DelayedTranscriptionEngine(int delayMs) : IRegisteredTranscriptionEngine
    {
        public string EngineId => "WhisperNet";

        public IReadOnlyCollection<string> SupportedModels { get; } = ["small"];

        public string? GetAvailabilityError() => null;

        public string? GetProbeError() => null;

        public async Task<TranscriptionResult> TranscribeAsync(string audioPath, ProjectSettings settings, CancellationToken ct = default)
        {
            await Task.Delay(delayMs, ct);
            return new TranscriptionResult(
                "Benchmark transcript",
                [new ClassTranscriber.Api.Contracts.TranscriptSegmentDto { StartMs = 0, EndMs = 1000, Text = "Benchmark transcript", Speaker = null }],
                "en",
                1000);
        }
    }
}
