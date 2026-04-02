namespace ClassTranscriber.Api.Transcription;

internal static class ProcessPathResolver
{
    public static string ResolveWorkerPath(string path)
    {
        var resolved = ResolveFilePath(path);
        if (File.Exists(resolved))
            return resolved;

        if (string.Equals(Path.GetExtension(resolved), ".dll", StringComparison.OrdinalIgnoreCase))
        {
            var withoutExtension = Path.ChangeExtension(resolved, null);
            if (File.Exists(withoutExtension))
                return withoutExtension;
        }
        else if (Path.GetExtension(resolved).Length == 0)
        {
            var withDll = $"{resolved}.dll";
            if (File.Exists(withDll))
                return withDll;
        }

        return resolved;
    }

    public static string ResolveFilePath(string path)
        => Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));

    public static bool FileExists(string path)
        => File.Exists(ResolveWorkerPath(path));

    public static bool ExecutableExists(string executableOrPath)
    {
        if (string.IsNullOrWhiteSpace(executableOrPath))
            return false;

        if (Path.IsPathRooted(executableOrPath) || executableOrPath.Contains(Path.DirectorySeparatorChar) || executableOrPath.Contains(Path.AltDirectorySeparatorChar))
            return File.Exists(ResolveFilePath(executableOrPath));

        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
            return false;

        var suffixes = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT")?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [".exe", ".cmd", ".bat"])
            : [string.Empty];

        foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var suffix in suffixes)
            {
                var candidate = Path.Combine(directory, executableOrPath + suffix);
                if (File.Exists(candidate))
                    return true;
            }
        }

        return false;
    }
}
