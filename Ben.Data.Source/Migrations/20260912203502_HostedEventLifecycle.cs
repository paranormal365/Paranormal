using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <summary>
    /// One state column replaces a published flag and two timestamps (item 235 phase 3).
    /// </summary>
    /// <remarks>
    /// <para><b>Hand-written, because the scaffold was destructive.</b> EF matched the dropped
    /// <c>IsPublished</c> bit against the added <c>GoNoGoWeekReminderSent</c> bit and wrote a
    /// RENAME: every published event would have become one whose week reminder had been sent, and
    /// whether it was published would have been gone with no way back. It is a fair guess for a
    /// tool with only two column shapes to compare and completely wrong, which is why a migration
    /// that drops a column in this repository is read before it is run.</para>
    ///
    /// <para><b>The scaffold's second fault:</b> <c>HoldMinutes</c> was given a default of 0, and
    /// the check constraint added afterwards demands 15 to 20160. Every existing row would have
    /// failed it, so the whole migration would have rolled back on any database with an event in
    /// it. The default is the real one, 2880, and the constraint is added last.</para>
    ///
    /// <para><b>The order matters.</b> The state is backfilled while <c>IsPublished</c> still
    /// exists, and only then is the flag dropped. Doing it the other way round is how a migration
    /// silently turns every live event into a draft.</para>
    /// </remarks>
    public partial class HostedEventLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── the state, and the stamps that say when it became that ────────
            migrationBuilder.AddColumn<int>(
                name: "LifecycleState", table: "HostedEvents",
                type: "int", nullable: false, defaultValue: 0);

            migrationBuilder.AddColumn<System.DateTime>(
                name: "LiveAtUtc", table: "HostedEvents", type: "datetime2", nullable: true);

            migrationBuilder.AddColumn<System.DateTime>(
                name: "EndedAtUtc", table: "HostedEvents", type: "datetime2", nullable: true);

            // Cancelled beats archived beats published. An event that is both cancelled and filed
            // away is a cancelled one: that is the fact its organizer and its guests remember, and
            // the archive is only where it was put afterwards.
            migrationBuilder.Sql("""
                UPDATE [HostedEvents]
                SET [LifecycleState] =
                    CASE
                        WHEN [CancelledAtUtc] IS NOT NULL THEN 5
                        WHEN [ArchivedAtUtc]  IS NOT NULL THEN 4
                        WHEN [IsPublished]    = 1         THEN 1
                        ELSE 0
                    END;
                """);

            migrationBuilder.DropIndex(
                name: "IX_HostedEvents_OrganizationId_IsPublished_ArchivedAtUtc",
                table: "HostedEvents");

            migrationBuilder.DropColumn(name: "IsPublished", table: "HostedEvents");

            // ── how a guest gets a place ──────────────────────────────────────
            migrationBuilder.AddColumn<int>(
                name: "BookingMode", table: "HostedEvents",
                type: "int", nullable: false, defaultValue: 0);

            // 2880 minutes: two days. Zero would fail the check constraint below on every row
            // that already exists, which is what the scaffold would have done.
            migrationBuilder.AddColumn<int>(
                name: "HoldMinutes", table: "HostedEvents",
                type: "int", nullable: false, defaultValue: 2880);

            // ── who agreed this event may happen where it happens ─────────────
            migrationBuilder.AddColumn<int>(
                name: "VenueArrangement", table: "HostedEvents",
                type: "int", nullable: false, defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "VenueContactName", table: "HostedEvents",
                type: "nvarchar(160)", maxLength: 160, nullable: true);

            migrationBuilder.AddColumn<System.DateTime>(
                name: "VenueAgreedOnUtc", table: "HostedEvents", type: "datetime2", nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VenueReference", table: "HostedEvents",
                type: "nvarchar(120)", maxLength: 120, nullable: true);

            // ── minimum numbers, and the decision they force ──────────────────
            migrationBuilder.AddColumn<int>(
                name: "MinimumGuests", table: "HostedEvents", type: "int", nullable: true);

            migrationBuilder.AddColumn<System.DateTime>(
                name: "GoNoGoDeadlineUtc", table: "HostedEvents", type: "datetime2", nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GoNoGoDecision", table: "HostedEvents",
                type: "int", nullable: false, defaultValue: 0);

            migrationBuilder.AddColumn<System.DateTime>(
                name: "GoNoGoDecidedUtc", table: "HostedEvents", type: "datetime2", nullable: true);

            migrationBuilder.AddColumn<System.Guid>(
                name: "GoNoGoDecidedByAppUserId", table: "HostedEvents",
                type: "uniqueidentifier", nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "GoNoGoWeekReminderSent", table: "HostedEvents",
                type: "bit", nullable: false, defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "GoNoGoDayReminderSent", table: "HostedEvents",
                type: "bit", nullable: false, defaultValue: false);

            migrationBuilder.AddColumn<System.DateTime>(
                name: "LastDigestSentUtc", table: "HostedEvents", type: "datetime2", nullable: true);

            // ── the two questions the site asks constantly ────────────────────
            migrationBuilder.CreateIndex(
                name: "IX_HostedEvents_OrganizationId_LifecycleState",
                table: "HostedEvents",
                columns: ["OrganizationId", "LifecycleState"]);

            migrationBuilder.CreateIndex(
                name: "IX_HostedEvents_LifecycleState_StartsOn",
                table: "HostedEvents",
                columns: ["LifecycleState", "StartsOn"]);

            // Last, once every row has a legal HoldMinutes.
            migrationBuilder.AddCheckConstraint(
                name: "CK_HostedEvents_HoldMinutes",
                table: "HostedEvents",
                sql: "[HoldMinutes] BETWEEN 15 AND 20160");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_HostedEvents_HoldMinutes", table: "HostedEvents");

            migrationBuilder.DropIndex(
                name: "IX_HostedEvents_LifecycleState_StartsOn", table: "HostedEvents");

            migrationBuilder.DropIndex(
                name: "IX_HostedEvents_OrganizationId_LifecycleState", table: "HostedEvents");

            // The flag comes back before the state goes, for the same reason it went after.
            migrationBuilder.AddColumn<bool>(
                name: "IsPublished", table: "HostedEvents",
                type: "bit", nullable: false, defaultValue: false);

            // Published, Live and Ended were all "published" under the old shape. Cancelled and
            // withdrawn become their timestamps, which is as much as the old shape could say.
            migrationBuilder.Sql("""
                UPDATE [HostedEvents] SET [IsPublished] = 1 WHERE [LifecycleState] IN (1, 2, 3);
                UPDATE [HostedEvents]
                SET [CancelledAtUtc] = COALESCE([CancelledAtUtc], SYSUTCDATETIME())
                WHERE [LifecycleState] IN (5, 6);
                UPDATE [HostedEvents]
                SET [ArchivedAtUtc] = COALESCE([ArchivedAtUtc], SYSUTCDATETIME())
                WHERE [LifecycleState] = 4;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_HostedEvents_OrganizationId_IsPublished_ArchivedAtUtc",
                table: "HostedEvents",
                columns: ["OrganizationId", "IsPublished", "ArchivedAtUtc"]);

            foreach (var column in new[]
                     {
                         "LifecycleState", "LiveAtUtc", "EndedAtUtc",
                         "BookingMode", "HoldMinutes",
                         "VenueArrangement", "VenueContactName", "VenueAgreedOnUtc", "VenueReference",
                         "MinimumGuests", "GoNoGoDeadlineUtc", "GoNoGoDecision", "GoNoGoDecidedUtc",
                         "GoNoGoDecidedByAppUserId", "GoNoGoWeekReminderSent", "GoNoGoDayReminderSent",
                         "LastDigestSentUtc",
                     })
            {
                migrationBuilder.DropColumn(name: column, table: "HostedEvents");
            }
        }
    }
}
