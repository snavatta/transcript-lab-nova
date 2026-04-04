using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClassTranscriber.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDiarizationMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DefaultDiarizationMode",
                table: "GlobalSettings",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "Basic");

            migrationBuilder.AddColumn<string>(
                name: "Settings_DiarizationMode",
                table: "Projects",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "Basic");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DefaultDiarizationMode",
                table: "GlobalSettings");

            migrationBuilder.DropColumn(
                name: "Settings_DiarizationMode",
                table: "Projects");
        }
    }
}
