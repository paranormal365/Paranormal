using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <summary>
    /// Item 235 phase 12: when an event's files were removed under the 90-day rule, and the two warnings.
    /// </summary>
    public partial class HostedEventRetention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "MediaClearedUtc",
                table: "HostedEvents",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RetentionWarnedMonthUtc",
                table: "HostedEvents",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RetentionWarnedWeekUtc",
                table: "HostedEvents",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MediaClearedUtc",
                table: "HostedEvents");

            migrationBuilder.DropColumn(
                name: "RetentionWarnedMonthUtc",
                table: "HostedEvents");

            migrationBuilder.DropColumn(
                name: "RetentionWarnedWeekUtc",
                table: "HostedEvents");
        }
    }
}
