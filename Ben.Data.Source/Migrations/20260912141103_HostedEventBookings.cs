using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class HostedEventBookings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BedNote",
                table: "PlaceRooms",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Capacity",
                table: "PlaceRooms",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsBookable",
                table: "PlaceRooms",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "BookingsCloseAtUtc",
                table: "HostedEvents",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "HostedEventBookings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HostedEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeadAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PartySize = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    DecidedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DecidedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DecisionNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    GuestAcknowledgedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    UmbrellaAttendeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostedEventBookings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HostedEventBookings_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_HostedEventBookings_AppUsers_DecidedByAppUserId",
                        column: x => x.DecidedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_HostedEventBookings_AppUsers_LeadAppUserId",
                        column: x => x.LeadAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_HostedEventBookings_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_HostedEventBookings_HostedEvents_HostedEventId",
                        column: x => x.HostedEventId,
                        principalTable: "HostedEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HostedEventMenus",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HostedEventNightId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    ServedAtLocal = table.Column<TimeSpan>(type: "time", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostedEventMenus", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HostedEventMenus_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_HostedEventMenus_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_HostedEventMenus_HostedEventNights_HostedEventNightId",
                        column: x => x.HostedEventNightId,
                        principalTable: "HostedEventNights",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HostedEventRooms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HostedEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlaceRoomId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CapacityOverride = table.Column<int>(type: "int", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostedEventRooms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HostedEventRooms_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_HostedEventRooms_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_HostedEventRooms_HostedEvents_HostedEventId",
                        column: x => x.HostedEventId,
                        principalTable: "HostedEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_HostedEventRooms_PlaceRooms_PlaceRoomId",
                        column: x => x.PlaceRoomId,
                        principalTable: "PlaceRooms",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "HostedEventBookingGuests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HostedEventBookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    AppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DietaryNotes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostedEventBookingGuests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HostedEventBookingGuests_AppUsers_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_HostedEventBookingGuests_HostedEventBookings_HostedEventBookingId",
                        column: x => x.HostedEventBookingId,
                        principalTable: "HostedEventBookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HostedEventBookingNights",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HostedEventBookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HostedEventNightId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlaceRoomId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostedEventBookingNights", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HostedEventBookingNights_HostedEventBookings_HostedEventBookingId",
                        column: x => x.HostedEventBookingId,
                        principalTable: "HostedEventBookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_HostedEventBookingNights_HostedEventNights_HostedEventNightId",
                        column: x => x.HostedEventNightId,
                        principalTable: "HostedEventNights",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_HostedEventBookingNights_PlaceRooms_PlaceRoomId",
                        column: x => x.PlaceRoomId,
                        principalTable: "PlaceRooms",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "HostedEventMenuItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HostedEventMenuId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Course = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DietaryTags = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostedEventMenuItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HostedEventMenuItems_HostedEventMenus_HostedEventMenuId",
                        column: x => x.HostedEventMenuId,
                        principalTable: "HostedEventMenus",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventBookingGuests_AppUserId",
                table: "HostedEventBookingGuests",
                column: "AppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventBookingGuests_HostedEventBookingId",
                table: "HostedEventBookingGuests",
                column: "HostedEventBookingId");

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventBookingNights_HostedEventBookingId_HostedEventNightId",
                table: "HostedEventBookingNights",
                columns: new[] { "HostedEventBookingId", "HostedEventNightId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventBookingNights_HostedEventNightId_PlaceRoomId",
                table: "HostedEventBookingNights",
                columns: new[] { "HostedEventNightId", "PlaceRoomId" });

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventBookingNights_PlaceRoomId",
                table: "HostedEventBookingNights",
                column: "PlaceRoomId");

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventBookings_CreatedByAppUserId",
                table: "HostedEventBookings",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventBookings_DecidedByAppUserId",
                table: "HostedEventBookings",
                column: "DecidedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventBookings_HostedEventId_Status",
                table: "HostedEventBookings",
                columns: new[] { "HostedEventId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventBookings_LeadAppUserId_Status",
                table: "HostedEventBookings",
                columns: new[] { "LeadAppUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventBookings_UpdatedByAppUserId",
                table: "HostedEventBookings",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventMenuItems_HostedEventMenuId",
                table: "HostedEventMenuItems",
                column: "HostedEventMenuId");

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventMenus_CreatedByAppUserId",
                table: "HostedEventMenus",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventMenus_HostedEventNightId",
                table: "HostedEventMenus",
                column: "HostedEventNightId");

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventMenus_UpdatedByAppUserId",
                table: "HostedEventMenus",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventRooms_CreatedByAppUserId",
                table: "HostedEventRooms",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventRooms_HostedEventId_PlaceRoomId",
                table: "HostedEventRooms",
                columns: new[] { "HostedEventId", "PlaceRoomId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventRooms_PlaceRoomId",
                table: "HostedEventRooms",
                column: "PlaceRoomId");

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventRooms_UpdatedByAppUserId",
                table: "HostedEventRooms",
                column: "UpdatedByAppUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HostedEventBookingGuests");

            migrationBuilder.DropTable(
                name: "HostedEventBookingNights");

            migrationBuilder.DropTable(
                name: "HostedEventMenuItems");

            migrationBuilder.DropTable(
                name: "HostedEventRooms");

            migrationBuilder.DropTable(
                name: "HostedEventBookings");

            migrationBuilder.DropTable(
                name: "HostedEventMenus");

            migrationBuilder.DropColumn(
                name: "BedNote",
                table: "PlaceRooms");

            migrationBuilder.DropColumn(
                name: "Capacity",
                table: "PlaceRooms");

            migrationBuilder.DropColumn(
                name: "IsBookable",
                table: "PlaceRooms");

            migrationBuilder.DropColumn(
                name: "BookingsCloseAtUtc",
                table: "HostedEvents");
        }
    }
}
