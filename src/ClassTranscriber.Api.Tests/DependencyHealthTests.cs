using System.Diagnostics;
using FluentAssertions;

namespace ClassTranscriber.Api.Tests;

/// <summary>
/// Verifies that external dependencies required by the transcription pipeline
/// are present and functional on the host system.
/// </summary>
public class DependencyHealthTests
{
    [Fact]
    public void Ffmpeg_IsAvailable()
    {
        var result = RunCommand("ffmpeg", "-version");
        result.ExitCode.Should().Be(0, "ffmpeg must be installed and on PATH");
        result.StdOut.Should().Contain("ffmpeg version");
    }

    [Fact]
    public void Ffprobe_IsAvailable()
    {
        var result = RunCommand("ffprobe", "-version");
        result.ExitCode.Should().Be(0, "ffprobe must be installed and on PATH");
        result.StdOut.Should().Contain("ffprobe version");
    }

    [Fact]
    public void WhisperCli_IsAvailable()
    {
        var result = RunCommand("whisper-cli", "--help");
        // whisper-cli may return 0 or non-zero for --help, but should at least start
        result.Started.Should().BeTrue("whisper-cli must be installed and on PATH");
    }

    [Fact]
    public void WhisperModel_SmallExists()
    {
        // Check the default model location relative to the API project
        var apiDir = FindApiProjectDirectory();
        var modelPath = Path.Combine(apiDir, "data", "models", "ggml-small.bin");
        File.Exists(modelPath).Should().BeTrue(
            $"Whisper model file should exist at {modelPath}. " +
            "Download it from https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.bin");
    }

    private static string FindApiProjectDirectory()
    {
        var dir = AppContext.BaseDirectory;
        // Walk up from bin/Debug/net10.0 to find the solution root
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, "src", "ClassTranscriber.Api");
            if (Directory.Exists(candidate))
                return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        // Fallback: try relative from working directory
        var cwd = Directory.GetCurrentDirectory();
        while (cwd is not null)
        {
            var candidate = Path.Combine(cwd, "src", "ClassTranscriber.Api");
            if (Directory.Exists(candidate))
                return candidate;
            cwd = Path.GetDirectoryName(cwd);
        }
        throw new DirectoryNotFoundException("Could not find ClassTranscriber.Api project directory");
    }

    private static CommandResult RunCommand(string command, string args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = command,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);
            if (process is null)
                return new CommandResult(false, -1, "", "Process.Start returned null");

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(TimeSpan.FromSeconds(10));

            return new CommandResult(true, process.ExitCode, stdout, stderr);
        }
        catch (Exception ex)
        {
            return new CommandResult(false, -1, "", ex.Message);
        }
    }

    private record CommandResult(bool Started, int ExitCode, string StdOut, string StdErr);
}
