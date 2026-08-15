using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddCalendarEventAddressAndMeetingUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MeetingUrl",
                table: "OrgCalendarEvents",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OrganizationAddressId",
                table: "OrgCalendarEvents",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrgCalendarEvents_OrganizationAddressId",
                table: "OrgCalendarEvents",
                column: "OrganizationAddressId");

            migrationBuilder.AddForeignKey(
                name: "FK_OrgCalendarEvents_OrganizationAddresses_OrganizationAddressId",
                table: "OrgCalendarEvents",
                column: "OrganizationAddressId",
                principalTable: "OrganizationAddresses",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrgCalendarEvents_OrganizationAddresses_OrganizationAddressId",
                table: "OrgCalendarEvents");

            migrationBuilder.DropIndex(
                name: "IX_OrgCalendarEvents_OrganizationAddressId",
                table: "OrgCalendarEvents");

            migrationBuilder.DropColumn(
                name: "MeetingUrl",
                table: "OrgCalendarEvents");

            migrationBuilder.DropColumn(
                name: "OrganizationAddressId",
                table: "OrgCalendarEvents");
        }
    }
}
