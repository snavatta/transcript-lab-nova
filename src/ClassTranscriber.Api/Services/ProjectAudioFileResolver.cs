using ClassTranscriber.Api.Domain;
using ClassTranscriber.Api.Storage;
using MediaType = ClassTranscriber.Api.Contracts.MediaType;

namespace ClassTranscriber.Api.Services;

public static class ProjectAudioFileResolver
{
    public static string GetExtractedAudioRelativePath(IFileStorage fileStorage, Project project)
        => Path.ChangeExtension(Path.Combine(fileStorage.GetAudioPath(), project.StoredFileName), ".wav");

    public static string GetNormalizedAudioRelativePath(IFileStorage fileStorage, Project project)
        => Path.Combine(fileStorage.GetAudioPath(), "norm_" + Path.GetFileName(GetExtractedAudioRelativePath(fileStorage, project)));

    public static string? TryGetAudioPreviewRelativePath(IFileStorage fileStorage, Project project)
    {
        if (project.MediaType != MediaType.Video)
            return null;

        var extractedRelativePath = GetExtractedAudioRelativePath(fileStorage, project);
        if (fileStorage.FileExists(extractedRelativePath))
            return extractedRelativePath;

        var normalizedRelativePath = GetNormalizedAudioRelativePath(fileStorage, project);
        return fileStorage.FileExists(normalizedRelativePath)
            ? normalizedRelativePath
            : null;
    }

    public static string[] GetExistingWorkspaceAudioRelativePaths(IFileStorage fileStorage, Project project)
    {
        var candidates = new[]
        {
            GetExtractedAudioRelativePath(fileStorage, project),
            GetNormalizedAudioRelativePath(fileStorage, project),
        };

        return candidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(fileStorage.FileExists)
            .ToArray();
    }
}
