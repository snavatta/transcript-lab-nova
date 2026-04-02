using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClassTranscriber.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Folders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TotalSizeBytes = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Folders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GlobalSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DefaultEngine = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    DefaultModel = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    DefaultLanguageMode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    DefaultLanguageCode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    DefaultAudioNormalizationEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    DefaultDiarizationEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    DefaultTranscriptViewMode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GlobalSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FolderId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    OriginalFileName = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    StoredFileName = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    MediaType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    FileExtension = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    MediaPath = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Progress = table.Column<int>(type: "INTEGER", nullable: false),
                    DurationMs = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    QueuedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FailedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    OriginalFileSizeBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    WorkspaceSizeBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    TotalSizeBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    Settings_Engine = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Settings_Model = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Settings_LanguageMode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Settings_LanguageCode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    Settings_AudioNormalizationEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Settings_DiarizationEnabled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Projects_Folders_FolderId",
                        column: x => x.FolderId,
                        principalTable: "Folders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Transcripts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlainText = table.Column<string>(type: "TEXT", nullable: false),
                    StructuredSegmentsJson = table.Column<string>(type: "TEXT", nullable: false),
                    DetectedLanguage = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    DurationMs = table.Column<long>(type: "INTEGER", nullable: true),
                    SegmentCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transcripts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Transcripts_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "GlobalSettings",
                columns: new[] { "Id", "DefaultAudioNormalizationEnabled", "DefaultDiarizationEnabled", "DefaultEngine", "DefaultLanguageCode", "DefaultLanguageMode", "DefaultModel", "DefaultTranscriptViewMode" },
                values: new object[] { 1, true, false, "Whisper", null, "Auto", "small", "Readable" });

            migrationBuilder.CreateIndex(
                name: "IX_Projects_FolderId",
                table: "Projects",
                column: "FolderId");

            migrationBuilder.CreateIndex(
                name: "IX_Transcripts_ProjectId",
                table: "Transcripts",
                column: "ProjectId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GlobalSettings");

            migrationBuilder.DropTable(
                name: "Transcripts");

            migrationBuilder.DropTable(
                name: "Projects");

            migrationBuilder.DropTable(
                name: "Folders");
        }
    }
}
