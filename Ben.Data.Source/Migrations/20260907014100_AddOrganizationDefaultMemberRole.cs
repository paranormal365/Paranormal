using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationDefaultMemberRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DefaultMemberRoleId",
                table: "Organizations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Organizations_DefaultMemberRoleId",
                table: "Organizations",
                column: "DefaultMemberRoleId");

            migrationBuilder.AddForeignKey(
                name: "FK_Organizations_OrganizationRoles_DefaultMemberRoleId",
                table: "Organizations",
                column: "DefaultMemberRoleId",
                principalTable: "OrganizationRoles",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Organizations_OrganizationRoles_DefaultMemberRoleId",
                table: "Organizations");

            migrationBuilder.DropIndex(
                name: "IX_Organizations_DefaultMemberRoleId",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "DefaultMemberRoleId",
                table: "Organizations");
        }
    }
}
