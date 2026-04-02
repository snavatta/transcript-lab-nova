using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClassTranscriber.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectDebugTimings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "AudioExtractionElapsedMs",
                table: "Projects",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "AudioNormalizationElapsedMs",
                table: "Projects",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "MediaInspectionElapsedMs",
                table: "Projects",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ResultPersistenceElapsedMs",
                table: "Projects",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "TotalProcessingElapsedMs",
                table: "Projects",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AudioExtractionElapsedMs",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "AudioNormalizationElapsedMs",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "MediaInspectionElapsedMs",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "ResultPersistenceElapsedMs",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "TotalProcessingElapsedMs",
                table: "Projects");
        }
    }
}
