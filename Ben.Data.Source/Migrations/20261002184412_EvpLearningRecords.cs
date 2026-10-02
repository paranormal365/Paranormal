using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class EvpLearningRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EvpRulings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UploadFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AudioMarkerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Ruling = table.Column<int>(type: "int", nullable: false),
                    PlayedFirst = table.Column<bool>(type: "bit", nullable: true),
                    BoundsAdjusted = table.Column<bool>(type: "bit", nullable: false),
                    DetectionScore = table.Column<float>(type: "real", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvpRulings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EvpRulings_UploadFiles_UploadFileId",
                        column: x => x.UploadFileId,
                        principalTable: "UploadFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EvpScans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UploadFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DetectorVersion = table.Column<int>(type: "int", nullable: false),
                    Sensitivity = table.Column<int>(type: "int", nullable: false),
                    ThresholdDb = table.Column<double>(type: "float", nullable: false),
                    MinDurationSeconds = table.Column<double>(type: "float", nullable: false),
                    MergeGapSeconds = table.Column<double>(type: "float", nullable: false),
                    ContextPadSeconds = table.Column<double>(type: "float", nullable: false),
                    MaxEventSeconds = table.Column<double>(type: "float", nullable: false),
                    RecordingSeconds = table.Column<double>(type: "float", nullable: false),
                    FoundCount = table.Column<int>(type: "int", nullable: false),
                    ProposedCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvpScans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EvpScans_UploadFiles_UploadFileId",
                        column: x => x.UploadFileId,
                        principalTable: "UploadFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EvpScanCandidates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EvpScanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AudioMarkerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Proposed = table.Column<bool>(type: "bit", nullable: false),
                    StartSeconds = table.Column<double>(type: "float", nullable: false),
                    EndSeconds = table.Column<double>(type: "float", nullable: false),
                    Score = table.Column<float>(type: "real", nullable: false),
                    PeakProminenceDb = table.Column<double>(type: "float", nullable: false),
                    MeanBandGapDb = table.Column<double>(type: "float", nullable: false),
                    EventSeconds = table.Column<double>(type: "float", nullable: false),
                    MeanFloorDb = table.Column<double>(type: "float", nullable: false),
                    PeakBandDb = table.Column<double>(type: "float", nullable: false),
                    BandLevelSpreadDb = table.Column<double>(type: "float", nullable: false),
                    ZeroCrossingRate = table.Column<double>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvpScanCandidates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EvpScanCandidates_EvpScans_EvpScanId",
                        column: x => x.EvpScanId,
                        principalTable: "EvpScans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EvpRulings_AudioMarkerId",
                table: "EvpRulings",
                column: "AudioMarkerId");

            migrationBuilder.CreateIndex(
                name: "IX_EvpRulings_UploadFileId",
                table: "EvpRulings",
                column: "UploadFileId");

            migrationBuilder.CreateIndex(
                name: "IX_EvpScanCandidates_AudioMarkerId",
                table: "EvpScanCandidates",
                column: "AudioMarkerId");

            migrationBuilder.CreateIndex(
                name: "IX_EvpScanCandidates_EvpScanId",
                table: "EvpScanCandidates",
                column: "EvpScanId");

            migrationBuilder.CreateIndex(
                name: "IX_EvpScans_UploadFileId_DateCreated",
                table: "EvpScans",
                columns: new[] { "UploadFileId", "DateCreated" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EvpRulings");

            migrationBuilder.DropTable(
                name: "EvpScanCandidates");

            migrationBuilder.DropTable(
                name: "EvpScans");
        }
    }
}
