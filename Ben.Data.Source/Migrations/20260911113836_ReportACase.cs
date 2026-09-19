using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class ReportACase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OrgMessageReports_OrgMessageId_ReportedByAppUserId",
                table: "OrgMessageReports");

            migrationBuilder.AlterColumn<Guid>(
                name: "OrgMessageId",
                table: "OrgMessageReports",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "CaseId",
                table: "OrgMessageReports",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrgMessageReports_CaseId_ReportedByAppUserId",
                table: "OrgMessageReports",
                columns: new[] { "CaseId", "ReportedByAppUserId" },
                unique: true,
                filter: "[CaseId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OrgMessageReports_OrgMessageId_ReportedByAppUserId",
                table: "OrgMessageReports",
                columns: new[] { "OrgMessageId", "ReportedByAppUserId" },
                unique: true,
                filter: "[OrgMessageId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_OrgMessageReports_Cases_CaseId",
                table: "OrgMessageReports",
                column: "CaseId",
                principalTable: "Cases",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrgMessageReports_Cases_CaseId",
                table: "OrgMessageReports");

            migrationBuilder.DropIndex(
                name: "IX_OrgMessageReports_CaseId_ReportedByAppUserId",
                table: "OrgMessageReports");

            migrationBuilder.DropIndex(
                name: "IX_OrgMessageReports_OrgMessageId_ReportedByAppUserId",
                table: "OrgMessageReports");

            migrationBuilder.DropColumn(
                name: "CaseId",
                table: "OrgMessageReports");

            migrationBuilder.AlterColumn<Guid>(
                name: "OrgMessageId",
                table: "OrgMessageReports",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrgMessageReports_OrgMessageId_ReportedByAppUserId",
                table: "OrgMessageReports",
                columns: new[] { "OrgMessageId", "ReportedByAppUserId" },
                unique: true);
        }
    }
}
