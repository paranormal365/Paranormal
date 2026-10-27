using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class FieldSessionAnalysisCopies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SourceClipPath",
                table: "UploadFiles",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceFieldSessionUploadId",
                table: "UploadFiles",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FieldSessionUploadId",
                table: "EvpScanJobs",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FieldSessionAnalysisCopies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FieldSessionUploadId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FieldSessionUploadFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CopyUploadFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Scope = table.Column<int>(type: "int", nullable: false),
                    ScopeAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ScopeOrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    SourceRelativePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    MadeByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FieldSessionAnalysisCopies", x => x.Id);
                    table.CheckConstraint("CK_FieldSessionAnalysisCopies_Scope", "([Scope] = 0 AND [ScopeAppUserId] IS NOT NULL AND [ScopeOrganizationId] IS NULL) OR ([Scope] = 1 AND [ScopeOrganizationId] IS NOT NULL AND [ScopeAppUserId] IS NULL)");
                    table.ForeignKey(
                        name: "FK_FieldSessionAnalysisCopies_UploadFiles_CopyUploadFileId",
                        column: x => x.CopyUploadFileId,
                        principalTable: "UploadFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UploadFiles_SourceFieldSessionUploadId",
                table: "UploadFiles",
                column: "SourceFieldSessionUploadId",
                filter: "[SourceFieldSessionUploadId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_EvpScanJobs_FieldSessionUploadId",
                table: "EvpScanJobs",
                column: "FieldSessionUploadId",
                filter: "[FieldSessionUploadId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FieldSessionAnalysisCopies_CopyUploadFileId",
                table: "FieldSessionAnalysisCopies",
                column: "CopyUploadFileId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FieldSessionAnalysisCopies_FieldSessionUploadId",
                table: "FieldSessionAnalysisCopies",
                column: "FieldSessionUploadId");

            migrationBuilder.CreateIndex(
                name: "IX_FieldSessionAnalysisCopies_OnePerGroup",
                table: "FieldSessionAnalysisCopies",
                columns: new[] { "FieldSessionUploadFileId", "ScopeOrganizationId" },
                unique: true,
                filter: "[ScopeOrganizationId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FieldSessionAnalysisCopies_OnePerPerson",
                table: "FieldSessionAnalysisCopies",
                columns: new[] { "FieldSessionUploadFileId", "ScopeAppUserId" },
                unique: true,
                filter: "[ScopeAppUserId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FieldSessionAnalysisCopies");

            migrationBuilder.DropIndex(
                name: "IX_UploadFiles_SourceFieldSessionUploadId",
                table: "UploadFiles");

            migrationBuilder.DropIndex(
                name: "IX_EvpScanJobs_FieldSessionUploadId",
                table: "EvpScanJobs");

            migrationBuilder.DropColumn(
                name: "SourceClipPath",
                table: "UploadFiles");

            migrationBuilder.DropColumn(
                name: "SourceFieldSessionUploadId",
                table: "UploadFiles");

            migrationBuilder.DropColumn(
                name: "FieldSessionUploadId",
                table: "EvpScanJobs");
        }
    }
}
