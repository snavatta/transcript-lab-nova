namespace ClassTranscriber.Api.Tests;

public sealed class FrontendAppShellTests : IAsyncLifetime
{
    private TestWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        _factory = new TestWebApplicationFactory(includeFrontendAppShell: true);
        _client = _factory.Client;
        return Task.CompletedTask;
    }

    [Fact]
    public async Task GetRoot_ReturnsFrontendIndex()
    {
        var response = await _client.GetAsync("/");

        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("TranscriptLab Nova", html);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GetClientRoute_ReturnsFrontendIndex()
    {
        var response = await _client.GetAsync("/queue");

        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("TranscriptLab Nova", html);
    }

    [Fact]
    public async Task UnknownApiRoute_DoesNotFallbackToFrontend()
    {
        var response = await _client.GetAsync("/api/does-not-exist");

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
    }
}
