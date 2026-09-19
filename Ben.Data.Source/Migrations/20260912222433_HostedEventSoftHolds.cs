using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <summary>
    /// Soft holds, a party split across rooms, and the index that arbitrates them
    /// (item 235 phase 4).
    /// </summary>
    /// <remarks>
    /// <para><b>The structure is EF's; the data steps are not.</b> Three unique indexes are created
    /// here over columns that already have rows in them, and a unique index built over data that
    /// violates it fails the whole migration with a message naming an index rather than a problem.
    /// So the rows are put right first, and where they cannot be, this stops with a sentence
    /// somebody can act on.</para>
    ///
    /// <para><b>It refuses rather than deletes.</b> If two live parties already hold the same room
    /// on the same night, that is a real double-booking somebody has to decide about — which party
    /// keeps the room is not a question a migration may answer by picking one. It raises, the
    /// transaction rolls back, and nothing is lost.</para>
    /// </remarks>
    public partial class HostedEventSoftHolds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_HostedEventBookingNights_HostedEventBookingId_HostedEventNightId",
                table: "HostedEventBookingNights");

            migrationBuilder.DropIndex(
                name: "IX_HostedEventBookingNights_HostedEventNightId_HostedEventLayoutUnitId",
                table: "HostedEventBookingNights");

            migrationBuilder.AddColumn<DateTime>(
                name: "HoldExpiresUtc",
                table: "HostedEventBookings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "People",
                table: "HostedEventBookingNights",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReleasedUtc",
                table: "HostedEventBookingNights",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "HostedEventUnitBlocks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HostedEventLayoutUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HostedEventNightId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostedEventUnitBlocks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HostedEventUnitBlocks_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_HostedEventUnitBlocks_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_HostedEventUnitBlocks_HostedEventLayoutUnits_HostedEventLayoutUnitId",
                        column: x => x.HostedEventLayoutUnitId,
                        principalTable: "HostedEventLayoutUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_HostedEventUnitBlocks_HostedEventNights_HostedEventNightId",
                        column: x => x.HostedEventNightId,
                        principalTable: "HostedEventNights",
                        principalColumn: "Id");
                });

            // ── put the rows right before any unique index is built over them ────
            //
            // A night belonging to a party that was turned down or cancelled is history, not a
            // holding. Without this they would go on occupying their unit for ever: the filtered
            // index below treats a null ReleasedUtc as "still held", so a room turned down last
            // month could never be offered to anybody again.
            migrationBuilder.Sql(@"
                UPDATE n
                SET n.[ReleasedUtc] = COALESCE(b.[DecidedUtc], b.[DateUpdated], b.[DateCreated])
                FROM [HostedEventBookingNights] n
                INNER JOIN [HostedEventBookings] b ON b.[Id] = n.[HostedEventBookingId]
                WHERE n.[ReleasedUtc] IS NULL AND b.[Status] IN (2, 3);");

            // Now check what is left, and stop rather than guess.
            //
            // Two live parties in one room on one night is a genuine double-booking that predates
            // the index meant to prevent it. Which of them keeps the room is the venue's decision
            // and nobody else's, so this raises and rolls back with the fix in the message.
            migrationBuilder.Sql(@"
                IF EXISTS (
                    SELECT 1
                    FROM [HostedEventBookingNights]
                    WHERE [HostedEventLayoutUnitId] IS NOT NULL AND [ReleasedUtc] IS NULL
                    GROUP BY [HostedEventNightId], [HostedEventLayoutUnitId]
                    HAVING COUNT(*) > 1)
                BEGIN
                    THROW 50235, 'Two or more live bookings already share a room or seat on the same night. This migration will not choose between them: release or turn down all but one of each, then run it again.', 1;
                END");

            // And the same question for the one-live-booking-per-person rule.
            migrationBuilder.Sql(@"
                IF EXISTS (
                    SELECT 1 FROM [HostedEventBookings]
                    WHERE [Status] IN (0, 1, 4)
                    GROUP BY [HostedEventId], [LeadAppUserId]
                    HAVING COUNT(*) > 1)
                BEGIN
                    THROW 50236, 'Somebody already has two live bookings on one event. A party asking twice is one party. Turn down or cancel all but one of each, then run this again.', 1;
                END");
            migrationBuilder.CreateIndex(
                name: "UX_HostedEventBookings_OneLivePerLead",
                table: "HostedEventBookings",
                columns: new[] { "HostedEventId", "LeadAppUserId" },
                unique: true,
                filter: "[Status] IN (0, 1, 4)");

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventBookingNights_HostedEventBookingId_HostedEventNightId_HostedEventLayoutUnitId",
                table: "HostedEventBookingNights",
                columns: new[] { "HostedEventBookingId", "HostedEventNightId", "HostedEventLayoutUnitId" },
                unique: true,
                filter: "[HostedEventLayoutUnitId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventBookingNights_UnitNightHistory",
                table: "HostedEventBookingNights",
                columns: new[] { "HostedEventNightId", "HostedEventLayoutUnitId", "ReleasedUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_HostedEventBookingNights_LiveUnitNight",
                table: "HostedEventBookingNights",
                columns: new[] { "HostedEventNightId", "HostedEventLayoutUnitId" },
                unique: true,
                filter: "[HostedEventLayoutUnitId] IS NOT NULL AND [ReleasedUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventUnitBlocks_CreatedByAppUserId",
                table: "HostedEventUnitBlocks",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventUnitBlocks_HostedEventLayoutUnitId",
                table: "HostedEventUnitBlocks",
                column: "HostedEventLayoutUnitId",
                unique: true,
                filter: "[HostedEventNightId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventUnitBlocks_HostedEventLayoutUnitId_HostedEventNightId",
                table: "HostedEventUnitBlocks",
                columns: new[] { "HostedEventLayoutUnitId", "HostedEventNightId" },
                unique: true,
                filter: "[HostedEventNightId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventUnitBlocks_HostedEventNightId",
                table: "HostedEventUnitBlocks",
                column: "HostedEventNightId");

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventUnitBlocks_UpdatedByAppUserId",
                table: "HostedEventUnitBlocks",
                column: "UpdatedByAppUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HostedEventUnitBlocks");

            migrationBuilder.DropIndex(
                name: "UX_HostedEventBookings_OneLivePerLead",
                table: "HostedEventBookings");

            migrationBuilder.DropIndex(
                name: "IX_HostedEventBookingNights_HostedEventBookingId_HostedEventNightId_HostedEventLayoutUnitId",
                table: "HostedEventBookingNights");

            migrationBuilder.DropIndex(
                name: "IX_HostedEventBookingNights_UnitNightHistory",
                table: "HostedEventBookingNights");

            migrationBuilder.DropIndex(
                name: "UX_HostedEventBookingNights_LiveUnitNight",
                table: "HostedEventBookingNights");

            migrationBuilder.DropColumn(
                name: "HoldExpiresUtc",
                table: "HostedEventBookings");

            migrationBuilder.DropColumn(
                name: "People",
                table: "HostedEventBookingNights");

            migrationBuilder.DropColumn(
                name: "ReleasedUtc",
                table: "HostedEventBookingNights");

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventBookingNights_HostedEventBookingId_HostedEventNightId",
                table: "HostedEventBookingNights",
                columns: new[] { "HostedEventBookingId", "HostedEventNightId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventBookingNights_HostedEventNightId_HostedEventLayoutUnitId",
                table: "HostedEventBookingNights",
                columns: new[] { "HostedEventNightId", "HostedEventLayoutUnitId" });
        }
    }
}
