using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class EvpScanJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EvpScanJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sensitivity = table.Column<int>(type: "int", nullable: false),
                    OptionsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Percent = table.Column<int>(type: "int", nullable: false),
                    Error = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StartedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FinishedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelRequestedUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvpScanJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EvpScanJobs_AppUsers_RequestedByAppUserId",
                        column: x => x.RequestedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "EvpScanJobSegments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EvpScanJobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UploadFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Percent = table.Column<int>(type: "int", nullable: false),
                    ProcessedSeconds = table.Column<double>(type: "float", nullable: false),
                    SettledSeconds = table.Column<double>(type: "float", nullable: false),
                    TotalSeconds = table.Column<double>(type: "float", nullable: true),
                    CandidateCount = table.Column<int>(type: "int", nullable: true),
                    TopScore = table.Column<float>(type: "real", nullable: true),
                    EvpScanId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Error = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    StartedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FinishedUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvpScanJobSegments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EvpScanJobSegments_EvpScanJobs_EvpScanJobId",
                        column: x => x.EvpScanJobId,
                        principalTable: "EvpScanJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EvpScanJobSegments_UploadFiles_UploadFileId",
                        column: x => x.UploadFileId,
                        principalTable: "UploadFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EvpScanJobs_RequestedByAppUserId_FinishedUtc",
                table: "EvpScanJobs",
                columns: new[] { "RequestedByAppUserId", "FinishedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_EvpScanJobs_Status_CreatedUtc",
                table: "EvpScanJobs",
                columns: new[] { "Status", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_EvpScanJobSegments_EvpScanJobId",
                table: "EvpScanJobSegments",
                column: "EvpScanJobId");

            migrationBuilder.CreateIndex(
                name: "IX_EvpScanJobSegments_OneLivePerFile",
                table: "EvpScanJobSegments",
                column: "UploadFileId",
                unique: true,
                filter: "[FinishedUtc] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EvpScanJobSegments");

            migrationBuilder.DropTable(
                name: "EvpScanJobs");
        }
    }
}
