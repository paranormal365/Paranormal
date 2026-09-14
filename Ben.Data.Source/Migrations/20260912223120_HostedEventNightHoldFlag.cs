using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <summary>
    /// The night row carries whether it is holding, because the arbiter cannot join
    /// (item 235 phase 4).
    /// </summary>
    /// <remarks>
    /// <para>The unique index that stops two parties taking one room has to decide from the night
    /// row alone, and "is the booking that owns me holding anything" is not a question a SQL Server
    /// filter can ask. Without this column the index refused a second REQUEST for the same room —
    /// but on an Ask event a request names a preference and holds nothing, so twelve parties may
    /// all ask for the Blue Room and the venue picks one. The index would have closed the waiting
    /// list that Ask mode exists for.</para>
    ///
    /// <para>Backfilled from the parent's status here, and written only by
    /// <c>BookingTransitions</c> afterwards.</para>
    /// </remarks>
    public partial class HostedEventNightHoldFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_HostedEventBookingNights_LiveUnitNight",
                table: "HostedEventBookingNights");

            migrationBuilder.AddColumn<bool>(
                name: "IsHolding",
                table: "HostedEventBookingNights",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // True exactly where the parent booking is Held (4) or Confirmed (1) and the night has
            // not gone back. Everything else — requests, turned down, cancelled, expired — names a
            // room without holding it.
            migrationBuilder.Sql(@"
                UPDATE n
                SET n.[IsHolding] = CASE
                        WHEN b.[Status] IN (1, 4) AND n.[ReleasedUtc] IS NULL THEN 1
                        ELSE 0
                    END
                FROM [HostedEventBookingNights] n
                INNER JOIN [HostedEventBookings] b ON b.[Id] = n.[HostedEventBookingId];");

            // And now the same pre-check as before, against the narrower rule. Anything left is a
            // real double-booking and the venue decides which party keeps the room, not this.
            migrationBuilder.Sql(@"
                IF EXISTS (
                    SELECT 1
                    FROM [HostedEventBookingNights]
                    WHERE [IsHolding] = 1 AND [HostedEventLayoutUnitId] IS NOT NULL
                      AND [ReleasedUtc] IS NULL
                    GROUP BY [HostedEventNightId], [HostedEventLayoutUnitId]
                    HAVING COUNT(*) > 1)
                BEGIN
                    THROW 50237, 'Two or more live bookings already hold the same room or seat on the same night. Release or turn down all but one of each, then run this again.', 1;
                END");

            migrationBuilder.CreateIndex(
                name: "UX_HostedEventBookingNights_LiveUnitNight",
                table: "HostedEventBookingNights",
                columns: new[] { "HostedEventNightId", "HostedEventLayoutUnitId" },
                unique: true,
                filter: "[IsHolding] = 1 AND [HostedEventLayoutUnitId] IS NOT NULL AND [ReleasedUtc] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_HostedEventBookingNights_LiveUnitNight",
                table: "HostedEventBookingNights");

            migrationBuilder.DropColumn(
                name: "IsHolding",
                table: "HostedEventBookingNights");

            migrationBuilder.CreateIndex(
                name: "UX_HostedEventBookingNights_LiveUnitNight",
                table: "HostedEventBookingNights",
                columns: new[] { "HostedEventNightId", "HostedEventLayoutUnitId" },
                unique: true,
                filter: "[HostedEventLayoutUnitId] IS NOT NULL AND [ReleasedUtc] IS NULL");
        }
    }
}
