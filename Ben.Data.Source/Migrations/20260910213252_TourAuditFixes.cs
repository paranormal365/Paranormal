using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class TourAuditFixes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EventReminderSents_OrgCalendarEventId_AppUserId",
                table: "EventReminderSents");

            migrationBuilder.AddColumn<DateTime>(
                name: "ForStartUtc",
                table: "EventReminderSents",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_EventReminderSents_OrgCalendarEventId_AppUserId_ForStartUtc",
                table: "EventReminderSents",
                columns: new[] { "OrgCalendarEventId", "AppUserId", "ForStartUtc" },
                unique: true,
                filter: "[ForStartUtc] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EventReminderSents_OrgCalendarEventId_AppUserId_ForStartUtc",
                table: "EventReminderSents");

            migrationBuilder.DropColumn(
                name: "ForStartUtc",
                table: "EventReminderSents");

            migrationBuilder.CreateIndex(
                name: "IX_EventReminderSents_OrgCalendarEventId_AppUserId",
                table: "EventReminderSents",
                columns: new[] { "OrgCalendarEventId", "AppUserId" },
                unique: true);
        }
    }
}
