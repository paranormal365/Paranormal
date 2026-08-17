using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddEventAttendanceInvites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EventAttendanceInvites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrgCalendarEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Token = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    DateExpires = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateConfirmed = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConfirmedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventAttendanceInvites", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventAttendanceInvites_AppUsers_ConfirmedByAppUserId",
                        column: x => x.ConfirmedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EventAttendanceInvites_OrgCalendarEvents_OrgCalendarEventId",
                        column: x => x.OrgCalendarEventId,
                        principalTable: "OrgCalendarEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EventAttendanceInvites_ConfirmedByAppUserId",
                table: "EventAttendanceInvites",
                column: "ConfirmedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EventAttendanceInvites_OrgCalendarEventId_Email",
                table: "EventAttendanceInvites",
                columns: new[] { "OrgCalendarEventId", "Email" });

            migrationBuilder.CreateIndex(
                name: "IX_EventAttendanceInvites_Token",
                table: "EventAttendanceInvites",
                column: "Token",
                unique: true,
                filter: "[Token] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EventAttendanceInvites");
        }
    }
}
