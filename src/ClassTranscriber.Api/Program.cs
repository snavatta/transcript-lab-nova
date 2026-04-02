using System.Text.Json;
using System.Text.Json.Serialization;
using ClassTranscriber.Api.Endpoints;
using ClassTranscriber.Api.Jobs;
using ClassTranscriber.Api.Media;
using ClassTranscriber.Api.Persistence;
using ClassTranscriber.Api.Services;
using ClassTranscriber.Api.Storage;
using ClassTranscriber.Api.Transcription;
using Microsoft.EntityFrameworkCore;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console());

    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();

    builder.Services.ConfigureHttpJsonOptions(options =>
    {
        options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

    builder.Services.AddHttpClient("WhisperModelDownloads", client =>
    {
        client.Timeout = Timeout.InfiniteTimeSpan;
    });

    // Storage
    builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection("Storage"));
    builder.Services.AddSingleton<IFileStorage, LocalFileStorage>();

    // Services
    builder.Services.AddScoped<IFolderService, FolderService>();
    builder.Services.AddScoped<ISettingsService, SettingsService>();
    builder.Services.AddScoped<IProjectService, ProjectService>();
    builder.Services.AddScoped<IUploadService, UploadService>();
    builder.Services.AddScoped<IQueueService, QueueService>();
    builder.Services.AddScoped<IExportService, ExportService>();

    // Media processing
    builder.Services.Configure<FfmpegOptions>(o => o.FFmpegPath = builder.Configuration["Transcription:FFmpegPath"] ?? "ffmpeg");
    builder.Services.AddSingleton<IMediaInspector, FfmpegMediaInspector>();
    builder.Services.AddSingleton<IAudioExtractor, FfmpegAudioExtractor>();
    builder.Services.AddSingleton<IAudioNormalizer, FfmpegAudioNormalizer>();

    // Transcription
    builder.Services.Configure<WhisperOptions>(o =>
    {
        o.WhisperCliPath = builder.Configuration["Transcription:WhisperCliPath"] ?? "whisper-cli";
        o.ModelsPath = builder.Configuration["Transcription:ModelsPath"]
            ?? Path.Combine(
                builder.Configuration["Storage:BasePath"] ?? "/data",
                builder.Configuration["Storage:ModelsPath"] ?? "models");
        if (bool.TryParse(builder.Configuration["Transcription:AutoDownloadModels"], out var autoDownloadModels))
            o.AutoDownloadModels = autoDownloadModels;
        o.ModelDownloadBaseUrl = builder.Configuration["Transcription:ModelDownloadBaseUrl"] ?? o.ModelDownloadBaseUrl;
    });
    builder.Services.Configure<SherpaOnnxOptions>(o =>
    {
        o.PythonPath = builder.Configuration["Transcription:SherpaOnnx:PythonPath"]
            ?? SherpaOnnxCliTranscriptionEngine.DefaultPythonExecutableName;
        o.AdapterScriptPath = builder.Configuration["Transcription:SherpaOnnx:AdapterScriptPath"]
            ?? Path.Combine("Tools", "sherpa_onnx_adapter.py");
        o.ModelsPath = builder.Configuration["Transcription:SherpaOnnx:ModelsPath"]
            ?? Path.Combine(
                builder.Configuration["Storage:BasePath"] ?? "/data",
                builder.Configuration["Storage:ModelsPath"] ?? "models",
                "sherpa-onnx");
        o.Provider = builder.Configuration["Transcription:SherpaOnnx:Provider"] ?? o.Provider;
        if (int.TryParse(builder.Configuration["Transcription:SherpaOnnx:NumThreads"], out var numThreads))
            o.NumThreads = numThreads;
    });
    builder.Services.AddSingleton<IRegisteredTranscriptionEngine, WhisperCliTranscriptionEngine>();
    builder.Services.AddSingleton<IRegisteredTranscriptionEngine, SherpaOnnxCliTranscriptionEngine>();
    builder.Services.AddSingleton<ITranscriptionEngineRegistry, TranscriptionEngineRegistry>();

    // Background worker
    builder.Services.Configure<TranscriptionWorkerOptions>(o =>
    {
        if (int.TryParse(builder.Configuration["Transcription:PollingIntervalSeconds"], out var interval))
            o.PollingIntervalSeconds = interval;
        if (int.TryParse(builder.Configuration["Transcription:WorkerConcurrency"], out var concurrency))
            o.WorkerConcurrency = concurrency;
    });
    builder.Services.AddHostedService<TranscriptionWorkerService>();

    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
        {
            policy.WithOrigins("http://localhost:5173")
                .AllowAnyHeader()
                .AllowAnyMethod();
        });
    });

    var app = builder.Build();

    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
    }

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseCors();
    app.UseStaticFiles();

    app.MapGet("/api/health", () => Results.Ok(new { status = "healthy" }))
        .WithName("HealthCheck")
        .WithTags("Health");

    app.MapFolderEndpoints();
    app.MapSettingsEndpoints();
    app.MapProjectEndpoints();
    app.MapUploadEndpoints();
    app.MapQueueEndpoints();
    app.MapTranscriptEndpoints();
    app.MapMediaEndpoints();
    app.MapExportEndpoints();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program { }
