using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class EvpVoiceLikelihood : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VoiceModel",
                table: "EvpScans",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "VoiceLikelihood",
                table: "EvpScanCandidates",
                type: "real",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "VoiceLikelihood",
                table: "AudioMarkers",
                type: "real",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VoiceModel",
                table: "EvpScans");

            migrationBuilder.DropColumn(
                name: "VoiceLikelihood",
                table: "EvpScanCandidates");

            migrationBuilder.DropColumn(
                name: "VoiceLikelihood",
                table: "AudioMarkers");
        }
    }
}
