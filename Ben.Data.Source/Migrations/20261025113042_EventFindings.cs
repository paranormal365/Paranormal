using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class EventFindings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FindingsOnPlace",
                table: "HostedEvents",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "FindingsPublishedByAppUserId",
                table: "HostedEvents",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FindingsPublishedUtc",
                table: "HostedEvents",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FindingsOffPlaceUtc",
                table: "HostedEventBookings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OnPlaceChosenUtc",
                table: "FieldSessionUploads",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PlaceOptOutUtc",
                table: "FieldSessionUploads",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OnPlaceChosenUtc",
                table: "EventEvidenceSubmissions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PlaceOptOutUtc",
                table: "EventEvidenceSubmissions",
                type: "datetime2",
                nullable: true);

            // Findings already public stay public (10/25/2026): an event at a public place with any accepted
            // evidence or a session shown with its results counts as published. New events start private.
            // EXEC so SQL Server compiles it when it runs: in the idempotent deploy script it shares a batch with the
            // ALTERs that add FindingsPublishedUtc, and a batch is compiled whole, before that column exists.
            migrationBuilder.Sql("""
                EXEC(N'
                UPDATE h SET h.FindingsPublishedUtc = SYSUTCDATETIME()
                FROM HostedEvents h
                JOIN Places p ON p.Id = h.PlaceId
                WHERE p.Kind = 2 AND h.FindingsPublishedUtc IS NULL
                  AND (EXISTS (SELECT 1 FROM EventEvidenceSubmissions s
                               JOIN OrgCalendarEvents c ON c.Id = s.OrgCalendarEventId
                               WHERE c.HostedEventId = h.Id AND s.Status = 1)
                    OR EXISTS (SELECT 1 FROM FieldSessionUploads u
                               WHERE u.HostedEventId = h.Id AND u.ShownWithEventResultsAtUtc IS NOT NULL));
                ');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FindingsOnPlace",
                table: "HostedEvents");

            migrationBuilder.DropColumn(
                name: "FindingsPublishedByAppUserId",
                table: "HostedEvents");

            migrationBuilder.DropColumn(
                name: "FindingsPublishedUtc",
                table: "HostedEvents");

            migrationBuilder.DropColumn(
                name: "FindingsOffPlaceUtc",
                table: "HostedEventBookings");

            migrationBuilder.DropColumn(
                name: "OnPlaceChosenUtc",
                table: "FieldSessionUploads");

            migrationBuilder.DropColumn(
                name: "PlaceOptOutUtc",
                table: "FieldSessionUploads");

            migrationBuilder.DropColumn(
                name: "OnPlaceChosenUtc",
                table: "EventEvidenceSubmissions");

            migrationBuilder.DropColumn(
                name: "PlaceOptOutUtc",
                table: "EventEvidenceSubmissions");
        }
    }
}
