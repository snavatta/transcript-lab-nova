namespace ClassTranscriber.Api.Jobs;

public interface IActiveJobCancellation
{
    bool TryCancel(Guid projectId);
}
