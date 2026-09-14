using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <summary>
    /// Who is told about bookings, and how far they have been told (item 235 phase 8).
    /// </summary>
    /// <remarks>
    /// Two new tables and nothing else. No row in either means the loud default — letters as
    /// bookings arrive — and the first pass after this applies tells nobody about anything older
    /// than an hour, so switching the feature on does not post a venue its whole back catalogue.
    /// </remarks>
    public partial class EventBookingAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EventBookingAlertPreferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Mode = table.Column<int>(type: "int", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventBookingAlertPreferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventBookingAlertPreferences_AppUsers_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EventBookingAlertPreferences_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EventBookingAlertStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HostedEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LastAlertUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AlertsCoverUpToUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastDigestUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventBookingAlertStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventBookingAlertStates_AppUsers_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EventBookingAlertStates_HostedEvents_HostedEventId",
                        column: x => x.HostedEventId,
                        principalTable: "HostedEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EventBookingAlertPreferences_AppUserId_OrganizationId",
                table: "EventBookingAlertPreferences",
                columns: new[] { "AppUserId", "OrganizationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EventBookingAlertPreferences_OrganizationId",
                table: "EventBookingAlertPreferences",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_EventBookingAlertStates_AppUserId_HostedEventId",
                table: "EventBookingAlertStates",
                columns: new[] { "AppUserId", "HostedEventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EventBookingAlertStates_HostedEventId",
                table: "EventBookingAlertStates",
                column: "HostedEventId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EventBookingAlertPreferences");

            migrationBuilder.DropTable(
                name: "EventBookingAlertStates");
        }
    }
}
