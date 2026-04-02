using System.Net;
using System.Net.Http.Json;
using ClassTranscriber.Api.Contracts;
using FluentAssertions;

namespace ClassTranscriber.Api.Tests;

public class FolderEndpointTests : IAsyncLifetime
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
    public async Task ListFolders_ReturnsEmpty_WhenNoFolders()
    {
        var response = await _client.GetAsync("/api/folders");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var folders = await response.Content.ReadFromJsonAsync<FolderSummaryDto[]>();
        folders.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateFolder_ReturnsCreatedFolder()
    {
        var response = await _client.PostAsJsonAsync("/api/folders", new
        {
            Name = "Biology",
            IconKey = "TravelExploreOutlined",
            ColorHex = "#2e7d32",
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var folder = await response.Content.ReadFromJsonAsync<FolderDetailDto>();
        folder.Should().NotBeNull();
        folder!.Name.Should().Be("Biology");
        folder.IconKey.Should().Be("TravelExploreOutlined");
        folder.ColorHex.Should().Be("#2E7D32");
        folder.Id.Should().NotBeEmpty();
    }

    [Fact]
    public async Task CreateFolder_AppliesDefaultAppearance_WhenOmitted()
    {
        var response = await _client.PostAsJsonAsync("/api/folders", new { Name = "Defaulted" });
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var folder = await response.Content.ReadFromJsonAsync<FolderDetailDto>();
        folder.Should().NotBeNull();
        folder!.IconKey.Should().Be(FolderAppearance.DefaultIconKey);
        folder.ColorHex.Should().Be(FolderAppearance.DefaultColorHex);
    }

    [Fact]
    public async Task CreateFolder_ReturnsBadRequest_WhenNameEmpty()
    {
        var response = await _client.PostAsJsonAsync("/api/folders", new { Name = "" });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateFolder_ReturnsBadRequest_WhenNameTooLong()
    {
        var longName = new string('A', 121);
        var response = await _client.PostAsJsonAsync("/api/folders", new { Name = longName });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetFolder_ReturnsFolder_AfterCreation()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/folders", new
        {
            Name = "Math",
            IconKey = "MenuBook",
            ColorHex = "#1565C0",
        });
        var created = await createResponse.Content.ReadFromJsonAsync<FolderDetailDto>();

        var response = await _client.GetAsync($"/api/folders/{created!.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var folder = await response.Content.ReadFromJsonAsync<FolderDetailDto>();
        folder!.Name.Should().Be("Math");
        folder.IconKey.Should().Be("MenuBook");
        folder.ColorHex.Should().Be("#1565C0");
    }

    [Fact]
    public async Task GetFolder_ReturnsNotFound_WhenNonexistent()
    {
        var response = await _client.GetAsync($"/api/folders/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdateFolder_RenamesFolder()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/folders", new { Name = "Physics" });
        var created = await createResponse.Content.ReadFromJsonAsync<FolderDetailDto>();

        var response = await _client.PutAsJsonAsync($"/api/folders/{created!.Id}", new
        {
            Name = "Chemistry",
            IconKey = "Biotech",
            ColorHex = "#8E24AA",
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await response.Content.ReadFromJsonAsync<FolderSummaryDto>();
        updated!.Name.Should().Be("Chemistry");
        updated.IconKey.Should().Be("Biotech");
        updated.ColorHex.Should().Be("#8E24AA");
    }

    [Fact]
    public async Task CreateFolder_ReturnsBadRequest_WhenIconInvalid()
    {
        var response = await _client.PostAsJsonAsync("/api/folders", new
        {
            Name = "Biology",
            IconKey = "invalid icon!",
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateFolder_ReturnsBadRequest_WhenColorInvalid()
    {
        var response = await _client.PostAsJsonAsync("/api/folders", new
        {
            Name = "Biology",
            ColorHex = "blue",
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DeleteFolder_RemovesFolder()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/folders", new { Name = "English" });
        var created = await createResponse.Content.ReadFromJsonAsync<FolderDetailDto>();

        var deleteResponse = await _client.DeleteAsync($"/api/folders/{created!.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getResponse = await _client.GetAsync($"/api/folders/{created.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
