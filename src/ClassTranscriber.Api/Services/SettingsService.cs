using ClassTranscriber.Api.Contracts;
using ClassTranscriber.Api.Persistence;
using ClassTranscriber.Api.Transcription;
using Microsoft.EntityFrameworkCore;

namespace ClassTranscriber.Api.Services;

public class SettingsService : ISettingsService
{
    private readonly AppDbContext _db;
    private readonly ITranscriptionEngineRegistry _engineRegistry;
    private readonly ILogger<SettingsService> _logger;

    public SettingsService(AppDbContext db, ITranscriptionEngineRegistry engineRegistry, ILogger<SettingsService> logger)
    {
        _db = db;
        _engineRegistry = engineRegistry;
        _logger = logger;
    }

    public async Task<GlobalSettingsDto> GetAsync(CancellationToken ct = default)
    {
        var settings = await _db.GlobalSettings.SingleAsync(ct);
        return MapToDto(settings, _engineRegistry);
    }

    public async Task<GlobalSettingsDto> UpdateAsync(UpdateGlobalSettingsRequest request, CancellationToken ct = default)
    {
        var settings = await _db.GlobalSettings.SingleAsync(ct);

        settings.DefaultEngine = request.DefaultEngine;
        settings.DefaultModel = request.DefaultModel;
        settings.DefaultLanguageMode = request.DefaultLanguageMode;
        settings.DefaultLanguageCode = request.DefaultLanguageCode;
        settings.DefaultAudioNormalizationEnabled = request.DefaultAudioNormalizationEnabled;
        settings.DefaultDiarizationEnabled = request.DefaultDiarizationEnabled;
        settings.DefaultDiarizationMode = request.DefaultDiarizationMode;
        settings.DefaultTranscriptViewMode = request.DefaultTranscriptViewMode;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Updated global settings");
        return MapToDto(settings, _engineRegistry);
    }

    private static GlobalSettingsDto MapToDto(Domain.GlobalSettings settings, ITranscriptionEngineRegistry engineRegistry)
    {
        var engine = TranscriptionSettingsDefaults.ResolveSupportedEngine(engineRegistry, settings.DefaultEngine);
        var model = TranscriptionSettingsDefaults.ResolveSupportedModel(engineRegistry, engine, settings.DefaultModel);
        var (languageMode, languageCode) = TranscriptionSettingsDefaults.ResolveSupportedLanguage(
            engine,
            settings.DefaultLanguageMode,
            settings.DefaultLanguageCode);

        return new GlobalSettingsDto
        {
            DefaultEngine = engine,
            DefaultModel = model,
            DefaultLanguageMode = languageMode,
            DefaultLanguageCode = languageCode,
            DefaultAudioNormalizationEnabled = settings.DefaultAudioNormalizationEnabled,
            DefaultDiarizationEnabled = settings.DefaultDiarizationEnabled,
            DefaultDiarizationMode = settings.DefaultDiarizationMode,
            DefaultTranscriptViewMode = settings.DefaultTranscriptViewMode,
        };
    }
}
