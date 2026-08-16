using System.Net;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.Json;
using ClassTranscriber.Api.Contracts;
using ClassTranscriber.Api.Endpoints;
using ClassTranscriber.Api.Services;
using ClassTranscriber.Api.Transcription;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ClassTranscriber.Api.Tests;

public sealed class SystemCapabilitiesTests
{
    [Fact]
    public async Task Capabilities_ReturnsExactSanitizedContract()
    {
        await using var app = BuildApp(new StubSystemCapabilitiesService());
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };

        var response = await client.GetAsync("/api/settings/capabilities");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(
            "collectedAtUtc", "hostedProviders", "computeBackends", "architecture",
            "logicalProcessorCount", "osDescription", "hardwareName");
        json.RootElement.GetProperty("hostedProviders").EnumerateArray()
            .Select(provider => provider.GetProperty("provider").GetString())
            .Should().Equal("OpenRouter", "xAI");
        json.RootElement.GetProperty("hostedProviders")[1].GetProperty("reachable").ValueKind
            .Should().Be(JsonValueKind.Null);
        json.RootElement.GetProperty("computeBackends").EnumerateArray()
            .Select(backend => backend.GetProperty("backend").GetString())
            .Should().Equal("CPU", "CUDA", "CoreML", "OpenVINO");
        json.RootElement.GetProperty("hardwareName").ValueKind.Should().Be(JsonValueKind.Null);

        await app.StopAsync();
    }

    [Fact]
    public async Task Capabilities_RedactsSensitiveProbeDetails()
    {
        const string secret = "SUPER_SECRET_MARKER";
        const string url = "https://private.example.invalid/sensitive";
        const string path = "/private/install/marker";
        var service = new SystemCapabilitiesService(
            new ThrowingHostedProviderCapabilitiesProbe($"{secret} {url} {path} provider-body stack trace"),
            new StubRuntimeCapabilitiesProbe());

        var result = await service.GetAsync();
        var payload = JsonSerializer.Serialize(result);

        payload.Should().NotContain(secret).And.NotContain(url).And.NotContain(path)
            .And.NotContain("provider-body").And.NotContain("stack trace");
        result.HostedProviders.Select(provider => provider.Status).Should().OnlyContain(status =>
            status == "Configured but unreachable." || status == "Not configured.");
    }

    [Fact]
    public async Task Capabilities_ConcurrentRequestsCoalesceProbeExecution()
    {
        var providerProbe = new BlockingHostedProviderCapabilitiesProbe();
        var runtimeProbe = new CountingRuntimeCapabilitiesProbe();
        var service = new SystemCapabilitiesService(providerProbe, runtimeProbe);
        var ready = new CountdownEvent(24);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = Enumerable.Range(0, 24)
            .Select(_ => Task.Run(async () =>
            {
                ready.Signal();
                await start.Task;
                return await service.GetAsync();
            }))
            .ToArray();

        ready.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue();
        start.SetResult();
        try
        {
            await Task.WhenAny(
                providerProbe.SecondCallStarted.Task,
                Task.Delay(TimeSpan.FromMilliseconds(250)));

            providerProbe.ReachabilityProbeCount.Should().Be(1);
        }
        finally
        {
            providerProbe.Release.SetResult();
            await Task.WhenAll(requests);
        }

        providerProbe.ReachabilityProbeCount.Should().Be(2);
        runtimeProbe.CaptureCount.Should().Be(1);
    }

    [Fact]
    public async Task Endpoint_ConcurrentRequestsCoalesceProbeExecution()
    {
        var providerProbe = new BlockingHostedProviderCapabilitiesProbe();
        var runtimeProbe = new CountingRuntimeCapabilitiesProbe();
        var service = new SystemCapabilitiesService(providerProbe, runtimeProbe);
        await using var app = BuildApp(service);
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };

        var requests = Enumerable.Range(0, 24)
            .Select(_ => client.GetAsync("/api/settings/capabilities"))
            .ToArray();
        try
        {
            await providerProbe.FirstCallStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await Task.WhenAny(providerProbe.SecondCallStarted.Task, Task.Delay(TimeSpan.FromMilliseconds(250)));
            providerProbe.ReachabilityProbeCount.Should().Be(1);
        }
        finally
        {
            providerProbe.Release.TrySetResult();
        }

        var responses = await Task.WhenAll(requests);
        responses.Should().OnlyContain(response => response.StatusCode == HttpStatusCode.OK);
        providerProbe.ReachabilityProbeCount.Should().Be(2);
        runtimeProbe.CaptureCount.Should().Be(1);
        foreach (var response in responses)
            response.Dispose();
        await app.StopAsync();
    }

    [Fact]
    public async Task Capabilities_CancellingOneWaiterDoesNotCancelSharedCollection()
    {
        var providerProbe = new BlockingHostedProviderCapabilitiesProbe();
        var runtimeProbe = new CountingRuntimeCapabilitiesProbe();
        var service = new SystemCapabilitiesService(providerProbe, runtimeProbe);
        using var cancelledWaiter = new CancellationTokenSource();

        var cancelledRequest = service.GetAsync(cancelledWaiter.Token);
        await providerProbe.FirstCallStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var survivingRequest = service.GetAsync();
        cancelledWaiter.Cancel();

        await FluentActions.Invoking(() => cancelledRequest)
            .Should().ThrowAsync<OperationCanceledException>();
        providerProbe.Release.SetResult();
        var result = await survivingRequest;

        result.HostedProviders.Should().HaveCount(2);
        providerProbe.ReachabilityProbeCount.Should().Be(2);
        runtimeProbe.CaptureCount.Should().Be(1);
    }

    [Fact]
    public async Task Capabilities_CacheExpiresAfterBoundedTtl()
    {
        var providerProbe = new CountingHostedProviderCapabilitiesProbe();
        var runtimeProbe = new CountingRuntimeCapabilitiesProbe();
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 8, 16, 0, 0, 0, TimeSpan.Zero));
        var cacheDuration = TimeSpan.FromSeconds(5);
        var service = new SystemCapabilitiesService(
            providerProbe,
            runtimeProbe,
            timeProvider,
            cacheDuration);

        var first = await service.GetAsync();
        var cached = await service.GetAsync();
        first.Should().BeSameAs(cached);
        providerProbe.ReachabilityProbeCount.Should().Be(2);
        runtimeProbe.CaptureCount.Should().Be(1);

        timeProvider.Advance(cacheDuration);
        var refreshed = await service.GetAsync();

        refreshed.Should().NotBeSameAs(first);
        providerProbe.ReachabilityProbeCount.Should().Be(4);
        runtimeProbe.CaptureCount.Should().Be(2);
    }

    [Fact]
    public async Task ProviderProbe_EnforcesConfiguredAndUnconfiguredPolicyWithoutLeakingConfiguration()
    {
        var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var probe = CreateProviderProbe(
            handler,
            openRouterBaseUrl: "https://provider.example.invalid/api/v1",
            openRouterApiKey: "configured-key",
            xaiBaseUrl: "http://insecure.example.invalid/v1",
            xaiApiKey: "configured-key");

        probe.IsConfigured("OpenRouter").Should().BeTrue();
        probe.IsConfigured("xAI").Should().BeFalse();
        (await probe.IsReachableAsync("OpenRouter", CancellationToken.None)).Should().BeTrue();
        (await probe.IsReachableAsync("xAI", CancellationToken.None)).Should().BeFalse();
        handler.RequestCount.Should().Be(1);
    }

    [Fact]
    public async Task ProviderProbe_HungRequestIsBounded()
    {
        var handler = new RecordingHttpMessageHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var probe = CreateProviderProbe(handler, probeTimeout: TimeSpan.FromMilliseconds(50));
        var stopwatch = Stopwatch.StartNew();

        var reachable = await probe.IsReachableAsync("OpenRouter", CancellationToken.None);

        stopwatch.Stop();
        reachable.Should().BeFalse();
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task ProviderProbe_RepeatedCallerCancellationPropagatesPromptly()
    {
        var handler = new RecordingHttpMessageHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var probe = CreateProviderProbe(handler, probeTimeout: TimeSpan.FromSeconds(5));

        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));
            await FluentActions.Invoking(() => probe.IsReachableAsync("OpenRouter", cts.Token))
                .Should().ThrowAsync<OperationCanceledException>();
        }
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void OpenVinoHungChildProbe_IsBoundedAndReportsUnavailable()
    {
        if (!OperatingSystem.IsLinux())
            return;

        var probeTimeout = TimeSpan.FromMilliseconds(50);
        var terminationTimeout = TimeSpan.FromMilliseconds(100);
        var assertionDeadline = probeTimeout + terminationTimeout + TimeSpan.FromSeconds(1);
        using var fixture = new HungOpenVinoExecutableFixture();
        var probe = new RuntimeCapabilitiesProbe(
            new StubCudaProbe(),
            Options.Create(new OpenVinoWhisperSidecarOptions { PythonPath = fixture.ExecutablePath }),
            probeTimeout,
            terminationTimeout);

        for (var attempt = 0; attempt < 10; attempt++)
        {
            var stopwatch = Stopwatch.StartNew();

            var result = probe.Capture();

            stopwatch.Stop();
            result.ComputeBackends.Single(backend => backend.Backend == "OpenVINO").Available.Should().BeFalse();
            stopwatch.Elapsed.Should().BeLessThan(assertionDeadline);
        }
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void OpenVinoFixture_FailingPathStillCleansOwnedRoot()
    {
        if (!OperatingSystem.IsLinux())
            return;

        string? ownedRoot = null;
        var action = () =>
        {
            using var fixture = new HungOpenVinoExecutableFixture();
            ownedRoot = fixture.RootPath;
            throw new FixtureFailureException();
        };

        action.Should().Throw<FixtureFailureException>();
        Directory.Exists(ownedRoot).Should().BeFalse();
    }

    private static HostedProviderCapabilitiesProbe CreateProviderProbe(
        HttpMessageHandler handler,
        string openRouterBaseUrl = "https://openrouter.example.invalid/api/v1",
        string openRouterApiKey = "configured-key",
        string xaiBaseUrl = "https://xai.example.invalid/v1",
        string xaiApiKey = "configured-key",
        TimeSpan? probeTimeout = null)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://provider.example.invalid/") };
        return new HostedProviderCapabilitiesProbe(
            Options.Create(new OpenRouterOptions { BaseUrl = openRouterBaseUrl, ApiKey = openRouterApiKey }),
            Options.Create(new XaiOptions { BaseUrl = xaiBaseUrl, ApiKey = xaiApiKey }),
            new StubHttpClientFactory(httpClient),
            probeTimeout);
    }

    private static WebApplication BuildApp(ISystemCapabilitiesService service)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(service);
        var app = builder.Build();
        app.MapSystemCapabilitiesEndpoints();
        return app;
    }

    private sealed class StubSystemCapabilitiesService : ISystemCapabilitiesService
    {
        public Task<SystemCapabilitiesDto> GetAsync(CancellationToken ct = default) => Task.FromResult(new SystemCapabilitiesDto
        {
            CollectedAtUtc = "2026-08-16T00:00:00.0000000Z",
            HostedProviders =
            [
                new HostedProviderCapabilityDto("OpenRouter", true, true, "Reachable."),
                new HostedProviderCapabilityDto("xAI", false, null, "Not configured."),
            ],
            ComputeBackends =
            [
                new ComputeBackendCapabilityDto("CPU", true, ["Test CPU"], "Available."),
                new ComputeBackendCapabilityDto("CUDA", false, [], "Unavailable."),
                new ComputeBackendCapabilityDto("CoreML", false, [], "Unavailable."),
                new ComputeBackendCapabilityDto("OpenVINO", true, ["GPU"], "Available."),
            ],
            Architecture = "X64",
            LogicalProcessorCount = 4,
            OsDescription = "Test OS",
            HardwareName = null,
        });
    }

    private sealed class ThrowingHostedProviderCapabilitiesProbe(string message) : IHostedProviderCapabilitiesProbe
    {
        public bool IsConfigured(string provider) => true;

        public Task<bool> IsReachableAsync(string provider, CancellationToken ct) =>
            throw new InvalidOperationException(message);
    }

    private sealed class StubRuntimeCapabilitiesProbe : IRuntimeCapabilitiesProbe
    {
        public RuntimeCapabilitiesSnapshot Capture() => new(
            "X64",
            4,
            "Test OS",
            null,
            [
                new RuntimeComputeBackend("CPU", true, ["Test CPU"]),
                new RuntimeComputeBackend("CUDA", false, []),
                new RuntimeComputeBackend("CoreML", false, []),
                new RuntimeComputeBackend("OpenVINO", false, []),
            ]);
    }

    private sealed class CountingRuntimeCapabilitiesProbe : IRuntimeCapabilitiesProbe
    {
        private int _captureCount;
        public int CaptureCount => Volatile.Read(ref _captureCount);

        public RuntimeCapabilitiesSnapshot Capture()
        {
            Interlocked.Increment(ref _captureCount);
            return new StubRuntimeCapabilitiesProbe().Capture();
        }
    }

    private sealed class BlockingHostedProviderCapabilitiesProbe : IHostedProviderCapabilitiesProbe
    {
        private int _reachabilityProbeCount;
        public int ReachabilityProbeCount => Volatile.Read(ref _reachabilityProbeCount);
        public TaskCompletionSource FirstCallStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondCallStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsConfigured(string provider) => true;

        public async Task<bool> IsReachableAsync(string provider, CancellationToken ct)
        {
            var count = Interlocked.Increment(ref _reachabilityProbeCount);
            if (count == 1)
                FirstCallStarted.TrySetResult();
            if (count == 2)
                SecondCallStarted.TrySetResult();
            await Release.Task.WaitAsync(ct);
            return true;
        }
    }

    private sealed class CountingHostedProviderCapabilitiesProbe : IHostedProviderCapabilitiesProbe
    {
        private int _reachabilityProbeCount;
        public int ReachabilityProbeCount => Volatile.Read(ref _reachabilityProbeCount);
        public bool IsConfigured(string provider) => true;

        public Task<bool> IsReachableAsync(string provider, CancellationToken ct)
        {
            Interlocked.Increment(ref _reachabilityProbeCount);
            return Task.FromResult(true);
        }
    }

    [SupportedOSPlatform("linux")]
    private sealed class HungOpenVinoExecutableFixture : IDisposable
    {
        public HungOpenVinoExecutableFixture()
        {
            RootPath = Path.Combine(Path.GetTempPath(), $"openvino-probe-{Guid.NewGuid():N}");
            Directory.CreateDirectory(RootPath);
            ExecutablePath = Path.Combine(RootPath, "hung-python");
            File.WriteAllText(ExecutablePath, "#!/bin/sh\nexec sleep 30\n");
            File.SetUnixFileMode(
                ExecutablePath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        public string RootPath { get; }
        public string ExecutablePath { get; }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
                Directory.Delete(RootPath, recursive: true);
        }
    }

    private sealed class FixtureFailureException : Exception;

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan elapsed) => _utcNow += elapsed;
    }

    private sealed class StubCudaProbe : ICudaEnvironmentProbe
    {
        public string? GetAvailabilityError() => "Unavailable.";
    }

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public RecordingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
            : this((request, _) => Task.FromResult(handler(request)))
        {
        }

        public RecordingHttpMessageHandler(
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return _handler(request, cancellationToken);
        }
    }
}
