using System.Diagnostics;
using ClassTranscriber.Api.Contracts;
using ClassTranscriber.Api.Persistence;
using ClassTranscriber.Api.Transcription;
using Microsoft.EntityFrameworkCore;

namespace ClassTranscriber.Api.Services;

public interface IDiagnosticsService
{
    Task<DiagnosticsDto> GetAsync(CancellationToken ct = default);
}

public interface IRuntimeMetricsSampler
{
    RuntimeDiagnosticsDto Capture();
}

public sealed class RuntimeMetricsSampler : IRuntimeMetricsSampler
{
    private readonly object _sync = new();
    private readonly DateTime _startedAtUtc;
    private TimeSpan _lastProcessorTime;
    private DateTime _lastCollectedAtUtc;
    private double _lastCpuUsagePercent;
    private bool _hasSample;

    public RuntimeMetricsSampler()
    {
        var process = Process.GetCurrentProcess();
        _startedAtUtc = process.StartTime.ToUniversalTime();
        _lastProcessorTime = process.TotalProcessorTime;
        _lastCollectedAtUtc = DateTime.UtcNow;
    }

    public RuntimeDiagnosticsDto Capture()
    {
        lock (_sync)
        {
            var process = Process.GetCurrentProcess();
            process.Refresh();

            var collectedAtUtc = DateTime.UtcNow;
            var totalProcessorTime = process.TotalProcessorTime;
            var uptimeMs = Math.Max(0L, (long)(collectedAtUtc - _startedAtUtc).TotalMilliseconds);
            var cpuUsagePercent = CalculateCpuUsagePercent(totalProcessorTime, collectedAtUtc, uptimeMs);

            _lastProcessorTime = totalProcessorTime;
            _lastCollectedAtUtc = collectedAtUtc;
            _lastCpuUsagePercent = cpuUsagePercent;
            _hasSample = true;

            return new RuntimeDiagnosticsDto
            {
                CollectedAtUtc = collectedAtUtc.ToString("O"),
                ProcessId = Environment.ProcessId,
                ProcessorCount = Environment.ProcessorCount,
                UptimeMs = uptimeMs,
                CpuUsagePercent = cpuUsagePercent,
                WorkingSetBytes = process.WorkingSet64,
                PrivateMemoryBytes = process.PrivateMemorySize64,
                ManagedHeapBytes = GC.GetTotalMemory(forceFullCollection: false),
            };
        }
    }

    private double CalculateCpuUsagePercent(TimeSpan totalProcessorTime, DateTime collectedAtUtc, long uptimeMs)
    {
        var processorCount = Math.Max(1, Environment.ProcessorCount);

        if (!_hasSample)
        {
            if (uptimeMs <= 0)
                return 0d;

            var averageSinceStart = totalProcessorTime.TotalMilliseconds / uptimeMs / processorCount * 100d;
            return Math.Clamp(averageSinceStart, 0d, 100d);
        }

        var wallClockMs = (collectedAtUtc - _lastCollectedAtUtc).TotalMilliseconds;
        if (wallClockMs <= 0)
            return _lastCpuUsagePercent;

        var cpuMs = (totalProcessorTime - _lastProcessorTime).TotalMilliseconds;
        var usagePercent = cpuMs / wallClockMs / processorCount * 100d;
        return Math.Clamp(usagePercent, 0d, 100d);
    }
}

public sealed class DiagnosticsService : IDiagnosticsService
{
    private readonly AppDbContext _db;
    private readonly IRuntimeMetricsSampler _runtimeMetricsSampler;
    private readonly IReadOnlyCollection<IRegisteredTranscriptionEngine> _engines;

    public DiagnosticsService(
        AppDbContext db,
        IRuntimeMetricsSampler runtimeMetricsSampler,
        IEnumerable<IRegisteredTranscriptionEngine> engines)
    {
        _db = db;
        _runtimeMetricsSampler = runtimeMetricsSampler;
        _engines = engines.ToArray();
    }

    public async Task<DiagnosticsDto> GetAsync(CancellationToken ct = default)
    {
        var runtime = _runtimeMetricsSampler.Capture();

        var projects = await _db.Projects
            .AsNoTracking()
            .Include(project => project.Folder)
            .OrderByDescending(project => project.TotalSizeBytes ?? project.OriginalFileSizeBytes ?? 0)
            .ThenBy(project => project.Name)
            .Select(project => new ProjectStorageDiagnosticsDto
            {
                ProjectId = project.Id.ToString(),
                FolderId = project.FolderId.ToString(),
                FolderName = project.Folder.Name,
                ProjectName = project.Name,
                Status = project.Status,
                OriginalFileSizeBytes = project.OriginalFileSizeBytes,
                WorkspaceSizeBytes = project.WorkspaceSizeBytes,
                TotalSizeBytes = project.TotalSizeBytes,
                UpdatedAtUtc = project.UpdatedAtUtc.ToString("O"),
            })
            .ToArrayAsync(ct);

        var engines = _engines
            .OrderBy(engine => engine.EngineId, StringComparer.OrdinalIgnoreCase)
            .Select(engine =>
            {
                var availabilityError = engine.GetAvailabilityError();
                return new DiagnosticsEngineDto
                {
                    Engine = engine.EngineId,
                    IsAvailable = availabilityError is null,
                    Models = engine.SupportedModels.OrderBy(model => model, StringComparer.OrdinalIgnoreCase).ToArray(),
                    AvailabilityError = availabilityError,
                };
            })
            .ToArray();

        return new DiagnosticsDto
        {
            Runtime = runtime,
            Engines = engines,
            Projects = projects,
        };
    }
}
