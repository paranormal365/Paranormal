using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddEventReminderSent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EventReminderSents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrgCalendarEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SentUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventReminderSents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventReminderSents_AppUsers_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EventReminderSents_OrgCalendarEvents_OrgCalendarEventId",
                        column: x => x.OrgCalendarEventId,
                        principalTable: "OrgCalendarEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EventReminderSents_AppUserId",
                table: "EventReminderSents",
                column: "AppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EventReminderSents_OrgCalendarEventId_AppUserId",
                table: "EventReminderSents",
                columns: new[] { "OrgCalendarEventId", "AppUserId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EventReminderSents");
        }
    }
}
