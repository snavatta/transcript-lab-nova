using System.Runtime.InteropServices;

namespace ClassTranscriber.Api.Transcription;

public interface IOpenVinoEnvironmentProbe
{
    string? GetAvailabilityError();
}

public sealed class OpenVinoEnvironmentProbe : IOpenVinoEnvironmentProbe
{
    private static readonly string[] CandidateLibraries = OperatingSystem.IsWindows()
        ? ["openvino.dll", "openvino_c.dll"]
        : OperatingSystem.IsMacOS()
            ? ["libopenvino.dylib", "openvino"]
            : ["libopenvino.so", "libopenvino.so.2500", "openvino"];

    public string? GetAvailabilityError()
    {
        foreach (var candidate in CandidateLibraries)
        {
            if (NativeLibrary.TryLoad(candidate, out var handle))
            {
                NativeLibrary.Free(handle);
                return null;
            }
        }

        return $"WhisperNetOpenVino cannot start on this host because none of the expected OpenVINO runtime libraries could be loaded ({string.Join(", ", CandidateLibraries)}). Install the OpenVINO runtime/toolkit and ensure its libraries are on the system loader path.";
    }
}
