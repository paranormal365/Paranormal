using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganiserSentAttendanceInvites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "InvitedByAppUserId",
                table: "EventAttendanceInvites",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_EventAttendanceInvites_InvitedByAppUserId",
                table: "EventAttendanceInvites",
                column: "InvitedByAppUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_EventAttendanceInvites_AppUsers_InvitedByAppUserId",
                table: "EventAttendanceInvites",
                column: "InvitedByAppUserId",
                principalTable: "AppUsers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EventAttendanceInvites_AppUsers_InvitedByAppUserId",
                table: "EventAttendanceInvites");

            migrationBuilder.DropIndex(
                name: "IX_EventAttendanceInvites_InvitedByAppUserId",
                table: "EventAttendanceInvites");

            migrationBuilder.DropColumn(
                name: "InvitedByAppUserId",
                table: "EventAttendanceInvites");
        }
    }
}
