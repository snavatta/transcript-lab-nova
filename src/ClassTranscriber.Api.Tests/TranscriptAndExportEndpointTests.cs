using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ClassTranscriber.Api.Contracts;
using ClassTranscriber.Api.Domain;
using ClassTranscriber.Api.Persistence;
using ClassTranscriber.Api.Services;
using ClassTranscriber.Api.Storage;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace ClassTranscriber.Api.Tests;

public class TranscriptAndExportEndpointTests : IAsyncLifetime
{
    private TestWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        _factory = new TestWebApplicationFactory();
        _client = _factory.Client;
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task GetTranscript_ReturnsConflict_WhenTranscriptIsUnavailable()
    {
        var projectId = await SeedProjectAsync();

        var response = await _client.GetAsync($"/api/projects/{projectId}/transcript");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task GetTranscript_ReturnsTranscript_WhenAvailable()
    {
        var projectId = await SeedProjectAsync(includeTranscript: true);

        var response = await _client.GetAsync($"/api/projects/{projectId}/transcript");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var transcript = await response.Content.ReadFromJsonAsync<TranscriptDto>();
        transcript.Should().NotBeNull();
        transcript!.ProjectId.Should().Be(projectId.ToString());
        transcript.SegmentCount.Should().Be(1);
        transcript.Segments.Should().ContainSingle(segment => segment.Text == "Transcript segment");
    }

    [Fact]
    public async Task ExportPdf_ReturnsActualPdfBytes()
    {
        var projectId = await SeedProjectAsync(includeTranscript: true);

        var response = await _client.GetAsync($"/api/projects/{projectId}/export?format=pdf");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");

        var content = await response.Content.ReadAsByteArrayAsync();
        Encoding.ASCII.GetString(content.Take(8).ToArray()).Should().StartWith("%PDF-");
    }

    [Fact]
    public async Task Export_ReturnsConflict_WhenTranscriptIsUnavailable()
    {
        var projectId = await SeedProjectAsync();

        var response = await _client.GetAsync($"/api/projects/{projectId}/export?format=pdf");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task GetProject_ReturnsAudioPreviewUrl_WhenDerivedAudioExistsForVideo()
    {
        var projectId = await SeedProjectAsync(
            mediaType: MediaType.Video,
            originalFileName: "lecture-01.mkv",
            storedFileName: "lecture-01.mkv",
            fileExtension: ".mkv",
            includeDerivedAudio: true);

        var response = await _client.GetAsync($"/api/projects/{projectId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var project = await response.Content.ReadFromJsonAsync<JsonElement>();
        project.GetProperty("audioPreviewUrl").GetString().Should().Be($"/api/projects/{projectId}/audio");
    }

    [Fact]
    public async Task GetProjectAudioPreview_ReturnsExtractedAudio_WhenAvailable()
    {
        var projectId = await SeedProjectAsync(
            mediaType: MediaType.Video,
            originalFileName: "lecture-01.mkv",
            storedFileName: "lecture-01.mkv",
            fileExtension: ".mkv",
            includeDerivedAudio: true);

        var response = await _client.GetAsync($"/api/projects/{projectId}/audio");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("audio/wav");

        var content = await response.Content.ReadAsByteArrayAsync();
        content.Should().NotBeEmpty();
    }

    private async Task<Guid> SeedProjectAsync(
        bool includeTranscript = false,
        MediaType mediaType = MediaType.Audio,
        string originalFileName = "lecture-01.wav",
        string storedFileName = "lecture-01.wav",
        string fileExtension = ".wav",
        bool includeDerivedAudio = false)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var fileStorage = scope.ServiceProvider.GetRequiredService<IFileStorage>();

        var folder = new Folder
        {
            Id = Guid.NewGuid(),
            Name = "Biology",
            IconKey = FolderAppearance.DefaultIconKey,
            ColorHex = FolderAppearance.DefaultColorHex,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };

        var project = new Project
        {
            Id = Guid.NewGuid(),
            FolderId = folder.Id,
            Folder = folder,
            Name = "Lecture 01",
            OriginalFileName = originalFileName,
            StoredFileName = storedFileName,
            FileExtension = fileExtension,
            MediaPath = $"uploads/{storedFileName}",
            MediaType = mediaType,
            Status = includeTranscript ? ProjectStatus.Completed : ProjectStatus.Draft,
            Progress = includeTranscript ? 100 : 0,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            OriginalFileSizeBytes = 1024,
            TotalSizeBytes = 1024,
            Settings = new ProjectSettings
            {
                Engine = "WhisperNet",
                Model = "small",
                LanguageMode = "Auto",
                AudioNormalizationEnabled = true,
                DiarizationEnabled = false,
            },
        };

        db.Folders.Add(folder);
        db.Projects.Add(project);

        await using (var mediaStream = new MemoryStream(TranscriptionPipelineTests.CreateMinimalWavBytesForTests()))
        {
            await fileStorage.SaveFileAsync(project.MediaPath, mediaStream);
        }

        if (includeDerivedAudio)
        {
            var derivedAudioRelativePath = ProjectAudioFileResolver.GetExtractedAudioRelativePath(fileStorage, project);
            await using var audioStream = new MemoryStream(TranscriptionPipelineTests.CreateMinimalWavBytesForTests());
            await fileStorage.SaveFileAsync(derivedAudioRelativePath, audioStream);
        }

        if (includeTranscript)
        {
            db.Transcripts.Add(new Transcript
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                PlainText = "Transcript segment",
                StructuredSegmentsJson = JsonSerializer.Serialize(new[]
                {
                    new ClassTranscriber.Api.Contracts.TranscriptSegmentDto
                    {
                        StartMs = 0,
                        EndMs = 1000,
                        Text = "Transcript segment",
                        Speaker = null,
                    },
                }),
                DetectedLanguage = "en",
                DurationMs = 1000,
                SegmentCount = 1,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
            });
        }

        await db.SaveChangesAsync();
        return project.Id;
    }
}
