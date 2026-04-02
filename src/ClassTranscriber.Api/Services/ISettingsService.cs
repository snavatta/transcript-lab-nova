using ClassTranscriber.Api.Contracts;

namespace ClassTranscriber.Api.Services;

public interface ISettingsService
{
    Task<GlobalSettingsDto> GetAsync(CancellationToken ct = default);
    Task<GlobalSettingsDto> UpdateAsync(UpdateGlobalSettingsRequest request, CancellationToken ct = default);
}
