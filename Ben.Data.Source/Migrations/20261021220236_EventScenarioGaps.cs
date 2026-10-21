using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class EventScenarioGaps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OrgCalendarEventId",
                table: "OrgMessages",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "StartsSessions",
                table: "HostedEventStaff",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "AttendanceRecordedByAppUserId",
                table: "HostedEventSessionSignUps",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AttendedUtc",
                table: "HostedEventSessionSignUps",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AsksForVotes",
                table: "HostedEvents",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "DoorTicketToken",
                table: "HostedEvents",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EvidenceDays",
                table: "HostedEvents",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MissedYouNote",
                table: "HostedEvents",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ResultsInDays",
                table: "HostedEvents",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ResultsSentUtc",
                table: "HostedEvents",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResultsUrl",
                table: "HostedEvents",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            // True for existing events, by hand: the model has no default (EF would then omit a false on
            // insert), the same as SendThankYou. An event the door scanned people in at sends its no-shows
            // "we missed you" instead of a thank-you unless the organizer turns it off.
            migrationBuilder.AddColumn<bool>(
                name: "SendMissedYou",
                table: "HostedEvents",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AskedAtTheDoorUtc",
                table: "HostedEventBookings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ResultsSentUtc",
                table: "HostedEventBookings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ConfirmsOnAccept",
                table: "EventAttendanceInvites",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_OrgMessages_OrgCalendarEventId_DateCreated",
                table: "OrgMessages",
                columns: new[] { "OrgCalendarEventId", "DateCreated" },
                filter: "[OrgCalendarEventId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_HostedEvents_DoorTicketToken",
                table: "HostedEvents",
                column: "DoorTicketToken",
                unique: true,
                filter: "[DoorTicketToken] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_OrgMessages_OrgCalendarEvents_OrgCalendarEventId",
                table: "OrgMessages",
                column: "OrgCalendarEventId",
                principalTable: "OrgCalendarEvents",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrgMessages_OrgCalendarEvents_OrgCalendarEventId",
                table: "OrgMessages");

            migrationBuilder.DropIndex(
                name: "IX_OrgMessages_OrgCalendarEventId_DateCreated",
                table: "OrgMessages");

            migrationBuilder.DropIndex(
                name: "IX_HostedEvents_DoorTicketToken",
                table: "HostedEvents");

            migrationBuilder.DropColumn(
                name: "OrgCalendarEventId",
                table: "OrgMessages");

            migrationBuilder.DropColumn(
                name: "StartsSessions",
                table: "HostedEventStaff");

            migrationBuilder.DropColumn(
                name: "AttendanceRecordedByAppUserId",
                table: "HostedEventSessionSignUps");

            migrationBuilder.DropColumn(
                name: "AttendedUtc",
                table: "HostedEventSessionSignUps");

            migrationBuilder.DropColumn(
                name: "AsksForVotes",
                table: "HostedEvents");

            migrationBuilder.DropColumn(
                name: "DoorTicketToken",
                table: "HostedEvents");

            migrationBuilder.DropColumn(
                name: "EvidenceDays",
                table: "HostedEvents");

            migrationBuilder.DropColumn(
                name: "MissedYouNote",
                table: "HostedEvents");

            migrationBuilder.DropColumn(
                name: "ResultsInDays",
                table: "HostedEvents");

            migrationBuilder.DropColumn(
                name: "ResultsSentUtc",
                table: "HostedEvents");

            migrationBuilder.DropColumn(
                name: "ResultsUrl",
                table: "HostedEvents");

            migrationBuilder.DropColumn(
                name: "SendMissedYou",
                table: "HostedEvents");

            migrationBuilder.DropColumn(
                name: "AskedAtTheDoorUtc",
                table: "HostedEventBookings");

            migrationBuilder.DropColumn(
                name: "ResultsSentUtc",
                table: "HostedEventBookings");

            migrationBuilder.DropColumn(
                name: "ConfirmsOnAccept",
                table: "EventAttendanceInvites");
        }
    }
}
