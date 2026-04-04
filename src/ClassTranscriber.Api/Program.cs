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
using ClassTranscriber.Api.Transcription.SpeechToText;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
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

    var sqliteConnectionString = SqliteConnectionStringResolver.Resolve(
        builder.Configuration.GetConnectionString("DefaultConnection")
        ?? builder.Configuration["ConnectionStrings:DefaultConnection"]
        ?? builder.Configuration["ConnectionStrings__DefaultConnection"]);

    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseSqlite(sqliteConnectionString));

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
    builder.Services.AddHttpClient(OpenVinoWhisperSidecarManager.HttpClientName, client =>
    {
        client.Timeout = Timeout.InfiniteTimeSpan;
    });
    builder.Services.AddHttpClient("OpenVinoWhisperSidecarModelDownloads", client =>
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
    builder.Services.Configure<OpenVinoWhisperSidecarOptions>(o =>
    {
        o.ModelsPath = builder.Configuration["Transcription:OpenVinoWhisperSidecar:ModelsPath"]
            ?? builder.Configuration["Transcription:OpenVinoGenAi:ModelsPath"]
            ?? Path.Combine(
                builder.Configuration["Storage:BasePath"] ?? "/data",
                builder.Configuration["Storage:ModelsPath"] ?? "models",
                "openvino-genai");
        if (bool.TryParse(
                builder.Configuration["Transcription:OpenVinoWhisperSidecar:AutoDownloadModels"]
                ?? builder.Configuration["Transcription:AutoDownloadModels"],
                out var autoDownload))
            o.AutoDownloadModels = autoDownload;
        if (bool.TryParse(
                builder.Configuration["Transcription:OpenVinoWhisperSidecar:LogSegments"]
                ?? builder.Configuration["Transcription:LogSegments"],
                out var logSegments))
            o.LogSegments = logSegments;
        o.ModelDownloadBaseUrl = builder.Configuration["Transcription:OpenVinoWhisperSidecar:ModelDownloadBaseUrl"]
            ?? builder.Configuration["Transcription:OpenVinoGenAi:ModelDownloadBaseUrl"]
            ?? o.ModelDownloadBaseUrl;
        o.PythonPath = builder.Configuration["Transcription:OpenVinoWhisperSidecar:PythonPath"]
            ?? builder.Configuration["Transcription:OpenVinoGenAi:PythonPath"]
            ?? o.PythonPath;
        o.ServerScriptPath = builder.Configuration["Transcription:OpenVinoWhisperSidecar:ServerScriptPath"] ?? o.ServerScriptPath;
        o.Device = builder.Configuration["Transcription:OpenVinoWhisperSidecar:Device"]
            ?? builder.Configuration["Transcription:OpenVinoGenAi:Device"]
            ?? o.Device;
        if (int.TryParse(builder.Configuration["Transcription:OpenVinoWhisperSidecar:Port"], out var port))
            o.Port = port;
        if (int.TryParse(builder.Configuration["Transcription:OpenVinoWhisperSidecar:StartupTimeoutSeconds"], out var timeout))
            o.StartupTimeoutSeconds = timeout;
    });
    builder.Services.AddSingleton<IRegisteredTranscriptionEngine, SherpaOnnxTranscriptionEngine>();
    builder.Services.AddSingleton<IRegisteredTranscriptionEngine, SherpaOnnxSenseVoiceTranscriptionEngine>();
    builder.Services.AddSingleton<IRegisteredTranscriptionEngine, WhisperNetCpuTranscriptionEngine>();
    builder.Services.AddSingleton<IRegisteredTranscriptionEngine, WhisperNetCudaTranscriptionEngine>();
    builder.Services.AddSingleton<IRegisteredTranscriptionEngine, OpenVinoGenAiTranscriptionEngine>();
    builder.Services.AddSingleton<IRegisteredTranscriptionEngine, OpenVinoWhisperSidecarTranscriptionEngine>();
    builder.Services.AddSingleton<IOpenVinoWhisperSidecarManager, OpenVinoWhisperSidecarManager>();
    builder.Services.AddSingleton<IOpenVinoWhisperSidecarEnvironmentProbe, OpenVinoWhisperSidecarEnvironmentProbe>();
    builder.Services.AddSingleton<IOpenVinoSidecarModelManager, OpenVinoSidecarModelManager>();
    builder.Services.AddKeyedSingleton<ISpeechToTextClient, OpenVinoSidecarSpeechToTextClient>("OpenVinoWhisperSidecar");
    builder.Services.AddSingleton<IRegisteredTranscriptionEngine, OnnxWhisperTranscriptionEngine>();
    builder.Services.Configure<OpenAiCompatibleOptions>(builder.Configuration.GetSection("Transcription:OpenAiCompatible"));
    builder.Services.AddSingleton<IRegisteredTranscriptionEngine, OpenAiCompatibleTranscriptionEngine>();
    builder.Services.AddKeyedSingleton<ISpeechToTextClient, OpenAiCompatibleSpeechToTextClient>("OpenAiCompatible");
    builder.Services.AddHttpClient(OpenAiCompatibleTranscriptionEngine.HttpClientName, (sp, client) =>
    {
        var opts = sp.GetRequiredService<IOptions<OpenAiCompatibleOptions>>().Value;
        if (!string.IsNullOrWhiteSpace(opts.BaseUrl))
            client.BaseAddress = new Uri(opts.BaseUrl.TrimEnd('/') + "/");
        client.Timeout = TimeSpan.FromSeconds(opts.TimeoutSeconds > 0 ? opts.TimeoutSeconds : 120);
        if (!string.IsNullOrWhiteSpace(opts.ApiKey))
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", opts.ApiKey);
    });
    builder.Services.AddSingleton<ICudaEnvironmentProbe, CudaEnvironmentProbe>();
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

        var openVinoGenAiProbe = scope.ServiceProvider.GetRequiredService<IOpenVinoGenAiEnvironmentProbe>();
        var openVinoWhisperSidecarProbe = scope.ServiceProvider.GetRequiredService<IOpenVinoWhisperSidecarEnvironmentProbe>();
        var cudaProbe = scope.ServiceProvider.GetRequiredService<ICudaEnvironmentProbe>();
        var startupLogger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
        var cudaProbeError = cudaProbe.GetAvailabilityError();
        if (cudaProbeError is not null)
        {
            startupLogger.LogWarning(
                "{ProbeError} WhisperNetCuda will remain selectable, but jobs will fail until the runtime is installed.",
                cudaProbeError);
        }

        var openVinoGenAiProbeError = openVinoGenAiProbe.GetAvailabilityError();
        if (openVinoGenAiProbeError is not null)
        {
            startupLogger.LogWarning(
                "{ProbeError} OpenVinoGenAi will remain registered, but jobs will fail until the runtime is installed.",
                openVinoGenAiProbeError);
        }

        var openVinoWhisperSidecarProbeError = openVinoWhisperSidecarProbe.GetAvailabilityError();
        if (openVinoWhisperSidecarProbeError is not null)
        {
            startupLogger.LogWarning(
                "{ProbeError} OpenVinoWhisperSidecar will remain selectable, but jobs will fail until the dependencies are installed.",
                openVinoWhisperSidecarProbeError);
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
