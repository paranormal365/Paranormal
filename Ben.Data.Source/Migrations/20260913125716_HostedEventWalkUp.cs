using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <summary>
    /// People who turned up on the night without a booking (item 235 phase 7).
    /// </summary>
    /// <remarks>
    /// A new table and nothing else. Ben, 2026-09-13: <i>"availability count for walk ups to event
    /// and on-sight sign ups."</i> A head count with a name on it when somebody gave one — no
    /// account is invented for them, because the site's settled answer to signing up a stranger is
    /// to send a link rather than to make an account from an address nobody verified, and a link
    /// is no use to somebody standing in a doorway with cash in their hand.
    /// </remarks>
    public partial class HostedEventWalkUp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HostedEventWalkUps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HostedEventNightId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    People = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ArrivedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RecordedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostedEventWalkUps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HostedEventWalkUps_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_HostedEventWalkUps_AppUsers_RecordedByAppUserId",
                        column: x => x.RecordedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_HostedEventWalkUps_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_HostedEventWalkUps_HostedEventNights_HostedEventNightId",
                        column: x => x.HostedEventNightId,
                        principalTable: "HostedEventNights",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventWalkUps_CreatedByAppUserId",
                table: "HostedEventWalkUps",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventWalkUps_HostedEventNightId",
                table: "HostedEventWalkUps",
                column: "HostedEventNightId");

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventWalkUps_RecordedByAppUserId",
                table: "HostedEventWalkUps",
                column: "RecordedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventWalkUps_UpdatedByAppUserId",
                table: "HostedEventWalkUps",
                column: "UpdatedByAppUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HostedEventWalkUps");
        }
    }
}
