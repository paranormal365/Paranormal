using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddTimelineInvestigationLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "InvestigationId",
                table: "CaseTimelineEntries",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CaseTimelineEntries_InvestigationId",
                table: "CaseTimelineEntries",
                column: "InvestigationId");

            migrationBuilder.AddForeignKey(
                name: "FK_CaseTimelineEntries_Investigations_InvestigationId",
                table: "CaseTimelineEntries",
                column: "InvestigationId",
                principalTable: "Investigations",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CaseTimelineEntries_Investigations_InvestigationId",
                table: "CaseTimelineEntries");

            migrationBuilder.DropIndex(
                name: "IX_CaseTimelineEntries_InvestigationId",
                table: "CaseTimelineEntries");

            migrationBuilder.DropColumn(
                name: "InvestigationId",
                table: "CaseTimelineEntries");
        }
    }
}
