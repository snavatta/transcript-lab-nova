using ClassTranscriber.Api.Contracts;
using ClassTranscriber.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClassTranscriber.Api.Services;

public class SettingsService : ISettingsService
{
    private readonly AppDbContext _db;
    private readonly ILogger<SettingsService> _logger;

    public SettingsService(AppDbContext db, ILogger<SettingsService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<GlobalSettingsDto> GetAsync(CancellationToken ct = default)
    {
        var settings = await _db.GlobalSettings.SingleAsync(ct);
        return MapToDto(settings);
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
        settings.DefaultTranscriptViewMode = request.DefaultTranscriptViewMode;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Updated global settings");
        return MapToDto(settings);
    }

    private static GlobalSettingsDto MapToDto(Domain.GlobalSettings settings) => new()
    {
        DefaultEngine = settings.DefaultEngine,
        DefaultModel = settings.DefaultModel,
        DefaultLanguageMode = settings.DefaultLanguageMode,
        DefaultLanguageCode = settings.DefaultLanguageCode,
        DefaultAudioNormalizationEnabled = settings.DefaultAudioNormalizationEnabled,
        DefaultDiarizationEnabled = settings.DefaultDiarizationEnabled,
        DefaultTranscriptViewMode = settings.DefaultTranscriptViewMode,
    };
}
