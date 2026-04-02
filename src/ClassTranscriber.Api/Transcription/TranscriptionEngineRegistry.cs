namespace ClassTranscriber.Api.Transcription;

public sealed class TranscriptionEngineRegistry : ITranscriptionEngineRegistry
{
    private readonly Dictionary<string, IRegisteredTranscriptionEngine> _engines;

    public TranscriptionEngineRegistry(IEnumerable<IRegisteredTranscriptionEngine> engines)
    {
        _engines = engines.ToDictionary(engine => engine.EngineId, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<string> GetSupportedEngines()
        => _engines.Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase).ToArray();

    public IReadOnlyCollection<string> GetSupportedModels(string engineId)
        => _engines.TryGetValue(engineId, out var engine)
            ? engine.SupportedModels.OrderBy(model => model, StringComparer.OrdinalIgnoreCase).ToArray()
            : [];

    public bool IsSupportedEngine(string engineId)
        => !string.IsNullOrWhiteSpace(engineId) && _engines.ContainsKey(engineId);

    public bool IsSupportedModel(string engineId, string model)
        => _engines.TryGetValue(engineId, out var engine)
            && engine.SupportedModels.Contains(model, StringComparer.OrdinalIgnoreCase);

    public ITranscriptionEngine Resolve(string engineId)
    {
        if (_engines.TryGetValue(engineId, out var engine))
            return engine;

        var supported = string.Join(", ", GetSupportedEngines());
        throw new InvalidOperationException($"Unsupported transcription engine '{engineId}'. Supported engines: {supported}.");
    }
}