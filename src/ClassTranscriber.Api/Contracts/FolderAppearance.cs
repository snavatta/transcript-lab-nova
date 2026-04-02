namespace ClassTranscriber.Api.Contracts;

public static class FolderAppearance
{
    public const string DefaultIconKey = "Folder";
    public const string DefaultColorHex = "#546E7A";

    public static bool TryResolveIconKey(string? iconKey, out string normalizedIconKey)
    {
        if (string.IsNullOrWhiteSpace(iconKey))
        {
            normalizedIconKey = DefaultIconKey;
            return true;
        }

        var trimmed = iconKey.Trim();
        if (!char.IsAsciiLetterUpper(trimmed[0]))
        {
            normalizedIconKey = string.Empty;
            return false;
        }

        for (var index = 1; index < trimmed.Length; index += 1)
        {
            if (!char.IsAsciiLetterOrDigit(trimmed[index]))
            {
                normalizedIconKey = string.Empty;
                return false;
            }
        }

        normalizedIconKey = trimmed;
        return true;
    }

    public static string ResolveIconKeyOrDefault(string? iconKey)
    {
        return TryResolveIconKey(iconKey, out var normalizedIconKey)
            ? normalizedIconKey
            : DefaultIconKey;
    }

    public static bool TryResolveColorHex(string? colorHex, out string normalizedColorHex)
    {
        if (string.IsNullOrWhiteSpace(colorHex))
        {
            normalizedColorHex = DefaultColorHex;
            return true;
        }

        var trimmed = colorHex.Trim();
        if (trimmed.Length != 7 || trimmed[0] != '#')
        {
            normalizedColorHex = string.Empty;
            return false;
        }

        for (var index = 1; index < trimmed.Length; index += 1)
        {
            if (!Uri.IsHexDigit(trimmed[index]))
            {
                normalizedColorHex = string.Empty;
                return false;
            }
        }

        normalizedColorHex = trimmed.ToUpperInvariant();
        return true;
    }

    public static string ResolveColorHexOrDefault(string? colorHex)
    {
        return TryResolveColorHex(colorHex, out var normalizedColorHex)
            ? normalizedColorHex
            : DefaultColorHex;
    }
}
