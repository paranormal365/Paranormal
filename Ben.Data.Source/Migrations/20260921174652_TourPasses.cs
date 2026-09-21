using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class TourPasses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CheckedInByAppUserId",
                table: "OrgCalendarEventAttendees",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CheckedInUtc",
                table: "OrgCalendarEventAttendees",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PassIssuedUtc",
                table: "OrgCalendarEventAttendees",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PassToken",
                table: "OrgCalendarEventAttendees",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrgCalendarEventAttendees_PassToken",
                table: "OrgCalendarEventAttendees",
                column: "PassToken",
                unique: true,
                filter: "[PassToken] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OrgCalendarEventAttendees_PassToken",
                table: "OrgCalendarEventAttendees");

            migrationBuilder.DropColumn(
                name: "CheckedInByAppUserId",
                table: "OrgCalendarEventAttendees");

            migrationBuilder.DropColumn(
                name: "CheckedInUtc",
                table: "OrgCalendarEventAttendees");

            migrationBuilder.DropColumn(
                name: "PassIssuedUtc",
                table: "OrgCalendarEventAttendees");

            migrationBuilder.DropColumn(
                name: "PassToken",
                table: "OrgCalendarEventAttendees");
        }
    }
}
