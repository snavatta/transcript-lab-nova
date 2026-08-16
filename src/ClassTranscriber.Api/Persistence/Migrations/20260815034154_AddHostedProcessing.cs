using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClassTranscriber.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHostedProcessing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DiarizationSource",
                table: "Transcripts",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HostedDiarizationCostClassification",
                table: "Transcripts",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "HostedDiarizationCostMicroUsd",
                table: "Transcripts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HostedDiarizationModel",
                table: "Transcripts",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HostedDiarizationProvider",
                table: "Transcripts",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "HostedDiarizationRateMicroUsdPerHour",
                table: "Transcripts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HostedDiarizationRequestCount",
                table: "Transcripts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HostedRequestCount",
                table: "Transcripts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HostedSttCostClassification",
                table: "Transcripts",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "HostedSttCostMicroUsd",
                table: "Transcripts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HostedSttModel",
                table: "Transcripts",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HostedSttProvider",
                table: "Transcripts",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "HostedSttRateMicroUsdPerHour",
                table: "Transcripts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NativeDiarizationUsed",
                table: "Transcripts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpeakerRoleAttributionModel",
                table: "Transcripts",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpeakerRoleAttributionStatus",
                table: "Transcripts",
                type: "TEXT",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SpeakerRoleCostMicroUsd",
                table: "Transcripts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SpeakerRoleOutputTokens",
                table: "Transcripts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SpeakerRolePromptTokens",
                table: "Transcripts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Settings_SpeakerRoleAttributionEnabled",
                table: "Projects",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "DefaultSpeakerRoleAttributionEnabled",
                table: "GlobalSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.UpdateData(
                table: "GlobalSettings",
                keyColumn: "Id",
                keyValue: 1,
                column: "DefaultSpeakerRoleAttributionEnabled",
                value: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DiarizationSource",
                table: "Transcripts");

            migrationBuilder.DropColumn(
                name: "HostedDiarizationCostClassification",
                table: "Transcripts");

            migrationBuilder.DropColumn(
                name: "HostedDiarizationCostMicroUsd",
                table: "Transcripts");

            migrationBuilder.DropColumn(
                name: "HostedDiarizationModel",
                table: "Transcripts");

            migrationBuilder.DropColumn(
                name: "HostedDiarizationProvider",
                table: "Transcripts");

            migrationBuilder.DropColumn(
                name: "HostedDiarizationRateMicroUsdPerHour",
                table: "Transcripts");

            migrationBuilder.DropColumn(
                name: "HostedDiarizationRequestCount",
                table: "Transcripts");

            migrationBuilder.DropColumn(
                name: "HostedRequestCount",
                table: "Transcripts");

            migrationBuilder.DropColumn(
                name: "HostedSttCostClassification",
                table: "Transcripts");

            migrationBuilder.DropColumn(
                name: "HostedSttCostMicroUsd",
                table: "Transcripts");

            migrationBuilder.DropColumn(
                name: "HostedSttModel",
                table: "Transcripts");

            migrationBuilder.DropColumn(
                name: "HostedSttProvider",
                table: "Transcripts");

            migrationBuilder.DropColumn(
                name: "HostedSttRateMicroUsdPerHour",
                table: "Transcripts");

            migrationBuilder.DropColumn(
                name: "NativeDiarizationUsed",
                table: "Transcripts");

            migrationBuilder.DropColumn(
                name: "SpeakerRoleAttributionModel",
                table: "Transcripts");

            migrationBuilder.DropColumn(
                name: "SpeakerRoleAttributionStatus",
                table: "Transcripts");

            migrationBuilder.DropColumn(
                name: "SpeakerRoleCostMicroUsd",
                table: "Transcripts");

            migrationBuilder.DropColumn(
                name: "SpeakerRoleOutputTokens",
                table: "Transcripts");

            migrationBuilder.DropColumn(
                name: "SpeakerRolePromptTokens",
                table: "Transcripts");

            migrationBuilder.DropColumn(
                name: "Settings_SpeakerRoleAttributionEnabled",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "DefaultSpeakerRoleAttributionEnabled",
                table: "GlobalSettings");
        }
    }
}
