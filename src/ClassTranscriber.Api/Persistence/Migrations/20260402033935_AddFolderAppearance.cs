using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClassTranscriber.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFolderAppearance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ColorHex",
                table: "Folders",
                type: "TEXT",
                maxLength: 7,
                nullable: false,
                defaultValue: "#546E7A");

            migrationBuilder.AddColumn<string>(
                name: "IconKey",
                table: "Folders",
                type: "TEXT",
                maxLength: 40,
                nullable: false,
                defaultValue: "Folder");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ColorHex",
                table: "Folders");

            migrationBuilder.DropColumn(
                name: "IconKey",
                table: "Folders");
        }
    }
}
