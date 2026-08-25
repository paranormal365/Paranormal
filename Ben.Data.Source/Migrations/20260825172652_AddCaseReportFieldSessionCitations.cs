using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddCaseReportFieldSessionCitations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CaseReportSectionFieldSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CaseReportSectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FieldSessionUploadId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Caption = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CaseReportSectionFieldSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CaseReportSectionFieldSessions_CaseReportSections_CaseReportSectionId",
                        column: x => x.CaseReportSectionId,
                        principalTable: "CaseReportSections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CaseReportSectionFieldSessions_FieldSessionUploads_FieldSessionUploadId",
                        column: x => x.FieldSessionUploadId,
                        principalTable: "FieldSessionUploads",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CaseReportSectionFieldSessions_CaseReportSectionId_FieldSessionUploadId",
                table: "CaseReportSectionFieldSessions",
                columns: new[] { "CaseReportSectionId", "FieldSessionUploadId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CaseReportSectionFieldSessions_FieldSessionUploadId",
                table: "CaseReportSectionFieldSessions",
                column: "FieldSessionUploadId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CaseReportSectionFieldSessions");
        }
    }
}
