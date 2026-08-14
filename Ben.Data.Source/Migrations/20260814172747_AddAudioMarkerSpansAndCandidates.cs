using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddAudioMarkerSpansAndCandidates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<float>(
                name: "DetectionScore",
                table: "AudioMarkers",
                type: "real",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "EndSeconds",
                table: "AudioMarkers",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsAutoDetected",
                table: "AudioMarkers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "LinkedClipUploadFileId",
                table: "AudioMarkers",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReviewStatus",
                table: "AudioMarkers",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_AudioMarkers_LinkedClipUploadFileId",
                table: "AudioMarkers",
                column: "LinkedClipUploadFileId");

            migrationBuilder.CreateIndex(
                name: "IX_AudioMarkers_UploadFileId_ReviewStatus",
                table: "AudioMarkers",
                columns: new[] { "UploadFileId", "ReviewStatus" });

            migrationBuilder.AddForeignKey(
                name: "FK_AudioMarkers_UploadFiles_LinkedClipUploadFileId",
                table: "AudioMarkers",
                column: "LinkedClipUploadFileId",
                principalTable: "UploadFiles",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AudioMarkers_UploadFiles_LinkedClipUploadFileId",
                table: "AudioMarkers");

            migrationBuilder.DropIndex(
                name: "IX_AudioMarkers_LinkedClipUploadFileId",
                table: "AudioMarkers");

            migrationBuilder.DropIndex(
                name: "IX_AudioMarkers_UploadFileId_ReviewStatus",
                table: "AudioMarkers");

            migrationBuilder.DropColumn(
                name: "DetectionScore",
                table: "AudioMarkers");

            migrationBuilder.DropColumn(
                name: "EndSeconds",
                table: "AudioMarkers");

            migrationBuilder.DropColumn(
                name: "IsAutoDetected",
                table: "AudioMarkers");

            migrationBuilder.DropColumn(
                name: "LinkedClipUploadFileId",
                table: "AudioMarkers");

            migrationBuilder.DropColumn(
                name: "ReviewStatus",
                table: "AudioMarkers");
        }
    }
}
