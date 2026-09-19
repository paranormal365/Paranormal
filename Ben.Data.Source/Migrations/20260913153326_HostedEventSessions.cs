using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <summary>
    /// The programme: sessions and who signed up (item 235 phase 10).
    /// </summary>
    /// <remarks>
    /// Two new tables and two nullable columns (<c>HostedEvents.ProgrammePublishedUtc</c>,
    /// <c>HostedEventBookings.ProgrammeSeenUtc</c>). Nothing existing changes, and no event has a
    /// published programme until its host publishes one.
    /// </remarks>
    public partial class HostedEventSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ProgrammePublishedUtc",
                table: "HostedEvents",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProgrammeSeenUtc",
                table: "HostedEventBookings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "HostedEventSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HostedEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    StartsAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndsAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PlaceRoomId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LocationText = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    LedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Capacity = table.Column<int>(type: "int", nullable: true),
                    RequiresSignUp = table.Column<bool>(type: "bit", nullable: false),
                    PlacesTaken = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CalledOffUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ChangedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostedEventSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HostedEventSessions_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_HostedEventSessions_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_HostedEventSessions_HostedEvents_HostedEventId",
                        column: x => x.HostedEventId,
                        principalTable: "HostedEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_HostedEventSessions_PlaceRooms_PlaceRoomId",
                        column: x => x.PlaceRoomId,
                        principalTable: "PlaceRooms",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "HostedEventSessionSignUps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HostedEventSessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HostedEventBookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    People = table.Column<int>(type: "int", nullable: false),
                    WaitlistedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PromotedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostedEventSessionSignUps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HostedEventSessionSignUps_AppUsers_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_HostedEventSessionSignUps_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_HostedEventSessionSignUps_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_HostedEventSessionSignUps_HostedEventBookings_HostedEventBookingId",
                        column: x => x.HostedEventBookingId,
                        principalTable: "HostedEventBookings",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_HostedEventSessionSignUps_HostedEventSessions_HostedEventSessionId",
                        column: x => x.HostedEventSessionId,
                        principalTable: "HostedEventSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventSessions_CreatedByAppUserId",
                table: "HostedEventSessions",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventSessions_HostedEventId_StartsAtUtc",
                table: "HostedEventSessions",
                columns: new[] { "HostedEventId", "StartsAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventSessions_PlaceRoomId",
                table: "HostedEventSessions",
                column: "PlaceRoomId");

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventSessions_UpdatedByAppUserId",
                table: "HostedEventSessions",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventSessionSignUps_AppUserId",
                table: "HostedEventSessionSignUps",
                column: "AppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventSessionSignUps_CreatedByAppUserId",
                table: "HostedEventSessionSignUps",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventSessionSignUps_HostedEventBookingId",
                table: "HostedEventSessionSignUps",
                column: "HostedEventBookingId");

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventSessionSignUps_HostedEventSessionId_AppUserId",
                table: "HostedEventSessionSignUps",
                columns: new[] { "HostedEventSessionId", "AppUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventSessionSignUps_UpdatedByAppUserId",
                table: "HostedEventSessionSignUps",
                column: "UpdatedByAppUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HostedEventSessionSignUps");

            migrationBuilder.DropTable(
                name: "HostedEventSessions");

            migrationBuilder.DropColumn(
                name: "ProgrammePublishedUtc",
                table: "HostedEvents");

            migrationBuilder.DropColumn(
                name: "ProgrammeSeenUtc",
                table: "HostedEventBookings");
        }
    }
}
