using System.Text.Json;
using System.Text.Json.Serialization;
using ClassTranscriber.Api.Endpoints;
using ClassTranscriber.Api.Frontend;
using ClassTranscriber.Api.Jobs;
using ClassTranscriber.Api.Media;
using ClassTranscriber.Api.Persistence;
using ClassTranscriber.Api.Services;
using ClassTranscriber.Api.Storage;
using ClassTranscriber.Api.Transcription;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    var uploadOptions = builder.Configuration.GetSection(UploadOptions.SectionName).Get<UploadOptions>() ?? new UploadOptions();
    if (uploadOptions.MaxRequestBodySizeBytes <= 0)
        uploadOptions.MaxRequestBodySizeBytes = UploadOptions.DefaultMaxRequestBodySizeBytes;

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console());

    builder.WebHost.ConfigureKestrel(options =>
    {
        options.Limits.MaxRequestBodySize = uploadOptions.MaxRequestBodySizeBytes;
    });

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
    builder.Services.AddHttpClient("SherpaOnnxModelDownloads", client =>
    {
        client.Timeout = Timeout.InfiniteTimeSpan;
    });
    builder.Services.AddHttpClient("OpenVinoGenAiModelDownloads", client =>
    {
        client.Timeout = Timeout.InfiniteTimeSpan;
    });

    // Storage
    builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection("Storage"));
    builder.Services.Configure<UploadOptions>(options =>
    {
        options.MaxRequestBodySizeBytes = uploadOptions.MaxRequestBodySizeBytes;
    });
    builder.Services.Configure<FormOptions>(options =>
    {
        options.MultipartBodyLengthLimit = uploadOptions.MaxRequestBodySizeBytes;
    });
    builder.Services.AddSingleton<IFileStorage, LocalFileStorage>();

    // Services
    builder.Services.AddScoped<IFolderService, FolderService>();
    builder.Services.AddScoped<ISettingsService, SettingsService>();
    builder.Services.AddScoped<IProjectService, ProjectService>();
    builder.Services.AddScoped<IUploadService, UploadService>();
    builder.Services.AddScoped<IQueueService, QueueService>();
    builder.Services.AddScoped<IExportService, ExportService>();
    builder.Services.AddScoped<IDiagnosticsService, DiagnosticsService>();
    builder.Services.AddScoped<ITranscriptionModelManagerService, TranscriptionModelManagerService>();
    builder.Services.AddSingleton<IRuntimeMetricsSampler, RuntimeMetricsSampler>();

    // Media processing
    builder.Services.Configure<FfmpegOptions>(o => o.FFmpegPath = builder.Configuration["Transcription:FFmpegPath"] ?? "ffmpeg");
    builder.Services.AddSingleton<IMediaInspector, FfmpegMediaInspector>();
    builder.Services.AddSingleton<IAudioExtractor, FfmpegAudioExtractor>();
    builder.Services.AddSingleton<IAudioNormalizer, FfmpegAudioNormalizer>();

    // Transcription
    builder.Services.Configure<SherpaOnnxOptions>(o =>
    {
        o.PythonPath = builder.Configuration["Transcription:SherpaOnnx:PythonPath"]
            ?? o.PythonPath;
        o.AdapterScriptPath = builder.Configuration["Transcription:SherpaOnnx:AdapterScriptPath"]
            ?? o.AdapterScriptPath;
        o.ModelsPath = builder.Configuration["Transcription:SherpaOnnx:ModelsPath"]
            ?? Path.Combine(
                builder.Configuration["Storage:BasePath"] ?? "/data",
                builder.Configuration["Storage:ModelsPath"] ?? "models",
                "sherpa-onnx");
        o.Provider = builder.Configuration["Transcription:SherpaOnnx:Provider"] ?? o.Provider;
        if (int.TryParse(builder.Configuration["Transcription:SherpaOnnx:NumThreads"], out var numThreads))
            o.NumThreads = numThreads;
        if (bool.TryParse(
                builder.Configuration["Transcription:SherpaOnnx:AutoDownloadModels"]
                ?? builder.Configuration["Transcription:AutoDownloadModels"],
                out var sherpaAutoDownload))
            o.AutoDownloadModels = sherpaAutoDownload;
        if (bool.TryParse(
                builder.Configuration["Transcription:SherpaOnnx:LogSegments"]
                ?? builder.Configuration["Transcription:LogSegments"],
                out var sherpaLogSegments))
            o.LogSegments = sherpaLogSegments;
        o.ModelDownloadBaseUrl = builder.Configuration["Transcription:SherpaOnnx:ModelDownloadBaseUrl"] ?? o.ModelDownloadBaseUrl;
        o.WorkerPath = builder.Configuration["Transcription:SherpaOnnx:WorkerPath"] ?? o.WorkerPath;
        o.DotNetHostPath = builder.Configuration["Transcription:SherpaOnnx:DotNetHostPath"] ?? o.DotNetHostPath;
    });
    builder.Services.Configure<SherpaOnnxSenseVoiceOptions>(o =>
    {
        o.ModelsPath = builder.Configuration["Transcription:SherpaOnnxSenseVoice:ModelsPath"]
            ?? Path.Combine(
                builder.Configuration["Storage:BasePath"] ?? "/data",
                builder.Configuration["Storage:ModelsPath"] ?? "models",
                "sherpa-onnx-sense-voice");
        o.Provider = builder.Configuration["Transcription:SherpaOnnxSenseVoice:Provider"] ?? o.Provider;
        if (int.TryParse(builder.Configuration["Transcription:SherpaOnnxSenseVoice:NumThreads"], out var numThreads))
            o.NumThreads = numThreads;
        if (bool.TryParse(
                builder.Configuration["Transcription:SherpaOnnxSenseVoice:AutoDownloadModels"]
                ?? builder.Configuration["Transcription:AutoDownloadModels"],
                out var senseVoiceAutoDownload))
            o.AutoDownloadModels = senseVoiceAutoDownload;
        if (bool.TryParse(
                builder.Configuration["Transcription:SherpaOnnxSenseVoice:LogSegments"]
                ?? builder.Configuration["Transcription:LogSegments"],
                out var senseVoiceLogSegments))
            o.LogSegments = senseVoiceLogSegments;
        o.ModelDownloadBaseUrl = builder.Configuration["Transcription:SherpaOnnxSenseVoice:ModelDownloadBaseUrl"] ?? o.ModelDownloadBaseUrl;
        o.WorkerPath = builder.Configuration["Transcription:SherpaOnnxSenseVoice:WorkerPath"]
            ?? builder.Configuration["Transcription:SherpaOnnx:WorkerPath"]
            ?? o.WorkerPath;
        o.DotNetHostPath = builder.Configuration["Transcription:SherpaOnnxSenseVoice:DotNetHostPath"]
            ?? builder.Configuration["Transcription:SherpaOnnx:DotNetHostPath"]
            ?? o.DotNetHostPath;
    });
    builder.Services.Configure<WhisperNetOptions>(o =>
    {
        o.ModelsPath = builder.Configuration["Transcription:WhisperNet:ModelsPath"]
            ?? builder.Configuration["Transcription:ModelsPath"]
            ?? Path.Combine(
                builder.Configuration["Storage:BasePath"] ?? "/data",
                builder.Configuration["Storage:ModelsPath"] ?? "models");
        if (bool.TryParse(
                builder.Configuration["Transcription:WhisperNet:AutoDownloadModels"]
                ?? builder.Configuration["Transcription:AutoDownloadModels"],
                out var autoDownload))
            o.AutoDownloadModels = autoDownload;
        if (bool.TryParse(
                builder.Configuration["Transcription:WhisperNet:LogSegments"]
                ?? builder.Configuration["Transcription:LogSegments"],
                out var whisperLogSegments))
            o.LogSegments = whisperLogSegments;
        o.ModelDownloadBaseUrl = builder.Configuration["Transcription:WhisperNet:ModelDownloadBaseUrl"]
            ?? builder.Configuration["Transcription:ModelDownloadBaseUrl"]
            ?? o.ModelDownloadBaseUrl;
        o.WorkerPath = builder.Configuration["Transcription:WhisperNet:WorkerPath"] ?? o.WorkerPath;
        o.DotNetHostPath = builder.Configuration["Transcription:WhisperNet:DotNetHostPath"] ?? o.DotNetHostPath;
        o.OpenVinoDevice = builder.Configuration["Transcription:WhisperNet:OpenVinoDevice"] ?? o.OpenVinoDevice;
        o.OpenVinoCachePath = builder.Configuration["Transcription:WhisperNet:OpenVinoCachePath"];
    });
    builder.Services.Configure<OpenVinoGenAiOptions>(o =>
    {
        o.ModelsPath = builder.Configuration["Transcription:OpenVinoGenAi:ModelsPath"]
            ?? Path.Combine(
                builder.Configuration["Storage:BasePath"] ?? "/data",
                builder.Configuration["Storage:ModelsPath"] ?? "models",
                "openvino-genai");
        if (bool.TryParse(
                builder.Configuration["Transcription:OpenVinoGenAi:AutoDownloadModels"]
                ?? builder.Configuration["Transcription:AutoDownloadModels"],
                out var autoDownload))
            o.AutoDownloadModels = autoDownload;
        if (bool.TryParse(
                builder.Configuration["Transcription:OpenVinoGenAi:LogSegments"]
                ?? builder.Configuration["Transcription:LogSegments"],
                out var logSegments))
            o.LogSegments = logSegments;
        o.ModelDownloadBaseUrl = builder.Configuration["Transcription:OpenVinoGenAi:ModelDownloadBaseUrl"]
            ?? o.ModelDownloadBaseUrl;
        o.PythonPath = builder.Configuration["Transcription:OpenVinoGenAi:PythonPath"] ?? o.PythonPath;
        o.WorkerScriptPath = builder.Configuration["Transcription:OpenVinoGenAi:WorkerScriptPath"] ?? o.WorkerScriptPath;
        o.Device = builder.Configuration["Transcription:OpenVinoGenAi:Device"] ?? o.Device;
    });
    builder.Services.AddSingleton<IRegisteredTranscriptionEngine, SherpaOnnxTranscriptionEngine>();
    builder.Services.AddSingleton<IRegisteredTranscriptionEngine, SherpaOnnxSenseVoiceTranscriptionEngine>();
    builder.Services.AddSingleton<IRegisteredTranscriptionEngine, WhisperNetCpuTranscriptionEngine>();
    builder.Services.AddSingleton<IRegisteredTranscriptionEngine, WhisperNetCudaTranscriptionEngine>();
    builder.Services.AddSingleton<IRegisteredTranscriptionEngine, WhisperNetOpenVinoTranscriptionEngine>();
    builder.Services.AddSingleton<IRegisteredTranscriptionEngine, OpenVinoGenAiTranscriptionEngine>();
    builder.Services.AddSingleton<ICudaEnvironmentProbe, CudaEnvironmentProbe>();
    builder.Services.AddSingleton<IOpenVinoEnvironmentProbe, OpenVinoEnvironmentProbe>();
    builder.Services.AddSingleton<IOpenVinoGenAiEnvironmentProbe, OpenVinoGenAiEnvironmentProbe>();
    builder.Services.AddSingleton<ISherpaOnnxWorkerRunner, SherpaOnnxWorkerRunner>();
    builder.Services.AddSingleton<IWhisperNetWorkerRunner, WhisperNetWorkerRunner>();
    builder.Services.AddSingleton<IOpenVinoGenAiWorkerRunner, OpenVinoGenAiWorkerRunner>();
    builder.Services.AddSingleton<ITranscriptionEngineRegistry, TranscriptionEngineRegistry>();
    builder.Services.AddSingleton<ISpeakerDiarizer, BasicSpeakerDiarizer>();

    // Background worker
    builder.Services.Configure<TranscriptionWorkerOptions>(o =>
    {
        if (int.TryParse(builder.Configuration["Transcription:PollingIntervalSeconds"], out var interval))
            o.PollingIntervalSeconds = interval;
        if (int.TryParse(builder.Configuration["Transcription:WorkerConcurrency"], out var concurrency))
            o.WorkerConcurrency = concurrency;
    });
    builder.Services.AddHostedService<TranscriptionWorkerService>();
    builder.Services.AddSingleton<IActiveJobCancellation>(sp =>
        sp.GetServices<IHostedService>().OfType<TranscriptionWorkerService>().First());

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

        var openVinoProbe = scope.ServiceProvider.GetRequiredService<IOpenVinoEnvironmentProbe>();
        var cudaProbe = scope.ServiceProvider.GetRequiredService<ICudaEnvironmentProbe>();
        var openVinoGenAiProbe = scope.ServiceProvider.GetRequiredService<IOpenVinoGenAiEnvironmentProbe>();
        var startupLogger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
        var cudaProbeError = cudaProbe.GetAvailabilityError();
        if (cudaProbeError is not null)
        {
            startupLogger.LogWarning(
                "{ProbeError} WhisperNetCuda will remain selectable, but jobs will fail until the runtime is installed.",
                cudaProbeError);
        }

        var openVinoProbeError = openVinoProbe.GetAvailabilityError();
        if (openVinoProbeError is not null)
        {
            startupLogger.LogWarning(
                "{ProbeError} WhisperNetOpenVino will remain selectable, but jobs will fail until the runtime is installed.",
                openVinoProbeError);
        }

        var openVinoGenAiProbeError = openVinoGenAiProbe.GetAvailabilityError();
        if (openVinoGenAiProbeError is not null)
        {
            startupLogger.LogWarning(
                "{ProbeError} OpenVinoGenAi will remain registered, but jobs will fail until the runtime is installed.",
                openVinoGenAiProbeError);
        }
    }

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseCors();
    app.UseFrontendAppShellAssets();

    app.MapGet("/api/health", () => Results.Ok(new { status = "healthy" }))
        .WithName("HealthCheck")
        .WithTags("Health");

    app.MapFolderEndpoints();
    app.MapSettingsEndpoints();
    app.MapDiagnosticsEndpoints();
    app.MapProjectEndpoints();
    app.MapUploadEndpoints();
    app.MapQueueEndpoints();
    app.MapTranscriptEndpoints();
    app.MapMediaEndpoints();
    app.MapExportEndpoints();
    app.MapFrontendAppShellFallback();

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
