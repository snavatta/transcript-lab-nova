using System.Runtime.InteropServices;
using ClassTranscriber.Api.Contracts;

namespace ClassTranscriber.Api.Services;

public interface ISystemCapabilitiesService
{
    Task<SystemCapabilitiesDto> GetAsync(CancellationToken ct = default);
}

public interface IHostedProviderCapabilitiesProbe
{
    bool IsConfigured(string provider);
    Task<bool> IsReachableAsync(string provider, CancellationToken ct);
}

public interface IRuntimeCapabilitiesProbe
{
    RuntimeCapabilitiesSnapshot Capture();
}

public sealed record RuntimeComputeBackend(string Backend, bool Available, string[] Devices);

public sealed record RuntimeCapabilitiesSnapshot(
    string Architecture,
    int LogicalProcessorCount,
    string OsDescription,
    string? HardwareName,
    RuntimeComputeBackend[] ComputeBackends);

public sealed class SystemCapabilitiesService : ISystemCapabilitiesService
{
    private static readonly string[] ProviderNames = ["OpenRouter", "xAI"];
    private readonly IHostedProviderCapabilitiesProbe _hostedProviderProbe;
    private readonly IRuntimeCapabilitiesProbe _runtimeProbe;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _cacheDuration;
    private readonly object _cacheSync = new();
    private SystemCapabilitiesDto? _cachedCapabilities;
    private DateTimeOffset _cacheExpiresAtUtc;
    private Task<SystemCapabilitiesDto>? _inFlightCollection;

    public SystemCapabilitiesService(
        IHostedProviderCapabilitiesProbe hostedProviderProbe,
        IRuntimeCapabilitiesProbe runtimeProbe,
        TimeProvider? timeProvider = null,
        TimeSpan? cacheDuration = null)
    {
        _hostedProviderProbe = hostedProviderProbe;
        _runtimeProbe = runtimeProbe;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _cacheDuration = cacheDuration is { } configuredDuration && configuredDuration > TimeSpan.Zero
            ? configuredDuration
            : TimeSpan.FromSeconds(5);
    }

    public async Task<SystemCapabilitiesDto> GetAsync(CancellationToken ct = default)
    {
        Task<SystemCapabilitiesDto> sharedCollection;
        TaskCompletionSource<SystemCapabilitiesDto>? producer = null;
        lock (_cacheSync)
        {
            if (_cachedCapabilities is not null && _timeProvider.GetUtcNow() < _cacheExpiresAtUtc)
                return _cachedCapabilities;

            if (_inFlightCollection is null)
            {
                producer = new TaskCompletionSource<SystemCapabilitiesDto>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _inFlightCollection = producer.Task;
            }

            sharedCollection = _inFlightCollection;
        }

        if (producer is not null)
            _ = CollectAndPublishAsync(producer);

        return await sharedCollection.WaitAsync(ct);
    }

    private async Task CollectAndPublishAsync(TaskCompletionSource<SystemCapabilitiesDto> producer)
    {
        try
        {
            var capabilities = await CollectAsync();
            lock (_cacheSync)
            {
                _cachedCapabilities = capabilities;
                _cacheExpiresAtUtc = _timeProvider.GetUtcNow() + _cacheDuration;
                _inFlightCollection = null;
            }
            producer.TrySetResult(capabilities);
        }
        catch (Exception exception)
        {
            lock (_cacheSync)
                _inFlightCollection = null;
            producer.TrySetException(exception);
        }
    }

    private async Task<SystemCapabilitiesDto> CollectAsync()
    {
        var providerResults = new HostedProviderCapabilityDto[ProviderNames.Length];
        for (var index = 0; index < ProviderNames.Length; index++)
        {
            var provider = ProviderNames[index];
            var configured = false;
            bool? reachable = null;
            try
            {
                configured = _hostedProviderProbe.IsConfigured(provider);
                if (configured)
                    reachable = await _hostedProviderProbe.IsReachableAsync(provider, CancellationToken.None);
            }
            catch
            {
                reachable = configured ? false : null;
            }

            providerResults[index] = new HostedProviderCapabilityDto(
                provider,
                configured,
                reachable,
                !configured
                    ? "Not configured."
                    : reachable == true
                        ? "Reachable."
                        : "Configured but unreachable.");
        }

        RuntimeCapabilitiesSnapshot runtime;
        try
        {
            runtime = _runtimeProbe.Capture();
        }
        catch
        {
            runtime = new RuntimeCapabilitiesSnapshot(
                RuntimeInformation.ProcessArchitecture.ToString(),
                Environment.ProcessorCount,
                RuntimeInformation.OSDescription,
                null,
                [
                    new RuntimeComputeBackend("CPU", true, []),
                    new RuntimeComputeBackend("CUDA", false, []),
                    new RuntimeComputeBackend("CoreML", false, []),
                    new RuntimeComputeBackend("OpenVINO", false, []),
                ]);
        }
        return new SystemCapabilitiesDto
        {
            CollectedAtUtc = _timeProvider.GetUtcNow().UtcDateTime.ToString("O"),
            HostedProviders = providerResults,
            ComputeBackends = runtime.ComputeBackends
                .Select(backend => new ComputeBackendCapabilityDto(
                    backend.Backend,
                    backend.Available,
                    backend.Devices,
                    backend.Available ? "Available." : "Unavailable."))
                .ToArray(),
            Architecture = runtime.Architecture,
            LogicalProcessorCount = runtime.LogicalProcessorCount,
            OsDescription = runtime.OsDescription,
            HardwareName = runtime.HardwareName,
        };
    }
}
