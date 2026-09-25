using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class OrganizationJoinLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "JoinToken",
                table: "Organizations",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "JoinTokenCreatedByAppUserId",
                table: "Organizations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "JoinTokenExpiresUtc",
                table: "Organizations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Organizations_JoinToken",
                table: "Organizations",
                column: "JoinToken",
                unique: true,
                filter: "[JoinToken] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Organizations_JoinToken",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "JoinToken",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "JoinTokenCreatedByAppUserId",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "JoinTokenExpiresUtc",
                table: "Organizations");
        }
    }
}
