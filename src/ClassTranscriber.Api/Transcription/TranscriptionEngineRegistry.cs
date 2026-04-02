namespace ClassTranscriber.Api.Transcription;

public sealed class TranscriptionEngineRegistry : ITranscriptionEngineRegistry
{
    private readonly Dictionary<string, IRegisteredTranscriptionEngine> _engines;

    public TranscriptionEngineRegistry(IEnumerable<IRegisteredTranscriptionEngine> engines)
    {
        _engines = engines.ToDictionary(engine => engine.EngineId, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<string> GetSupportedEngines()
        => _engines.Values
            .Where(engine => engine.GetAvailabilityError() is null)
            .Select(engine => engine.EngineId)
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public IReadOnlyCollection<string> GetSupportedModels(string engineId)
        => _engines.TryGetValue(engineId, out var engine)
            && engine.GetAvailabilityError() is null
            ? engine.SupportedModels.OrderBy(model => model, StringComparer.OrdinalIgnoreCase).ToArray()
            : [];

    public bool IsSupportedEngine(string engineId)
        => !string.IsNullOrWhiteSpace(engineId)
            && _engines.TryGetValue(engineId, out var engine)
            && engine.GetAvailabilityError() is null;

    public bool IsSupportedModel(string engineId, string model)
        => _engines.TryGetValue(engineId, out var engine)
            && engine.GetAvailabilityError() is null
            && engine.SupportedModels.Contains(model, StringComparer.OrdinalIgnoreCase);

    public ITranscriptionEngine Resolve(string engineId)
    {
        if (_engines.TryGetValue(engineId, out var engine))
        {
            var availabilityError = engine.GetAvailabilityError();
            if (availabilityError is not null)
                throw new InvalidOperationException(availabilityError);

            return engine;
        }

        var supported = string.Join(", ", GetSupportedEngines());
        throw new InvalidOperationException($"Unsupported transcription engine '{engineId}'. Supported engines: {supported}.");
    }
}
