using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <summary>
    /// Item 235 phase 17b: IsHaunted removing a hosted event, and the organizer's appeal. One new table; the new
    /// lifecycle value (Removed = 7) is stored in the existing int column and needs no change.
    /// </summary>
    public partial class HostedEventRemovals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HostedEventRemovals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HostedEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PreviousState = table.Column<int>(type: "int", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    RemovedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RemovedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreditReturned = table.Column<bool>(type: "bit", nullable: false),
                    GuestsTold = table.Column<int>(type: "int", nullable: false),
                    AppealState = table.Column<int>(type: "int", nullable: false),
                    AppealMessage = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    AppealedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AppealedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DecisionNote = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    DecidedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DecidedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostedEventRemovals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HostedEventRemovals_AppUsers_AppealedByAppUserId",
                        column: x => x.AppealedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_HostedEventRemovals_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_HostedEventRemovals_AppUsers_DecidedByAppUserId",
                        column: x => x.DecidedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_HostedEventRemovals_AppUsers_RemovedByAppUserId",
                        column: x => x.RemovedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_HostedEventRemovals_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_HostedEventRemovals_HostedEvents_HostedEventId",
                        column: x => x.HostedEventId,
                        principalTable: "HostedEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventRemovals_AppealedByAppUserId",
                table: "HostedEventRemovals",
                column: "AppealedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventRemovals_AppealState",
                table: "HostedEventRemovals",
                column: "AppealState");

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventRemovals_CreatedByAppUserId",
                table: "HostedEventRemovals",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventRemovals_DecidedByAppUserId",
                table: "HostedEventRemovals",
                column: "DecidedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventRemovals_HostedEventId_RemovedUtc",
                table: "HostedEventRemovals",
                columns: new[] { "HostedEventId", "RemovedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventRemovals_RemovedByAppUserId",
                table: "HostedEventRemovals",
                column: "RemovedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventRemovals_UpdatedByAppUserId",
                table: "HostedEventRemovals",
                column: "UpdatedByAppUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HostedEventRemovals");
        }
    }
}
