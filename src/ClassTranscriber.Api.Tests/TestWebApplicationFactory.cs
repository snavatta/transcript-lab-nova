using System.Text.Json;
using System.Text.Json.Serialization;
using ClassTranscriber.Api.Contracts;
using ClassTranscriber.Api.Domain;
using ClassTranscriber.Api.Endpoints;
using ClassTranscriber.Api.Media;
using ClassTranscriber.Api.Persistence;
using ClassTranscriber.Api.Services;
using ClassTranscriber.Api.Storage;
using ClassTranscriber.Api.Transcription;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClassTranscriber.Api.Tests;

public class TestWebApplicationFactory : IDisposable, IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly WebApplication _app;
    public HttpClient Client { get; }

    public TestWebApplicationFactory()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.Environment.EnvironmentName = "Testing";

        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        });

        builder.Services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite(_connection));

        builder.Services.Configure<StorageOptions>(o =>
            o.BasePath = Path.Combine(Path.GetTempPath(), $"transcriptlab-test-{Guid.NewGuid():N}"));
        builder.Services.AddSingleton<IFileStorage, LocalFileStorage>();

        builder.Services.AddScoped<IFolderService, FolderService>();
        builder.Services.AddScoped<ISettingsService, SettingsService>();
        builder.Services.AddScoped<IProjectService, ProjectService>();
        builder.Services.AddScoped<IUploadService, UploadService>();
        builder.Services.AddScoped<IQueueService, QueueService>();
        builder.Services.AddScoped<IExportService, ExportService>();

        builder.Services.AddSingleton<IMediaInspector, NoOpMediaInspector>();
        builder.Services.AddSingleton<IAudioExtractor, NoOpAudioExtractor>();
        builder.Services.AddSingleton<IAudioNormalizer, NoOpAudioNormalizer>();
        builder.Services.AddSingleton<IRegisteredTranscriptionEngine>(new NoOpTranscriptionEngine("Whisper", ["tiny", "base", "small", "medium", "large"]));
        builder.Services.AddSingleton<IRegisteredTranscriptionEngine>(new NoOpTranscriptionEngine("SherpaOnnx", ["small", "medium"]));
        builder.Services.AddSingleton<ITranscriptionEngineRegistry, TranscriptionEngineRegistry>();

        builder.WebHost.UseUrls("http://127.0.0.1:0");

        _app = builder.Build();

        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();
        }

        _app.MapGet("/api/health", () => Results.Ok(new { status = "healthy" }));
        _app.MapFolderEndpoints();
        _app.MapSettingsEndpoints();
        _app.MapProjectEndpoints();
        _app.MapUploadEndpoints();
        _app.MapQueueEndpoints();
        _app.MapTranscriptEndpoints();
        _app.MapMediaEndpoints();
        _app.MapExportEndpoints();

        _app.StartAsync().GetAwaiter().GetResult();

        Client = new HttpClient { BaseAddress = new Uri(_app.Urls.First()) };
    }

    public void Dispose()
    {
        Client.Dispose();
        _app.StopAsync().GetAwaiter().GetResult();
        _app.DisposeAsync().GetAwaiter().GetResult();
        _connection.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
        _connection.Dispose();
    }
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

public class NoOpTranscriptionEngine(string engineId, IReadOnlyCollection<string> supportedModels) : IRegisteredTranscriptionEngine
{
    public string EngineId { get; } = engineId;

    public IReadOnlyCollection<string> SupportedModels { get; } = supportedModels;

    public Task<TranscriptionResult> TranscribeAsync(string audioPath, ProjectSettings settings, CancellationToken ct = default)
        => Task.FromResult(new TranscriptionResult(
            "Test transcript",
            [new TranscriptSegmentDto { StartMs = 0, EndMs = 5000, Text = "Test transcript" }],
            "en",
            60000
        ));
}
