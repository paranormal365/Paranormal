using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <summary>
    /// Who actually walked in, night by night (item 235 phase 7).
    /// </summary>
    /// <remarks>
    /// <para><b>A new table and nothing else</b> — no column altered, nothing backfilled. The old
    /// single stamp on the pass stays exactly where it is: it is what a scan wrote before tonight
    /// existed as a concept, and rewriting history into per-night rows would be inventing nights
    /// somebody may never have come to.</para>
    ///
    /// <para><b>Unique on (booking, night)</b>, because a second scan of the same party on the
    /// same night is the same arrival. A door that recorded two would double every count fed from
    /// this table, and the count is what a fire officer asks for.</para>
    /// </remarks>
    public partial class HostedEventCheckIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HostedEventCheckIns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HostedEventBookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HostedEventNightId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ArrivedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LeftUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    People = table.Column<int>(type: "int", nullable: true),
                    Method = table.Column<int>(type: "int", nullable: false),
                    RecordedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostedEventCheckIns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HostedEventCheckIns_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_HostedEventCheckIns_AppUsers_RecordedByAppUserId",
                        column: x => x.RecordedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_HostedEventCheckIns_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_HostedEventCheckIns_HostedEventBookings_HostedEventBookingId",
                        column: x => x.HostedEventBookingId,
                        principalTable: "HostedEventBookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_HostedEventCheckIns_HostedEventNights_HostedEventNightId",
                        column: x => x.HostedEventNightId,
                        principalTable: "HostedEventNights",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventCheckIns_CreatedByAppUserId",
                table: "HostedEventCheckIns",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventCheckIns_HostedEventBookingId_HostedEventNightId",
                table: "HostedEventCheckIns",
                columns: new[] { "HostedEventBookingId", "HostedEventNightId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventCheckIns_HostedEventNightId",
                table: "HostedEventCheckIns",
                column: "HostedEventNightId");

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventCheckIns_RecordedByAppUserId",
                table: "HostedEventCheckIns",
                column: "RecordedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventCheckIns_UpdatedByAppUserId",
                table: "HostedEventCheckIns",
                column: "UpdatedByAppUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HostedEventCheckIns");
        }
    }
}
