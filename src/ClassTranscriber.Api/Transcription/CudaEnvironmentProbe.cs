using System.Runtime.InteropServices;

namespace ClassTranscriber.Api.Transcription;

public interface ICudaEnvironmentProbe
{
    string? GetAvailabilityError();
}

public sealed class CudaEnvironmentProbe : ICudaEnvironmentProbe
{
    private static readonly string[] CandidateLibraries = OperatingSystem.IsWindows()
        ? ["nvcuda.dll", "cudart64_130.dll", "cudart64_12.dll"]
        : OperatingSystem.IsLinux()
            ? ["libcuda.so.1", "libcuda.so", "libcudart.so.13.0", "libcudart.so.12", "libcudart.so"]
            : [];

    public string? GetAvailabilityError()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
            return "WhisperNetCuda is only supported on Windows x64 and Linux x64 hosts.";

        foreach (var candidate in CandidateLibraries)
        {
            if (NativeLibrary.TryLoad(candidate, out var handle))
            {
                NativeLibrary.Free(handle);
                return null;
            }
        }

        return $"WhisperNetCuda cannot start on this host because none of the expected CUDA runtime libraries could be loaded ({string.Join(", ", CandidateLibraries)}). Install the NVIDIA driver/runtime and, in Docker, expose the GPU with the NVIDIA container runtime.";
    }
}
