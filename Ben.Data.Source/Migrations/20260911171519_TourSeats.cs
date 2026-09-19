using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class TourSeats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "GuestAcknowledgedUtc",
                table: "OrgCalendarEventAttendees",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SeatDecidedByAppUserId",
                table: "OrgCalendarEventAttendees",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SeatDecidedUtc",
                table: "OrgCalendarEventAttendees",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SeatStatus",
                table: "OrgCalendarEventAttendees",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Seats",
                table: "OrgCalendarEventAttendees",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "Seats",
                table: "EventAttendanceInvites",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrgCalendarEventAttendees_OrgCalendarEventId_SeatStatus",
                table: "OrgCalendarEventAttendees",
                columns: new[] { "OrgCalendarEventId", "SeatStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_OrgCalendarEventAttendees_SeatDecidedByAppUserId",
                table: "OrgCalendarEventAttendees",
                column: "SeatDecidedByAppUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_OrgCalendarEventAttendees_AppUsers_SeatDecidedByAppUserId",
                table: "OrgCalendarEventAttendees",
                column: "SeatDecidedByAppUserId",
                principalTable: "AppUsers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrgCalendarEventAttendees_AppUsers_SeatDecidedByAppUserId",
                table: "OrgCalendarEventAttendees");

            migrationBuilder.DropIndex(
                name: "IX_OrgCalendarEventAttendees_OrgCalendarEventId_SeatStatus",
                table: "OrgCalendarEventAttendees");

            migrationBuilder.DropIndex(
                name: "IX_OrgCalendarEventAttendees_SeatDecidedByAppUserId",
                table: "OrgCalendarEventAttendees");

            migrationBuilder.DropColumn(
                name: "GuestAcknowledgedUtc",
                table: "OrgCalendarEventAttendees");

            migrationBuilder.DropColumn(
                name: "SeatDecidedByAppUserId",
                table: "OrgCalendarEventAttendees");

            migrationBuilder.DropColumn(
                name: "SeatDecidedUtc",
                table: "OrgCalendarEventAttendees");

            migrationBuilder.DropColumn(
                name: "SeatStatus",
                table: "OrgCalendarEventAttendees");

            migrationBuilder.DropColumn(
                name: "Seats",
                table: "OrgCalendarEventAttendees");

            migrationBuilder.DropColumn(
                name: "Seats",
                table: "EventAttendanceInvites");
        }
    }
}
