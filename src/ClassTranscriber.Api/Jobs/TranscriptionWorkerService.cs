using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using ClassTranscriber.Api.Contracts;
using ClassTranscriber.Api.Domain;
using ClassTranscriber.Api.Media;
using ClassTranscriber.Api.Persistence;
using ClassTranscriber.Api.Services;
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

public class TranscriptionWorkerService : BackgroundService, IActiveJobCancellation
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TranscriptionWorkerOptions _options;
    private readonly ISpeakerDiarizer _speakerDiarizer;
    private readonly ILogger<TranscriptionWorkerService> _logger;
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _activeJobs = new();

    public TranscriptionWorkerService(
        IServiceScopeFactory scopeFactory,
        IOptions<TranscriptionWorkerOptions> options,
        ISpeakerDiarizer speakerDiarizer,
        ILogger<TranscriptionWorkerService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _speakerDiarizer = speakerDiarizer;
        _options.PollingIntervalSeconds = Math.Max(1, _options.PollingIntervalSeconds);
        _options.WorkerConcurrency = Math.Max(1, _options.WorkerConcurrency);
        _logger = logger;
    }

    public bool TryCancel(Guid projectId)
    {
        if (_activeJobs.TryGetValue(projectId, out var cts))
        {
            cts.Cancel();
            return true;
        }

        return false;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Transcription worker started. Polling every {Interval}s, concurrency={Concurrency}",
            _options.PollingIntervalSeconds, _options.WorkerConcurrency);

        var runningTasks = new List<Task>();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DrainCompletedTasksAsync(runningTasks);

                while (runningTasks.Count < _options.WorkerConcurrency && !stoppingToken.IsCancellationRequested)
                {
                    var projectId = await TryClaimNextProjectAsync(stoppingToken);
                    if (projectId is null)
                        break;

                    runningTasks.Add(ProcessClaimedProjectAsync(projectId.Value, stoppingToken));
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in transcription worker loop");
            }

            if (runningTasks.Count == 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.PollingIntervalSeconds), stoppingToken);
                continue;
            }

            try
            {
                await Task.WhenAny(runningTasks.Append(Task.Delay(TimeSpan.FromSeconds(_options.PollingIntervalSeconds), stoppingToken)));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        await DrainCompletedTasksAsync(runningTasks, waitForAll: true);
        _logger.LogInformation("Transcription worker stopped");
    }

    private async Task<Guid?> TryClaimNextProjectAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var project = await db.Projects
            .Where(p => p.Status == ProjectStatus.Queued)
            .OrderBy(p => p.QueuedAtUtc ?? p.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

        if (project is null)
            return null;

        project.Status = ProjectStatus.PreparingMedia;
        project.StartedAtUtc = DateTime.UtcNow;
        project.UpdatedAtUtc = DateTime.UtcNow;
        project.Progress = 0;
        project.TranscriptionElapsedMs = null;
        project.TotalProcessingElapsedMs = null;
        project.MediaInspectionElapsedMs = null;
        project.AudioExtractionElapsedMs = null;
        project.AudioNormalizationElapsedMs = null;
        project.ResultPersistenceElapsedMs = null;
        await db.SaveChangesAsync(CancellationToken.None);

        _logger.LogInformation("Claimed project {ProjectId} ({ProjectName}) for processing", project.Id, project.Name);
        return project.Id;
    }

    private async Task ProcessClaimedProjectAsync(Guid projectId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project is null)
            return;

        if (project.Status == ProjectStatus.Cancelled)
        {
            _logger.LogInformation("Skipping project {ProjectId} because it was cancelled before processing began", projectId);
            return;
        }

        using var jobCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _activeJobs[project.Id] = jobCts;

        try
        {
            var jobToken = jobCts.Token;
            var totalStopwatch = Stopwatch.StartNew();
            long inspectElapsedMs = 0;
            long extractElapsedMs = 0;
            long normalizeElapsedMs = 0;
            long transcriptionElapsedMs = 0;
            long persistElapsedMs = 0;

            var fileStorage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
            var mediaInspector = scope.ServiceProvider.GetRequiredService<IMediaInspector>();
            var audioExtractor = scope.ServiceProvider.GetRequiredService<IAudioExtractor>();
            var audioNormalizer = scope.ServiceProvider.GetRequiredService<IAudioNormalizer>();
            var transcriptionEngineRegistry = scope.ServiceProvider.GetRequiredService<ITranscriptionEngineRegistry>();
            var transcriptionEngine = transcriptionEngineRegistry.Resolve(project.Settings.Engine);

            var mediaFullPath = fileStorage.GetFullPath(project.MediaPath);

            // Inspect media
            var inspectStopwatch = Stopwatch.StartNew();
            var mediaInfo = await mediaInspector.InspectAsync(mediaFullPath, jobToken);
            inspectStopwatch.Stop();
            inspectElapsedMs = inspectStopwatch.ElapsedMilliseconds;
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

            var extractStopwatch = Stopwatch.StartNew();
            if (project.MediaType == Contracts.MediaType.Video)
            {
                // Extract audio from video
                preparedAudioPath = await audioExtractor.ExtractAudioAsync(mediaFullPath, audioOutputPath, jobToken);
            }
            else
            {
                // Convert audio to WAV 16kHz mono for the transcription engines
                preparedAudioPath = await audioExtractor.ExtractAudioAsync(mediaFullPath, audioOutputPath, jobToken);
            }
            extractStopwatch.Stop();
            extractElapsedMs = extractStopwatch.ElapsedMilliseconds;

            // Normalize if enabled
            if (project.Settings.AudioNormalizationEnabled)
            {
                var normalizedPath = Path.Combine(audioDir, "norm_" + Path.GetFileName(preparedAudioPath));
                var normalizeStopwatch = Stopwatch.StartNew();
                preparedAudioPath = await audioNormalizer.NormalizeAsync(preparedAudioPath, normalizedPath, jobToken);
                normalizeStopwatch.Stop();
                normalizeElapsedMs = normalizeStopwatch.ElapsedMilliseconds;
            }

            // Mark as Transcribing
            project.Status = ProjectStatus.Transcribing;
            project.Progress = 0;
            project.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);

            // Transcribe
            var transcriptionStopwatch = Stopwatch.StartNew();
            var result = await transcriptionEngine.TranscribeAsync(preparedAudioPath, project.Settings, jobToken);
            transcriptionStopwatch.Stop();
            transcriptionElapsedMs = transcriptionStopwatch.ElapsedMilliseconds;
            jobToken.ThrowIfCancellationRequested();

            if (project.Settings.DiarizationEnabled && result.Segments.Length > 0)
            {
                try
                {
                    result = result with
                    {
                        Segments = _speakerDiarizer.AssignSpeakers(preparedAudioPath, result.Segments, jobToken),
                    };
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Speaker diarization failed for project {ProjectId}; continuing without speaker labels", project.Id);
                }
            }

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
            project.TranscriptionElapsedMs = transcriptionElapsedMs;
            project.MediaInspectionElapsedMs = inspectElapsedMs;
            project.AudioExtractionElapsedMs = extractElapsedMs;
            project.AudioNormalizationElapsedMs = normalizeElapsedMs == 0 ? null : normalizeElapsedMs;
            project.TotalProcessingElapsedMs = null;
            project.ResultPersistenceElapsedMs = null;

            // Update workspace size using all derived audio artifacts that remain on disk.
            var workspaceSize = ProjectAudioFileResolver.GetExistingWorkspaceAudioRelativePaths(fileStorage, project)
                .Select(relativePath => new FileInfo(fileStorage.GetFullPath(relativePath)).Length)
                .Sum();
            project.WorkspaceSizeBytes = workspaceSize;
            project.TotalSizeBytes = (project.OriginalFileSizeBytes ?? 0) + workspaceSize;

            var persistStopwatch = Stopwatch.StartNew();
            await db.SaveChangesAsync(CancellationToken.None);
            persistStopwatch.Stop();
            persistElapsedMs = persistStopwatch.ElapsedMilliseconds;
            totalStopwatch.Stop();

            project.ResultPersistenceElapsedMs = persistElapsedMs;
            project.TotalProcessingElapsedMs = totalStopwatch.ElapsedMilliseconds;

            try
            {
                await db.SaveChangesAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to persist final debug timing metrics for project {ProjectId}", project.Id);
            }

            var preparationElapsedMs = inspectElapsedMs + extractElapsedMs + normalizeElapsedMs;
            var audioDurationMs = result.DurationMs ?? project.DurationMs;
            var transcriptionRealtimeFactor = CalculateRealtimeFactor(transcriptionElapsedMs, audioDurationMs);
            var totalRealtimeFactor = CalculateRealtimeFactor(project.TotalProcessingElapsedMs ?? totalStopwatch.ElapsedMilliseconds, audioDurationMs);

            _logger.LogInformation(
                "Project {ProjectId} completed successfully. segments={SegmentCount}, audioDurationMs={AudioDurationMs}, totalElapsedMs={TotalElapsedMs}, preparationElapsedMs={PreparationElapsedMs}, inspectElapsedMs={InspectElapsedMs}, extractElapsedMs={ExtractElapsedMs}, normalizeElapsedMs={NormalizeElapsedMs}, transcriptionElapsedMs={TranscriptionElapsedMs}, persistElapsedMs={PersistElapsedMs}, transcriptionRealtimeFactor={TranscriptionRealtimeFactor}, totalRealtimeFactor={TotalRealtimeFactor}",
                project.Id,
                result.Segments.Length,
                audioDurationMs,
                totalStopwatch.ElapsedMilliseconds,
                preparationElapsedMs,
                inspectElapsedMs,
                extractElapsedMs,
                normalizeElapsedMs,
                transcriptionElapsedMs,
                persistElapsedMs,
                transcriptionRealtimeFactor,
                totalRealtimeFactor);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Job-level cancellation (user cancelled), not app shutdown
            _logger.LogInformation("Project {ProjectId} was cancelled by user", project.Id);

            project.Status = ProjectStatus.Cancelled;
            project.Progress = 0;
            project.UpdatedAtUtc = DateTime.UtcNow;

            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Project {ProjectId} failed", project.Id);

            project.Status = ProjectStatus.Failed;
            project.Progress = 0;
            project.ErrorMessage = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
            project.FailedAtUtc = DateTime.UtcNow;
            project.UpdatedAtUtc = DateTime.UtcNow;

            await db.SaveChangesAsync(CancellationToken.None);
        }
        finally
        {
            _activeJobs.TryRemove(project.Id, out _);
        }
    }

    private static double? CalculateRealtimeFactor(long elapsedMs, long? audioDurationMs)
    {
        if (audioDurationMs is null || audioDurationMs <= 0)
            return null;

        return Math.Round((double)elapsedMs / audioDurationMs.Value, 2);
    }

    private async Task DrainCompletedTasksAsync(List<Task> runningTasks, bool waitForAll = false)
    {
        if (waitForAll)
        {
            while (runningTasks.Count > 0)
            {
                var completedTask = await Task.WhenAny(runningTasks);
                await ObserveTaskAsync(completedTask);
                runningTasks.Remove(completedTask);
            }

            return;
        }

        for (var index = runningTasks.Count - 1; index >= 0; index -= 1)
        {
            var task = runningTasks[index];
            if (!task.IsCompleted)
                continue;

            await ObserveTaskAsync(task);
            runningTasks.RemoveAt(index);
        }
    }

    private async Task ObserveTaskAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
            // The worker or job was cancelled; state persistence is handled in the processing flow.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception while processing a transcription job");
        }
    }
}
