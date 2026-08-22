using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddCasePauseAndLapseNotices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "OneWeekNoticeSentForPeriodEnd",
                table: "OrganizationSubscriptions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TwoWeekNoticeSentForPeriodEnd",
                table: "OrganizationSubscriptions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StatusBeforePause",
                table: "Cases",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OneWeekNoticeSentForPeriodEnd",
                table: "OrganizationSubscriptions");

            migrationBuilder.DropColumn(
                name: "TwoWeekNoticeSentForPeriodEnd",
                table: "OrganizationSubscriptions");

            migrationBuilder.DropColumn(
                name: "StatusBeforePause",
                table: "Cases");
        }
    }
}
