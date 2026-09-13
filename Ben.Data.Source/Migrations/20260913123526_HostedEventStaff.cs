using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <summary>
    /// Who is helping at one event, and what they may do (item 235 phase 7).
    /// </summary>
    /// <remarks>
    /// <para><b>A new table and nothing else.</b> Nothing is altered, nothing is backfilled, and
    /// no existing row changes — the whole of it is one CREATE TABLE with its indexes, which is
    /// the only shape of migration that cannot lose anything.</para>
    ///
    /// <para><b>Two filtered unique indexes, not one.</b> A row is either a person (AppUserId) or
    /// an invitation to an address (Email), never both, and each must exist once per event.
    /// Without the second, inviting the same helper twice would leave two rows and revoking one
    /// would look as though it had worked.</para>
    ///
    /// <para>The event cascades: a helper at an event that no longer exists is nothing at all. The
    /// AppUser links are NoAction, because deleting a PERSON goes through its own purge and that
    /// purge has to see these rows rather than have them vanish underneath it.</para>
    /// </remarks>
    public partial class HostedEventStaff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HostedEventStaff",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HostedEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RoleLabel = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    SeesBookings = table.Column<bool>(type: "bit", nullable: false),
                    Decides = table.Column<bool>(type: "bit", nullable: false),
                    RunsTheDoor = table.Column<bool>(type: "bit", nullable: false),
                    SeesMenus = table.Column<bool>(type: "bit", nullable: false),
                    SeesFiles = table.Column<bool>(type: "bit", nullable: false),
                    Token = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DateExpires = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DateConfirmed = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostedEventStaff", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HostedEventStaff_AppUsers_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_HostedEventStaff_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_HostedEventStaff_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_HostedEventStaff_HostedEvents_HostedEventId",
                        column: x => x.HostedEventId,
                        principalTable: "HostedEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventStaff_AppUserId",
                table: "HostedEventStaff",
                column: "AppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventStaff_CreatedByAppUserId",
                table: "HostedEventStaff",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventStaff_HostedEventId_AppUserId",
                table: "HostedEventStaff",
                columns: new[] { "HostedEventId", "AppUserId" },
                unique: true,
                filter: "[AppUserId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventStaff_HostedEventId_Email",
                table: "HostedEventStaff",
                columns: new[] { "HostedEventId", "Email" },
                unique: true,
                filter: "[Email] IS NOT NULL AND [AppUserId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventStaff_Token",
                table: "HostedEventStaff",
                column: "Token",
                filter: "[Token] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventStaff_UpdatedByAppUserId",
                table: "HostedEventStaff",
                column: "UpdatedByAppUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HostedEventStaff");
        }
    }
}
