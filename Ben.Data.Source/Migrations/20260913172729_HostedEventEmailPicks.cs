using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <summary>
    /// Item 235 slice 11d: places picked by somebody not signed in, pending for fifteen minutes until
    /// they prove their email address; a contact phone on bookings and on emailed asks.
    /// </summary>
    public partial class HostedEventEmailPicks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContactPhone",
                table: "HostedEventBookings",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FirstName",
                table: "EventAttendanceInvites",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastName",
                table: "EventAttendanceInvites",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Phone",
                table: "EventAttendanceInvites",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "HostedEventEmailPicks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HostedEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    PartySize = table.Column<int>(type: "int", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    TokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ExpiresUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsLive = table.Column<bool>(type: "bit", nullable: false),
                    ConfirmedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    HostedEventBookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RefusedSentence = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostedEventEmailPicks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HostedEventEmailPicks_HostedEventBookings_HostedEventBookingId",
                        column: x => x.HostedEventBookingId,
                        principalTable: "HostedEventBookings",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_HostedEventEmailPicks_HostedEvents_HostedEventId",
                        column: x => x.HostedEventId,
                        principalTable: "HostedEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HostedEventEmailPickPlaces",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HostedEventEmailPickId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HostedEventNightId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HostedEventLayoutUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    People = table.Column<int>(type: "int", nullable: true),
                    IsLive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostedEventEmailPickPlaces", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HostedEventEmailPickPlaces_HostedEventEmailPicks_HostedEventEmailPickId",
                        column: x => x.HostedEventEmailPickId,
                        principalTable: "HostedEventEmailPicks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_HostedEventEmailPickPlaces_HostedEventLayoutUnits_HostedEventLayoutUnitId",
                        column: x => x.HostedEventLayoutUnitId,
                        principalTable: "HostedEventLayoutUnits",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_HostedEventEmailPickPlaces_HostedEventNights_HostedEventNightId",
                        column: x => x.HostedEventNightId,
                        principalTable: "HostedEventNights",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventEmailPickPlaces_HostedEventEmailPickId",
                table: "HostedEventEmailPickPlaces",
                column: "HostedEventEmailPickId");

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventEmailPickPlaces_HostedEventLayoutUnitId",
                table: "HostedEventEmailPickPlaces",
                column: "HostedEventLayoutUnitId");

            migrationBuilder.CreateIndex(
                name: "UX_HostedEventEmailPickPlaces_LiveUnitNight",
                table: "HostedEventEmailPickPlaces",
                columns: new[] { "HostedEventNightId", "HostedEventLayoutUnitId" },
                unique: true,
                filter: "[IsLive] = 1 AND [HostedEventLayoutUnitId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventEmailPicks_HostedEventBookingId",
                table: "HostedEventEmailPicks",
                column: "HostedEventBookingId");

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventEmailPicks_IsLive_ExpiresUtc",
                table: "HostedEventEmailPicks",
                columns: new[] { "IsLive", "ExpiresUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventEmailPicks_TokenHash",
                table: "HostedEventEmailPicks",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_HostedEventEmailPicks_OneLivePerAddress",
                table: "HostedEventEmailPicks",
                columns: new[] { "HostedEventId", "Email" },
                unique: true,
                filter: "[IsLive] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HostedEventEmailPickPlaces");

            migrationBuilder.DropTable(
                name: "HostedEventEmailPicks");

            migrationBuilder.DropColumn(
                name: "ContactPhone",
                table: "HostedEventBookings");

            migrationBuilder.DropColumn(
                name: "FirstName",
                table: "EventAttendanceInvites");

            migrationBuilder.DropColumn(
                name: "LastName",
                table: "EventAttendanceInvites");

            migrationBuilder.DropColumn(
                name: "Phone",
                table: "EventAttendanceInvites");
        }
    }
}
