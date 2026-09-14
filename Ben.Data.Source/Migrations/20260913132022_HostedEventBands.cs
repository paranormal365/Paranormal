using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <summary>
    /// What a party wears, so a steward knows by glance (item 235 phase 7).
    /// </summary>
    /// <remarks>
    /// <para>Ben, 2026-09-13: <i>"the organizer gets coloured wrist bands which mean different
    /// things — blue could be the full event with food, purple the full event, red is day
    /// one."</i></para>
    ///
    /// <para><b>Additive throughout.</b> A new table, and one nullable column on the bookings —
    /// null everywhere, which means "work the band out from the rules". Nothing existing is
    /// altered and nothing is backfilled, so an event with no bands behaves exactly as it did
    /// yesterday.</para>
    ///
    /// <para><b>The booking's link is NoAction and not SetNull</b>, because SQL Server refuses the
    /// second: bands cascade from the event and so do bookings, which is two paths to the same
    /// rows. Deleting a band clears the column off every booking first, in the endpoint.</para>
    /// </remarks>
    public partial class HostedEventBands : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "HostedEventBandId",
                table: "HostedEventBookings",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "HostedEventBands",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HostedEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Colour = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Meaning = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Hex = table.Column<string>(type: "nvarchar(9)", maxLength: 9, nullable: true),
                    Rule = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostedEventBands", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HostedEventBands_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_HostedEventBands_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_HostedEventBands_HostedEvents_HostedEventId",
                        column: x => x.HostedEventId,
                        principalTable: "HostedEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventBookings_HostedEventBandId",
                table: "HostedEventBookings",
                column: "HostedEventBandId");

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventBands_CreatedByAppUserId",
                table: "HostedEventBands",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventBands_HostedEventId",
                table: "HostedEventBands",
                column: "HostedEventId");

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventBands_UpdatedByAppUserId",
                table: "HostedEventBands",
                column: "UpdatedByAppUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_HostedEventBookings_HostedEventBands_HostedEventBandId",
                table: "HostedEventBookings",
                column: "HostedEventBandId",
                principalTable: "HostedEventBands",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_HostedEventBookings_HostedEventBands_HostedEventBandId",
                table: "HostedEventBookings");

            migrationBuilder.DropTable(
                name: "HostedEventBands");

            migrationBuilder.DropIndex(
                name: "IX_HostedEventBookings_HostedEventBandId",
                table: "HostedEventBookings");

            migrationBuilder.DropColumn(
                name: "HostedEventBandId",
                table: "HostedEventBookings");
        }
    }
}
