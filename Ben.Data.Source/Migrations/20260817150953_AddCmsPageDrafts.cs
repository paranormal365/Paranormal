using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddCmsPageDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DraftOfOrganizationPageId",
                table: "OrganizationPages",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationPages_DraftOfOrganizationPageId",
                table: "OrganizationPages",
                column: "DraftOfOrganizationPageId",
                unique: true,
                filter: "[DraftOfOrganizationPageId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_OrganizationPages_OrganizationPages_DraftOfOrganizationPageId",
                table: "OrganizationPages",
                column: "DraftOfOrganizationPageId",
                principalTable: "OrganizationPages",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrganizationPages_OrganizationPages_DraftOfOrganizationPageId",
                table: "OrganizationPages");

            migrationBuilder.DropIndex(
                name: "IX_OrganizationPages_DraftOfOrganizationPageId",
                table: "OrganizationPages");

            migrationBuilder.DropColumn(
                name: "DraftOfOrganizationPageId",
                table: "OrganizationPages");
        }
    }
}
