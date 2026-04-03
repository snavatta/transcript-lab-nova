using ClassTranscriber.Api.Persistence;
using FluentAssertions;

namespace ClassTranscriber.Api.Tests;

public sealed class SqliteConnectionStringResolverTests
{
    [Fact]
    public void Resolve_ReturnsConfiguredConnectionString_WhenKeyValueFormatIsProvided()
    {
        var resolved = SqliteConnectionStringResolver.Resolve("Data Source=/data/transcriptlab.db");

        resolved.Should().Be("Data Source=/data/transcriptlab.db");
    }

    [Fact]
    public void Resolve_PromotesPlainFilePath_ToSqliteConnectionString()
    {
        var resolved = SqliteConnectionStringResolver.Resolve("/data/transcriptlab.db");

        resolved.Should().Be("Data Source=/data/transcriptlab.db");
    }

    [Fact]
    public void Resolve_ThrowsForMissingValue()
    {
        var act = () => SqliteConnectionStringResolver.Resolve("   ");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*DefaultConnection*");
    }
}
