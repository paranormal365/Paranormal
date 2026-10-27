using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class EvpLearningReads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<float>(
                name: "SpectralFlatness",
                table: "EvpScanCandidates",
                type: "real",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "VoicedFrameFraction",
                table: "EvpScanCandidates",
                type: "real",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Blind",
                table: "EvpRulings",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EvpBlindReview",
                table: "AppUsers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "EvpMisses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UploadFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EvpScanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AudioMarkerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartSeconds = table.Column<double>(type: "float", nullable: false),
                    EndSeconds = table.Column<double>(type: "float", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvpMisses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EvpMisses_UploadFiles_UploadFileId",
                        column: x => x.UploadFileId,
                        principalTable: "UploadFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EvpMisses_DateCreated",
                table: "EvpMisses",
                column: "DateCreated");

            migrationBuilder.CreateIndex(
                name: "IX_EvpMisses_UploadFileId",
                table: "EvpMisses",
                column: "UploadFileId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EvpMisses");

            migrationBuilder.DropColumn(
                name: "SpectralFlatness",
                table: "EvpScanCandidates");

            migrationBuilder.DropColumn(
                name: "VoicedFrameFraction",
                table: "EvpScanCandidates");

            migrationBuilder.DropColumn(
                name: "Blind",
                table: "EvpRulings");

            migrationBuilder.DropColumn(
                name: "EvpBlindReview",
                table: "AppUsers");
        }
    }
}
