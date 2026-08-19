using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddPublicEventFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OrgCalendarEvents_OrganizationId",
                table: "OrgCalendarEvents");

            migrationBuilder.AddColumn<int>(
                name: "AttendeeCapacity",
                table: "OrgCalendarEvents",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HideExactLocation",
                table: "OrgCalendarEvents",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "PlaceId",
                table: "OrgCalendarEvents",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RsvpClosesAt",
                table: "OrgCalendarEvents",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrgCalendarEvents_OrganizationId_IsPublic_StartDateTime",
                table: "OrgCalendarEvents",
                columns: new[] { "OrganizationId", "IsPublic", "StartDateTime" });

            migrationBuilder.CreateIndex(
                name: "IX_OrgCalendarEvents_PlaceId",
                table: "OrgCalendarEvents",
                column: "PlaceId");

            migrationBuilder.AddForeignKey(
                name: "FK_OrgCalendarEvents_Places_PlaceId",
                table: "OrgCalendarEvents",
                column: "PlaceId",
                principalTable: "Places",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrgCalendarEvents_Places_PlaceId",
                table: "OrgCalendarEvents");

            migrationBuilder.DropIndex(
                name: "IX_OrgCalendarEvents_OrganizationId_IsPublic_StartDateTime",
                table: "OrgCalendarEvents");

            migrationBuilder.DropIndex(
                name: "IX_OrgCalendarEvents_PlaceId",
                table: "OrgCalendarEvents");

            migrationBuilder.DropColumn(
                name: "AttendeeCapacity",
                table: "OrgCalendarEvents");

            migrationBuilder.DropColumn(
                name: "HideExactLocation",
                table: "OrgCalendarEvents");

            migrationBuilder.DropColumn(
                name: "PlaceId",
                table: "OrgCalendarEvents");

            migrationBuilder.DropColumn(
                name: "RsvpClosesAt",
                table: "OrgCalendarEvents");

            migrationBuilder.CreateIndex(
                name: "IX_OrgCalendarEvents_OrganizationId",
                table: "OrgCalendarEvents",
                column: "OrganizationId");
        }
    }
}
