using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddInvestigationAttendeeArrival : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AttendanceRecordedByAppUserId",
                table: "InvestigationAttendees",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DateArrived",
                table: "InvestigationAttendees",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvestigationAttendees_AttendanceRecordedByAppUserId",
                table: "InvestigationAttendees",
                column: "AttendanceRecordedByAppUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_InvestigationAttendees_AppUsers_AttendanceRecordedByAppUserId",
                table: "InvestigationAttendees",
                column: "AttendanceRecordedByAppUserId",
                principalTable: "AppUsers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InvestigationAttendees_AppUsers_AttendanceRecordedByAppUserId",
                table: "InvestigationAttendees");

            migrationBuilder.DropIndex(
                name: "IX_InvestigationAttendees_AttendanceRecordedByAppUserId",
                table: "InvestigationAttendees");

            migrationBuilder.DropColumn(
                name: "AttendanceRecordedByAppUserId",
                table: "InvestigationAttendees");

            migrationBuilder.DropColumn(
                name: "DateArrived",
                table: "InvestigationAttendees");
        }
    }
}
