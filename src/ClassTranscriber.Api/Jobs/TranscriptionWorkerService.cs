using System.Text.Json;
using ClassTranscriber.Api.Contracts;
using ClassTranscriber.Api.Domain;
using ClassTranscriber.Api.Media;
using ClassTranscriber.Api.Persistence;
using ClassTranscriber.Api.Storage;
using ClassTranscriber.Api.Transcription;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ClassTranscriber.Api.Jobs;

public class TranscriptionWorkerOptions
{
    public int PollingIntervalSeconds { get; set; } = 5;
    public int WorkerConcurrency { get; set; } = 1;
}

public class TranscriptionWorkerService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TranscriptionWorkerOptions _options;
    private readonly ILogger<TranscriptionWorkerService> _logger;

    public TranscriptionWorkerService(
        IServiceScopeFactory scopeFactory,
        IOptions<TranscriptionWorkerOptions> options,
        ILogger<TranscriptionWorkerService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Transcription worker started. Polling every {Interval}s, concurrency={Concurrency}",
            _options.PollingIntervalSeconds, _options.WorkerConcurrency);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessNextJobAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in transcription worker loop");
            }

            await Task.Delay(TimeSpan.FromSeconds(_options.PollingIntervalSeconds), stoppingToken);
        }

        _logger.LogInformation("Transcription worker stopped");
    }

    private async Task ProcessNextJobAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await db.Projects
            .Where(p => p.Status == ProjectStatus.Queued)
            .OrderBy(p => p.QueuedAtUtc ?? p.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

        if (project is null)
            return;

        _logger.LogInformation("Processing project {ProjectId} ({ProjectName})", project.Id, project.Name);

        try
        {
            // Mark as PreparingMedia
            project.Status = ProjectStatus.PreparingMedia;
            project.StartedAtUtc = DateTime.UtcNow;
            project.UpdatedAtUtc = DateTime.UtcNow;
            project.Progress = 10;
            await db.SaveChangesAsync(ct);

            var fileStorage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
            var mediaInspector = scope.ServiceProvider.GetRequiredService<IMediaInspector>();
            var audioExtractor = scope.ServiceProvider.GetRequiredService<IAudioExtractor>();
            var audioNormalizer = scope.ServiceProvider.GetRequiredService<IAudioNormalizer>();
            var transcriptionEngineRegistry = scope.ServiceProvider.GetRequiredService<ITranscriptionEngineRegistry>();
            var transcriptionEngine = transcriptionEngineRegistry.Resolve(project.Settings.Engine);

            var mediaFullPath = fileStorage.GetFullPath(project.MediaPath);

            // Inspect media
            var mediaInfo = await mediaInspector.InspectAsync(mediaFullPath, ct);
            if (mediaInfo is not null)
            {
                project.DurationMs = mediaInfo.DurationMs;
                project.MediaType = mediaInfo.MediaType;
            }

            // Prepare audio
            var audioDir = fileStorage.GetFullPath(fileStorage.GetAudioPath());
            Directory.CreateDirectory(audioDir);
            var audioOutputPath = Path.Combine(audioDir, Path.ChangeExtension(project.StoredFileName, ".wav"));

            string preparedAudioPath;

            if (project.MediaType == Contracts.MediaType.Video)
            {
                // Extract audio from video
                preparedAudioPath = await audioExtractor.ExtractAudioAsync(mediaFullPath, audioOutputPath, ct);
            }
            else
            {
                // Convert audio to WAV 16kHz mono for Whisper
                preparedAudioPath = await audioExtractor.ExtractAudioAsync(mediaFullPath, audioOutputPath, ct);
            }

            // Normalize if enabled
            if (project.Settings.AudioNormalizationEnabled)
            {
                var normalizedPath = Path.Combine(audioDir, "norm_" + Path.GetFileName(preparedAudioPath));
                preparedAudioPath = await audioNormalizer.NormalizeAsync(preparedAudioPath, normalizedPath, ct);
            }

            project.Progress = 30;
            await db.SaveChangesAsync(ct);

            // Mark as Transcribing
            project.Status = ProjectStatus.Transcribing;
            project.Progress = 40;
            project.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            // Transcribe
            var result = await transcriptionEngine.TranscribeAsync(preparedAudioPath, project.Settings, ct);

            project.Progress = 90;
            await db.SaveChangesAsync(ct);

            // Store transcript
            var transcript = new Transcript
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                PlainText = result.PlainText,
                StructuredSegmentsJson = JsonSerializer.Serialize(result.Segments),
                DetectedLanguage = result.DetectedLanguage,
                DurationMs = result.DurationMs,
                SegmentCount = result.Segments.Length,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
            };

            db.Transcripts.Add(transcript);

            // Update project
            project.Status = ProjectStatus.Completed;
            project.Progress = 100;
            project.CompletedAtUtc = DateTime.UtcNow;
            project.UpdatedAtUtc = DateTime.UtcNow;
            project.DurationMs = result.DurationMs ?? project.DurationMs;

            // Update workspace size
            var workspaceSize = 0L;
            if (File.Exists(preparedAudioPath))
                workspaceSize += new FileInfo(preparedAudioPath).Length;
            project.WorkspaceSizeBytes = workspaceSize;
            project.TotalSizeBytes = (project.OriginalFileSizeBytes ?? 0) + workspaceSize;

            await db.SaveChangesAsync(ct);

            _logger.LogInformation("Project {ProjectId} completed successfully with {SegmentCount} segments",
                project.Id, result.Segments.Length);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Project {ProjectId} failed", project.Id);

            project.Status = ProjectStatus.Failed;
            project.ErrorMessage = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
            project.FailedAtUtc = DateTime.UtcNow;
            project.UpdatedAtUtc = DateTime.UtcNow;

            await db.SaveChangesAsync(CancellationToken.None);
        }
    }
}
