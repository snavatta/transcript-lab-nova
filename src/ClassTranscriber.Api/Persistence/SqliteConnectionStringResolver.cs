namespace ClassTranscriber.Api.Persistence;

public static class SqliteConnectionStringResolver
{
    public static string Resolve(string? configuredValue)
    {
        var trimmed = configuredValue?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");

        if (trimmed.Contains('='))
            return trimmed;

        return $"Data Source={trimmed}";
    }
}
