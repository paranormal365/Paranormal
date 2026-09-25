using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class EventStaffRoomCursors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "StaffRoomCoversUpToUtc",
                table: "HostedEvents",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StaffRoomLastPostUtc",
                table: "HostedEvents",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StaffRoomCoversUpToUtc",
                table: "HostedEvents");

            migrationBuilder.DropColumn(
                name: "StaffRoomLastPostUtc",
                table: "HostedEvents");
        }
    }
}
