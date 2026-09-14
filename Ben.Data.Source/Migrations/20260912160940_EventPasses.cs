using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class EventPasses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HostedEventPasses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HostedEventBookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Token = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    IssuedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RevokedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ReissuedFromHostedEventPassId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EmailedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CheckedInUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CheckedInByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostedEventPasses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HostedEventPasses_AppUsers_CheckedInByAppUserId",
                        column: x => x.CheckedInByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_HostedEventPasses_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_HostedEventPasses_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_HostedEventPasses_HostedEventBookings_HostedEventBookingId",
                        column: x => x.HostedEventBookingId,
                        principalTable: "HostedEventBookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventPasses_CheckedInByAppUserId",
                table: "HostedEventPasses",
                column: "CheckedInByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventPasses_CreatedByAppUserId",
                table: "HostedEventPasses",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventPasses_HostedEventBookingId_IssuedUtc",
                table: "HostedEventPasses",
                columns: new[] { "HostedEventBookingId", "IssuedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventPasses_Token",
                table: "HostedEventPasses",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventPasses_UpdatedByAppUserId",
                table: "HostedEventPasses",
                column: "UpdatedByAppUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HostedEventPasses");
        }
    }
}
