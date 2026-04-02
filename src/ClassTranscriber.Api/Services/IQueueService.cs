using ClassTranscriber.Api.Contracts;

namespace ClassTranscriber.Api.Services;

public interface IQueueService
{
    Task<QueueOverviewDto> GetOverviewAsync(CancellationToken ct = default);
}
