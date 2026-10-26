using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class EvpScanSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EvpScanOptionsJson",
                table: "UploadFileAudioConfigs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "BandHighHz",
                table: "EvpScans",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "BandLowHz",
                table: "EvpScans",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OptionsJson",
                table: "EvpScans",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Exploratory",
                table: "EvpScanCandidates",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsExploratory",
                table: "AudioMarkers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "EvpScanPresets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    OptionsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvpScanPresets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EvpScanPresets_AppUsers_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EvpScanPresets_AppUserId_Name",
                table: "EvpScanPresets",
                columns: new[] { "AppUserId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EvpScanPresets_OneDefault",
                table: "EvpScanPresets",
                column: "AppUserId",
                unique: true,
                filter: "[IsDefault] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EvpScanPresets");

            migrationBuilder.DropColumn(
                name: "EvpScanOptionsJson",
                table: "UploadFileAudioConfigs");

            migrationBuilder.DropColumn(
                name: "BandHighHz",
                table: "EvpScans");

            migrationBuilder.DropColumn(
                name: "BandLowHz",
                table: "EvpScans");

            migrationBuilder.DropColumn(
                name: "OptionsJson",
                table: "EvpScans");

            migrationBuilder.DropColumn(
                name: "Exploratory",
                table: "EvpScanCandidates");

            migrationBuilder.DropColumn(
                name: "IsExploratory",
                table: "AudioMarkers");
        }
    }
}
