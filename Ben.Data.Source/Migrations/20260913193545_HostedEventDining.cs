using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <summary>
    /// Item 235 phase 13: dining tables, and which confirmed parties sit at them at each sitting.
    /// </summary>
    public partial class HostedEventDining : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HostedEventDiningTables",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HostedEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Seats = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostedEventDiningTables", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HostedEventDiningTables_HostedEvents_HostedEventId",
                        column: x => x.HostedEventId,
                        principalTable: "HostedEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HostedEventDiningSeats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HostedEventMenuId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HostedEventDiningTableId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HostedEventBookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    People = table.Column<int>(type: "int", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostedEventDiningSeats", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HostedEventDiningSeats_HostedEventBookings_HostedEventBookingId",
                        column: x => x.HostedEventBookingId,
                        principalTable: "HostedEventBookings",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_HostedEventDiningSeats_HostedEventDiningTables_HostedEventDiningTableId",
                        column: x => x.HostedEventDiningTableId,
                        principalTable: "HostedEventDiningTables",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_HostedEventDiningSeats_HostedEventMenus_HostedEventMenuId",
                        column: x => x.HostedEventMenuId,
                        principalTable: "HostedEventMenus",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventDiningSeats_HostedEventBookingId",
                table: "HostedEventDiningSeats",
                column: "HostedEventBookingId");

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventDiningSeats_HostedEventDiningTableId",
                table: "HostedEventDiningSeats",
                column: "HostedEventDiningTableId");

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventDiningSeats_HostedEventMenuId_HostedEventBookingId_HostedEventDiningTableId",
                table: "HostedEventDiningSeats",
                columns: new[] { "HostedEventMenuId", "HostedEventBookingId", "HostedEventDiningTableId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventDiningTables_HostedEventId_SortOrder",
                table: "HostedEventDiningTables",
                columns: new[] { "HostedEventId", "SortOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HostedEventDiningSeats");

            migrationBuilder.DropTable(
                name: "HostedEventDiningTables");
        }
    }
}
