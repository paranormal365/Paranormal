using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class RenewalGracePeriod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "GraceForPeriodEnd",
                table: "OrganizationSubscriptions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "GraceGrantedToAppUserId",
                table: "OrganizationSubscriptions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "GraceGrantedUtc",
                table: "OrganizationSubscriptions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "GraceUntilUtc",
                table: "OrganizationSubscriptions",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GraceForPeriodEnd",
                table: "OrganizationSubscriptions");

            migrationBuilder.DropColumn(
                name: "GraceGrantedToAppUserId",
                table: "OrganizationSubscriptions");

            migrationBuilder.DropColumn(
                name: "GraceGrantedUtc",
                table: "OrganizationSubscriptions");

            migrationBuilder.DropColumn(
                name: "GraceUntilUtc",
                table: "OrganizationSubscriptions");
        }
    }
}
