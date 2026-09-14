using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <summary>
    /// Item 235 phase 17a (audit findings A4 and A7): what a guest needs to know about getting in and around an event,
    /// and a record of each letter a host sends to the people coming. Additive: one nullable column and one new table.
    /// </summary>
    public partial class HostedEventAnnouncementsAndAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccessNotes",
                table: "HostedEvents",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "HostedEventAnnouncements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HostedEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    HostedEventNightId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IncludeUnconfirmed = table.Column<bool>(type: "bit", nullable: false),
                    Recipients = table.Column<int>(type: "int", nullable: false),
                    Emailed = table.Column<int>(type: "int", nullable: false),
                    SentByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SentUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostedEventAnnouncements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HostedEventAnnouncements_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_HostedEventAnnouncements_AppUsers_SentByAppUserId",
                        column: x => x.SentByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_HostedEventAnnouncements_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_HostedEventAnnouncements_HostedEventNights_HostedEventNightId",
                        column: x => x.HostedEventNightId,
                        principalTable: "HostedEventNights",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_HostedEventAnnouncements_HostedEvents_HostedEventId",
                        column: x => x.HostedEventId,
                        principalTable: "HostedEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventAnnouncements_CreatedByAppUserId",
                table: "HostedEventAnnouncements",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventAnnouncements_HostedEventId_SentUtc",
                table: "HostedEventAnnouncements",
                columns: new[] { "HostedEventId", "SentUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventAnnouncements_HostedEventNightId",
                table: "HostedEventAnnouncements",
                column: "HostedEventNightId");

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventAnnouncements_SentByAppUserId",
                table: "HostedEventAnnouncements",
                column: "SentByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventAnnouncements_UpdatedByAppUserId",
                table: "HostedEventAnnouncements",
                column: "UpdatedByAppUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HostedEventAnnouncements");

            migrationBuilder.DropColumn(
                name: "AccessNotes",
                table: "HostedEvents");
        }
    }
}
