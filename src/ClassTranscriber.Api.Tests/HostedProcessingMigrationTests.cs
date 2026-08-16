using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ClassTranscriber.Api.Tests;

public sealed class HostedProcessingMigrationTests
{
    private const string PreviousMigration = "20260409000000_AddDiarizationMode";

    [Fact]
    public async Task Migration_PreservesLegacyXaiProviderRows_AndTranscriptValues()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"transcriptlab-migration-{Guid.NewGuid():N}.db");

        try
        {
            await using var db = CreateContext(databasePath);
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync(PreviousMigration);

            var folderId = Guid.NewGuid();
            var projectId = Guid.NewGuid();
            var transcriptId = Guid.NewGuid();
            var now = DateTime.UtcNow.ToString("O");

            await db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO Folders (Id, Name, IconKey, ColorHex, CreatedAtUtc, UpdatedAtUtc, TotalSizeBytes) VALUES ({folderId}, {"Migration"}, {"Folder"}, {"#546E7A"}, {now}, {now}, {0})");
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO Projects (
                    Id, FolderId, Name, OriginalFileName, StoredFileName, MediaType, FileExtension, MediaPath,
                    Status, Progress, CreatedAtUtc, UpdatedAtUtc, Settings_Engine, Settings_Model,
                    Settings_LanguageMode, Settings_AudioNormalizationEnabled, Settings_DiarizationEnabled,
                    Settings_DiarizationMode)
                VALUES (
                    {projectId}, {folderId}, {"Legacy xAI"}, {"legacy.wav"}, {"legacy.wav"}, {"Audio"}, {".wav"},
                    {"uploads/legacy.wav"}, {"Completed"}, {100}, {now}, {now}, {"Xai"}, {"grok-stt-1.0"},
                    {"Auto"}, {false}, {true}, {"Basic"})
                """);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO Transcripts (
                    Id, ProjectId, PlainText, StructuredSegmentsJson, DetectedLanguage, DurationMs,
                    SegmentCount, CreatedAtUtc, UpdatedAtUtc)
                VALUES ({transcriptId}, {projectId}, {"legacy text"}, {"[]"}, {"en"}, {1234}, {0}, {now}, {now})
                """);
            await db.Database.ExecuteSqlRawAsync("UPDATE GlobalSettings SET DefaultEngine = 'Xai'");

            await migrator.MigrateAsync();

            (await ScalarAsync<string>(db, "SELECT Settings_DiarizationSource FROM Projects"))
                .Should().Be("Provider");
            (await ScalarAsync<string>(db, "SELECT DefaultDiarizationSource FROM GlobalSettings WHERE Id = 1"))
                .Should().Be("Provider");
            (await ScalarAsync<string>(db, "SELECT PlainText FROM Transcripts"))
                .Should().Be("legacy text");
            (await ScalarAsync<long>(db, "SELECT DurationMs FROM Transcripts"))
                .Should().Be(1234);

            var hostedColumns = await GetColumnNamesAsync(db, "Transcripts");
            hostedColumns.Should().Contain([
                "DiarizationSource",
                "HostedDiarizationProvider",
                "HostedDiarizationModel",
                "HostedDiarizationRequestCount",
                "HostedDiarizationCostMicroUsd",
                "HostedDiarizationRateMicroUsdPerHour",
                "HostedDiarizationCostClassification",
            ]);
            (await GetRequiredColumnNamesAsync(db, "Transcripts")).Should().NotContain([
                "DiarizationSource",
                "HostedDiarizationProvider",
                "HostedDiarizationModel",
                "HostedDiarizationRequestCount",
                "HostedDiarizationCostMicroUsd",
                "HostedDiarizationRateMicroUsdPerHour",
                "HostedDiarizationCostClassification",
            ]);

            foreach (var column in hostedColumns.Where(column => column is "DiarizationSource"
                or "HostedDiarizationProvider"
                or "HostedDiarizationModel"
                or "HostedDiarizationRequestCount"
                or "HostedDiarizationCostMicroUsd"
                or "HostedDiarizationRateMicroUsdPerHour"
                or "HostedDiarizationCostClassification"))
            {
                (await ScalarAsync<object?>(db, $"SELECT {column} FROM Transcripts"))
                    .Should().BeNull($"the migration must not infer historical {column} values");
            }

            await migrator.MigrateAsync(PreviousMigration);

            (await ScalarAsync<string>(db, "SELECT PlainText FROM Transcripts"))
                .Should().Be("legacy text");
            (await GetColumnNamesAsync(db, "Transcripts")).Should().NotContain("HostedDiarizationProvider");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath))
                File.Delete(databasePath);
        }
    }

    private static Persistence.AppDbContext CreateContext(string databasePath)
    {
        var options = new DbContextOptionsBuilder<Persistence.AppDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;
        return new Persistence.AppDbContext(options);
    }

    private static async Task<T?> ScalarAsync<T>(Persistence.AppDbContext db, string sql, Guid? id = null)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        if (id is not null)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = "$id";
            parameter.Value = id.Value.ToString();
            command.Parameters.Add(parameter);
        }

        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? default : (T)Convert.ChangeType(value, typeof(T));
    }

    private static async Task<string[]> GetColumnNamesAsync(Persistence.AppDbContext db, string table)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table})";
        await using var reader = await command.ExecuteReaderAsync();
        var columns = new List<string>();
        while (await reader.ReadAsync())
            columns.Add(reader.GetString(1));
        return columns.ToArray();
    }

    private static async Task<string[]> GetRequiredColumnNamesAsync(Persistence.AppDbContext db, string table)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table})";
        await using var reader = await command.ExecuteReaderAsync();
        var columns = new List<string>();
        while (await reader.ReadAsync())
        {
            if (reader.GetInt32(3) == 1)
                columns.Add(reader.GetString(1));
        }
        return columns.ToArray();
    }
}
