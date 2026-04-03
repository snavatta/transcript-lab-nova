using System.Text.Json;
using System.Text.Json.Serialization;
using ClassTranscriber.Api.Contracts;
using ClassTranscriber.Api.Domain;
using ClassTranscriber.Api.Endpoints;
using ClassTranscriber.Api.Frontend;
using ClassTranscriber.Api.Jobs;
using ClassTranscriber.Api.Media;
using ClassTranscriber.Api.Persistence;
using ClassTranscriber.Api.Services;
using ClassTranscriber.Api.Storage;
using ClassTranscriber.Api.Transcription;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClassTranscriber.Api.Tests;

public class TestWebApplicationFactory : IDisposable, IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly WebApplication _app;
    private readonly string _storageBasePath;
    private readonly string? _webRootPath;
    private readonly IReadOnlyList<IRegisteredTranscriptionEngine> _transcriptionEngines;
    public HttpClient Client { get; }
    public IServiceProvider Services => _app.Services;

    public TestWebApplicationFactory(
        IEnumerable<IRegisteredTranscriptionEngine>? transcriptionEngines = null,
        bool includeFrontendAppShell = false,
        long? maxRequestBodySizeBytes = null)
    {
        _transcriptionEngines = transcriptionEngines?.ToArray()
            ?? [
                new NoOpTranscriptionEngine("SherpaOnnx", ["small", "medium"]),
                new NoOpTranscriptionEngine("SherpaOnnxSenseVoice", ["small"]),
                new NoOpTranscriptionEngine("WhisperNet", ["tiny", "base", "small", "medium", "large"]),
                new NoOpTranscriptionEngine("WhisperNetCuda", ["tiny", "base", "small", "medium", "large"]),
                new NoOpTranscriptionEngine("WhisperNetOpenVino", ["tiny", "base", "small", "medium", "large"]),
            ];
        var resolvedMaxRequestBodySizeBytes = maxRequestBodySizeBytes is > 0
            ? maxRequestBodySizeBytes.Value
            : UploadOptions.DefaultMaxRequestBodySizeBytes;
        _storageBasePath = Path.Combine(Path.GetTempPath(), $"transcriptlab-test-{Guid.NewGuid():N}");

        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _webRootPath = includeFrontendAppShell ? CreateTestWebRoot() : null;
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = Array.Empty<string>(),
            EnvironmentName = "Testing",
            WebRootPath = _webRootPath,
        });

        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        });

        builder.Services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite(_connection));

        builder.Services.AddHttpClient("WhisperModelDownloads", client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
        });
        builder.Services.AddHttpClient("SherpaOnnxModelDownloads", client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
        });

        builder.Services.Configure<StorageOptions>(o => o.BasePath = _storageBasePath);
        builder.Services.Configure<UploadOptions>(options =>
        {
            options.MaxRequestBodySizeBytes = resolvedMaxRequestBodySizeBytes;
        });
        builder.Services.Configure<FormOptions>(options =>
        {
            options.MultipartBodyLengthLimit = resolvedMaxRequestBodySizeBytes;
        });
        builder.Services.AddSingleton<IFileStorage, LocalFileStorage>();

        builder.Services.AddScoped<IFolderService, FolderService>();
        builder.Services.AddScoped<ISettingsService, SettingsService>();
        builder.Services.AddScoped<IProjectService, ProjectService>();
        builder.Services.AddScoped<IUploadService, UploadService>();
        builder.Services.AddScoped<IQueueService, QueueService>();
        builder.Services.AddScoped<IExportService, ExportService>();
        builder.Services.AddScoped<IDiagnosticsService, DiagnosticsService>();
        builder.Services.AddScoped<ITranscriptionModelManagerService, TranscriptionModelManagerService>();
        builder.Services.AddSingleton<IRuntimeMetricsSampler, RuntimeMetricsSampler>();

        builder.Services.Configure<WhisperNetOptions>(o =>
        {
            o.ModelsPath = Path.Combine(_storageBasePath, "models");
        });
        builder.Services.Configure<SherpaOnnxOptions>(o =>
        {
            o.ModelsPath = Path.Combine(_storageBasePath, "models", "sherpa-onnx");
        });
        builder.Services.Configure<SherpaOnnxSenseVoiceOptions>(o =>
        {
            o.ModelsPath = Path.Combine(_storageBasePath, "models", "sherpa-onnx-sense-voice");
        });

        builder.Services.AddSingleton<IMediaInspector, NoOpMediaInspector>();
        builder.Services.AddSingleton<IAudioExtractor, NoOpAudioExtractor>();
        builder.Services.AddSingleton<IAudioNormalizer, NoOpAudioNormalizer>();
        builder.Services.AddSingleton<IActiveJobCancellation, NoOpActiveJobCancellation>();
        builder.Services.AddSingleton<ICudaEnvironmentProbe, CudaEnvironmentProbe>();
        builder.Services.AddSingleton<IOpenVinoEnvironmentProbe, OpenVinoEnvironmentProbe>();
        foreach (var engine in _transcriptionEngines)
            builder.Services.AddSingleton<IRegisteredTranscriptionEngine>(engine);
        builder.Services.AddSingleton<ITranscriptionEngineRegistry, TranscriptionEngineRegistry>();

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Limits.MaxRequestBodySize = resolvedMaxRequestBodySizeBytes;
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        _app = builder.Build();

        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();
        }

        if (_webRootPath is not null)
            _app.UseFrontendAppShellAssets();

        _app.MapGet("/api/health", () => Results.Ok(new { status = "healthy" }));
        _app.MapFolderEndpoints();
        _app.MapSettingsEndpoints();
        _app.MapDiagnosticsEndpoints();
        _app.MapProjectEndpoints();
        _app.MapUploadEndpoints();
        _app.MapQueueEndpoints();
        _app.MapTranscriptEndpoints();
        _app.MapMediaEndpoints();
        _app.MapExportEndpoints();
        if (_webRootPath is not null)
            _app.MapFrontendAppShellFallback();

        _app.StartAsync().GetAwaiter().GetResult();

        Client = new HttpClient { BaseAddress = new Uri(_app.Urls.First()) };
    }

    public void Dispose()
    {
        Client.Dispose();
        _app.StopAsync().GetAwaiter().GetResult();
        _app.DisposeAsync().GetAwaiter().GetResult();
        _connection.Dispose();
        if (Directory.Exists(_storageBasePath))
            Directory.Delete(_storageBasePath, recursive: true);
        DeleteTestWebRoot();
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
        _connection.Dispose();
        if (Directory.Exists(_storageBasePath))
            Directory.Delete(_storageBasePath, recursive: true);
        DeleteTestWebRoot();
    }

    private static string CreateTestWebRoot()
    {
        var webRootPath = Path.Combine(Path.GetTempPath(), $"transcriptlab-wwwroot-{Guid.NewGuid():N}");
        Directory.CreateDirectory(webRootPath);
        File.WriteAllText(
            Path.Combine(webRootPath, "index.html"),
            """
            <!doctype html>
            <html lang="en">
              <body>
                <div id="root">TranscriptLab Nova</div>
              </body>
            </html>
            """);
        return webRootPath;
    }

    private void DeleteTestWebRoot()
    {
        if (_webRootPath is not null && Directory.Exists(_webRootPath))
            Directory.Delete(_webRootPath, recursive: true);
    }
}

public class NoOpActiveJobCancellation : IActiveJobCancellation
{
    public bool TryCancel(Guid projectId) => false;
}

public class NoOpMediaInspector : IMediaInspector
{
    public Task<MediaInfo?> InspectAsync(string filePath, CancellationToken ct = default)
        => Task.FromResult<MediaInfo?>(new MediaInfo(60000, MediaType.Audio, "audio/wav"));
}

public class NoOpAudioExtractor : IAudioExtractor
{
    public Task<string> ExtractAudioAsync(string inputPath, string outputPath, CancellationToken ct = default)
    {
        File.WriteAllBytes(outputPath, []);
        return Task.FromResult(outputPath);
    }
}

public class NoOpAudioNormalizer : IAudioNormalizer
{
    public Task<string> NormalizeAsync(string inputPath, string outputDir, CancellationToken ct = default)
    {
        var output = Path.Combine(outputDir, "normalized.wav");
        File.Copy(inputPath, output, true);
        return Task.FromResult(output);
    }
}

public class NoOpTranscriptionEngine(
    string engineId,
    IReadOnlyCollection<string> supportedModels,
    string? availabilityError = null) : IRegisteredTranscriptionEngine
{
    public string EngineId { get; } = engineId;

    public IReadOnlyCollection<string> SupportedModels { get; } = supportedModels;

    public string? GetAvailabilityError() => availabilityError;

    public string? GetProbeError() => availabilityError;

    public Task<TranscriptionResult> TranscribeAsync(string audioPath, ProjectSettings settings, CancellationToken ct = default)
        => Task.FromResult(new TranscriptionResult(
            "Test transcript",
            [new ClassTranscriber.Api.Contracts.TranscriptSegmentDto { StartMs = 0, EndMs = 5000, Text = "Test transcript" }],
            "en",
            60000
        ));
}
