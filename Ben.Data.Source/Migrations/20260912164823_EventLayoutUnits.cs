using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <summary>
    /// Rooms become layout units, so an event can allocate seats as well (item 235 phase 2.4).
    /// </summary>
    /// <remarks>
    /// <para><b>Hand-written as a rename, not a drop and create.</b> The scaffolded version would
    /// have dropped <c>HostedEventRooms</c> and recreated it under the new name, taking every
    /// offered room and — through the booking-night column — every confirmed party's room with it.
    /// Nothing is live on this table yet, but a migration that is destructive when it happens to
    /// run against data is destructive, and this one runs on a test database that has bookings in
    /// it.</para>
    ///
    /// <para>The one genuinely new piece of work is remapping <c>HostedEventBookingNights</c>: it
    /// held the VENUE's room id and must now hold the EVENT's unit id, which is a join through the
    /// unit table rather than a rename.</para>
    /// </remarks>
    public partial class EventLayoutUnits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── The table, renamed with its rows ─────────────────────────────
            migrationBuilder.DropForeignKey(
                name: "FK_HostedEventBookingNights_PlaceRooms_PlaceRoomId",
                table: "HostedEventBookingNights");

            migrationBuilder.RenameTable(
                name: "HostedEventRooms",
                newName: "HostedEventLayoutUnits");

            migrationBuilder.RenameColumn(
                name: "CapacityOverride",
                table: "HostedEventLayoutUnits",
                newName: "Capacity");

            // Indexes and keys carry the old table's name until they are renamed too, which makes
            // every later migration's error messages talk about a table that no longer exists.
            migrationBuilder.RenameIndex(
                name: "IX_HostedEventRooms_HostedEventId_PlaceRoomId",
                table: "HostedEventLayoutUnits",
                newName: "IX_HostedEventLayoutUnits_HostedEventId_PlaceRoomId");
            migrationBuilder.RenameIndex(
                name: "IX_HostedEventRooms_PlaceRoomId",
                table: "HostedEventLayoutUnits",
                newName: "IX_HostedEventLayoutUnits_PlaceRoomId");
            migrationBuilder.RenameIndex(
                name: "IX_HostedEventRooms_CreatedByAppUserId",
                table: "HostedEventLayoutUnits",
                newName: "IX_HostedEventLayoutUnits_CreatedByAppUserId");
            migrationBuilder.RenameIndex(
                name: "IX_HostedEventRooms_UpdatedByAppUserId",
                table: "HostedEventLayoutUnits",
                newName: "IX_HostedEventLayoutUnits_UpdatedByAppUserId");

            // ── What a seat needs that a room never did ──────────────────────
            // PlaceRoomId becomes optional: a seat is not one of the venue's described rooms.
            // The unique index has to go first — it is filtered afterwards, because the rule is
            // only about rooms and an unfiltered one would allow exactly one seat to exist.
            migrationBuilder.DropIndex(
                name: "IX_HostedEventLayoutUnits_HostedEventId_PlaceRoomId",
                table: "HostedEventLayoutUnits");

            migrationBuilder.AlterColumn<Guid>(
                name: "PlaceRoomId",
                table: "HostedEventLayoutUnits",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<string>(
                name: "Label",
                table: "HostedEventLayoutUnits",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Section",
                table: "HostedEventLayoutUnits",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Price",
                table: "HostedEventLayoutUnits",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LayoutRow",
                table: "HostedEventLayoutUnits",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LayoutColumn",
                table: "HostedEventLayoutUnits",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventLayoutUnits_HostedEventId_PlaceRoomId",
                table: "HostedEventLayoutUnits",
                columns: new[] { "HostedEventId", "PlaceRoomId" },
                unique: true,
                filter: "[PlaceRoomId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventLayoutUnits_HostedEventId_SortOrder",
                table: "HostedEventLayoutUnits",
                columns: new[] { "HostedEventId", "SortOrder" });

            // ── The event says which kind of plan it has ─────────────────────
            // 1 is Rooms, and every event that exists today is one — nothing else could be built.
            migrationBuilder.AddColumn<int>(
                name: "LayoutKind",
                table: "HostedEvents",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<decimal>(
                name: "DayPassPrice",
                table: "HostedEvents",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            // ── The booking night points at the unit, not the venue's room ───
            // Added, filled by joining through the unit table, and only then swapped in. Renaming
            // the column would have left every row holding a PlaceRoom id in a column that now
            // means something else — the same number pointing at the wrong table, which is the
            // kind of corruption nothing downstream would notice until a guest was sent to a room
            // somebody else was asleep in.
            migrationBuilder.AddColumn<Guid>(
                name: "HostedEventLayoutUnitId",
                table: "HostedEventBookingNights",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE n
                   SET n.HostedEventLayoutUnitId = u.Id
                  FROM HostedEventBookingNights n
                  JOIN HostedEventBookings b ON b.Id = n.HostedEventBookingId
                  JOIN HostedEventLayoutUnits u
                    ON u.HostedEventId = b.HostedEventId
                   AND u.PlaceRoomId   = n.PlaceRoomId;
                """);

            migrationBuilder.DropIndex(
                name: "IX_HostedEventBookingNights_HostedEventNightId_PlaceRoomId",
                table: "HostedEventBookingNights");
            migrationBuilder.DropIndex(
                name: "IX_HostedEventBookingNights_PlaceRoomId",
                table: "HostedEventBookingNights");

            migrationBuilder.DropColumn(
                name: "PlaceRoomId",
                table: "HostedEventBookingNights");

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventBookingNights_HostedEventNightId_HostedEventLayoutUnitId",
                table: "HostedEventBookingNights",
                columns: new[] { "HostedEventNightId", "HostedEventLayoutUnitId" });

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventBookingNights_HostedEventLayoutUnitId",
                table: "HostedEventBookingNights",
                column: "HostedEventLayoutUnitId");

            migrationBuilder.AddForeignKey(
                name: "FK_HostedEventBookingNights_HostedEventLayoutUnits_HostedEventLayoutUnitId",
                table: "HostedEventBookingNights",
                column: "HostedEventLayoutUnitId",
                principalTable: "HostedEventLayoutUnits",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Undoes only what Up did, in reverse, and puts the room ids back by the same join.
            migrationBuilder.DropForeignKey(
                name: "FK_HostedEventBookingNights_HostedEventLayoutUnits_HostedEventLayoutUnitId",
                table: "HostedEventBookingNights");
            migrationBuilder.DropIndex(
                name: "IX_HostedEventBookingNights_HostedEventNightId_HostedEventLayoutUnitId",
                table: "HostedEventBookingNights");
            migrationBuilder.DropIndex(
                name: "IX_HostedEventBookingNights_HostedEventLayoutUnitId",
                table: "HostedEventBookingNights");

            migrationBuilder.AddColumn<Guid>(
                name: "PlaceRoomId",
                table: "HostedEventBookingNights",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: Guid.Empty);

            migrationBuilder.Sql("""
                UPDATE n
                   SET n.PlaceRoomId = u.PlaceRoomId
                  FROM HostedEventBookingNights n
                  JOIN HostedEventLayoutUnits u ON u.Id = n.HostedEventLayoutUnitId
                 WHERE u.PlaceRoomId IS NOT NULL;
                """);

            // A seat has no room to go back to, so a night holding one cannot survive the
            // downgrade. Deleted rather than left pointing at an empty guid, which would look like
            // a booking in a room that does not exist.
            migrationBuilder.Sql("""
                DELETE FROM HostedEventBookingNights WHERE PlaceRoomId = '00000000-0000-0000-0000-000000000000';
                """);

            migrationBuilder.DropColumn(
                name: "HostedEventLayoutUnitId",
                table: "HostedEventBookingNights");

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventBookingNights_HostedEventNightId_PlaceRoomId",
                table: "HostedEventBookingNights",
                columns: new[] { "HostedEventNightId", "PlaceRoomId" });
            migrationBuilder.CreateIndex(
                name: "IX_HostedEventBookingNights_PlaceRoomId",
                table: "HostedEventBookingNights",
                column: "PlaceRoomId");
            migrationBuilder.AddForeignKey(
                name: "FK_HostedEventBookingNights_PlaceRooms_PlaceRoomId",
                table: "HostedEventBookingNights",
                column: "PlaceRoomId",
                principalTable: "PlaceRooms",
                principalColumn: "Id");

            migrationBuilder.DropColumn(name: "DayPassPrice", table: "HostedEvents");
            migrationBuilder.DropColumn(name: "LayoutKind", table: "HostedEvents");

            migrationBuilder.DropIndex(
                name: "IX_HostedEventLayoutUnits_HostedEventId_SortOrder",
                table: "HostedEventLayoutUnits");
            migrationBuilder.DropIndex(
                name: "IX_HostedEventLayoutUnits_HostedEventId_PlaceRoomId",
                table: "HostedEventLayoutUnits");

            // Seats cannot exist in the old shape at all.
            migrationBuilder.Sql("DELETE FROM HostedEventLayoutUnits WHERE PlaceRoomId IS NULL;");

            migrationBuilder.DropColumn(name: "LayoutColumn", table: "HostedEventLayoutUnits");
            migrationBuilder.DropColumn(name: "LayoutRow", table: "HostedEventLayoutUnits");
            migrationBuilder.DropColumn(name: "Price", table: "HostedEventLayoutUnits");
            migrationBuilder.DropColumn(name: "Section", table: "HostedEventLayoutUnits");
            migrationBuilder.DropColumn(name: "Label", table: "HostedEventLayoutUnits");

            migrationBuilder.AlterColumn<Guid>(
                name: "PlaceRoomId",
                table: "HostedEventLayoutUnits",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: Guid.Empty,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.RenameIndex(
                name: "IX_HostedEventLayoutUnits_UpdatedByAppUserId",
                table: "HostedEventLayoutUnits",
                newName: "IX_HostedEventRooms_UpdatedByAppUserId");
            migrationBuilder.RenameIndex(
                name: "IX_HostedEventLayoutUnits_CreatedByAppUserId",
                table: "HostedEventLayoutUnits",
                newName: "IX_HostedEventRooms_CreatedByAppUserId");
            migrationBuilder.RenameIndex(
                name: "IX_HostedEventLayoutUnits_PlaceRoomId",
                table: "HostedEventLayoutUnits",
                newName: "IX_HostedEventRooms_PlaceRoomId");

            migrationBuilder.RenameColumn(
                name: "Capacity",
                table: "HostedEventLayoutUnits",
                newName: "CapacityOverride");

            migrationBuilder.RenameTable(
                name: "HostedEventLayoutUnits",
                newName: "HostedEventRooms");

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventRooms_HostedEventId_PlaceRoomId",
                table: "HostedEventRooms",
                columns: new[] { "HostedEventId", "PlaceRoomId" },
                unique: true);
        }
    }
}
