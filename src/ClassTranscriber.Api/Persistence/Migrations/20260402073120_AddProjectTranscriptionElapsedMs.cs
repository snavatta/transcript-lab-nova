using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClassTranscriber.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectTranscriptionElapsedMs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "TranscriptionElapsedMs",
                table: "Projects",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TranscriptionElapsedMs",
                table: "Projects");
        }
    }
}
