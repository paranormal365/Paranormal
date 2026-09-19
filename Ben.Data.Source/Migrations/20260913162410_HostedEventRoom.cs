using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <summary>
    /// The event's room (item 235 phase 11): two nullable columns, <c>OrgMessages.HostedEventId</c> and
    /// <c>HostedEvents.RoomClosedUtc</c>. No existing message gains an event.
    /// </summary>
    public partial class HostedEventRoom : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "HostedEventId",
                table: "OrgMessages",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RoomClosedUtc",
                table: "HostedEvents",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrgMessages_HostedEventId_DateCreated",
                table: "OrgMessages",
                columns: new[] { "HostedEventId", "DateCreated" },
                filter: "[HostedEventId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_OrgMessages_HostedEvents_HostedEventId",
                table: "OrgMessages",
                column: "HostedEventId",
                principalTable: "HostedEvents",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrgMessages_HostedEvents_HostedEventId",
                table: "OrgMessages");

            migrationBuilder.DropIndex(
                name: "IX_OrgMessages_HostedEventId_DateCreated",
                table: "OrgMessages");

            migrationBuilder.DropColumn(
                name: "HostedEventId",
                table: "OrgMessages");

            migrationBuilder.DropColumn(
                name: "RoomClosedUtc",
                table: "HostedEvents");
        }
    }
}
