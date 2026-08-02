using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddEvidenceVoteContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CaseId",
                table: "EvidenceVotes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsOriginalUploader",
                table: "EvidenceVotes",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsVoterCaseClient",
                table: "EvidenceVotes",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsVoterCaseOrgMember",
                table: "EvidenceVotes",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "VoterOrganizationName",
                table: "EvidenceVotes",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_EvidenceVotes_CaseId",
                table: "EvidenceVotes",
                column: "CaseId");

            migrationBuilder.AddForeignKey(
                name: "FK_EvidenceVotes_Cases_CaseId",
                table: "EvidenceVotes",
                column: "CaseId",
                principalTable: "Cases",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EvidenceVotes_Cases_CaseId",
                table: "EvidenceVotes");

            migrationBuilder.DropIndex(
                name: "IX_EvidenceVotes_CaseId",
                table: "EvidenceVotes");

            migrationBuilder.DropColumn(
                name: "CaseId",
                table: "EvidenceVotes");

            migrationBuilder.DropColumn(
                name: "IsOriginalUploader",
                table: "EvidenceVotes");

            migrationBuilder.DropColumn(
                name: "IsVoterCaseClient",
                table: "EvidenceVotes");

            migrationBuilder.DropColumn(
                name: "IsVoterCaseOrgMember",
                table: "EvidenceVotes");

            migrationBuilder.DropColumn(
                name: "VoterOrganizationName",
                table: "EvidenceVotes");
        }
    }
}
