using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class FieldSessionsAtEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FieldLaunchId",
                table: "FieldSessionUploads",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "HostedEventId",
                table: "FieldSessionUploads",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OrgCalendarEventId",
                table: "FieldSessionUploads",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_FieldSessionUploads_HostedEventId_StartedAt",
                table: "FieldSessionUploads",
                columns: new[] { "HostedEventId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_FieldSessionUploads_OrgCalendarEventId_StartedAt",
                table: "FieldSessionUploads",
                columns: new[] { "OrgCalendarEventId", "StartedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FieldSessionUploads_HostedEventId_StartedAt",
                table: "FieldSessionUploads");

            migrationBuilder.DropIndex(
                name: "IX_FieldSessionUploads_OrgCalendarEventId_StartedAt",
                table: "FieldSessionUploads");

            migrationBuilder.DropColumn(
                name: "FieldLaunchId",
                table: "FieldSessionUploads");

            migrationBuilder.DropColumn(
                name: "HostedEventId",
                table: "FieldSessionUploads");

            migrationBuilder.DropColumn(
                name: "OrgCalendarEventId",
                table: "FieldSessionUploads");
        }
    }
}
