using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClassTranscriber.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDiarizationSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Settings_DiarizationSource",
                table: "Projects",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "Local");

            migrationBuilder.AddColumn<string>(
                name: "DefaultDiarizationSource",
                table: "GlobalSettings",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "Local");

            migrationBuilder.UpdateData(
                table: "GlobalSettings",
                keyColumn: "Id",
                keyValue: 1,
                column: "DefaultDiarizationSource",
                value: "Local");

            migrationBuilder.Sql(
                "UPDATE Projects SET Settings_DiarizationSource = 'Provider' WHERE Settings_Engine = 'Xai';");

            migrationBuilder.Sql(
                "UPDATE GlobalSettings SET DefaultDiarizationSource = 'Provider' WHERE DefaultEngine = 'Xai';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Settings_DiarizationSource",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "DefaultDiarizationSource",
                table: "GlobalSettings");
        }
    }
}
