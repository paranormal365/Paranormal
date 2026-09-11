using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class PollsSchedulingAndPlace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "PostedLatitude",
                table: "OrgMessages",
                type: "decimal(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PostedLongitude",
                table: "OrgMessages",
                type: "decimal(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PostedPlaceName",
                table: "OrgMessages",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ScheduledForUtc",
                table: "OrgMessages",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MessagePolls",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrgMessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Question = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    ClosesAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AllowMultiple = table.Column<bool>(type: "bit", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessagePolls", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MessagePolls_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MessagePolls_OrgMessages_OrgMessageId",
                        column: x => x.OrgMessageId,
                        principalTable: "OrgMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MessagePollOptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MessagePollId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessagePollOptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MessagePollOptions_MessagePolls_MessagePollId",
                        column: x => x.MessagePollId,
                        principalTable: "MessagePolls",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MessagePollVotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MessagePollId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MessagePollOptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessagePollVotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MessagePollVotes_AppUsers_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MessagePollVotes_MessagePollOptions_MessagePollOptionId",
                        column: x => x.MessagePollOptionId,
                        principalTable: "MessagePollOptions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MessagePollVotes_MessagePolls_MessagePollId",
                        column: x => x.MessagePollId,
                        principalTable: "MessagePolls",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrgMessages_ScheduledForUtc_DateCreated",
                table: "OrgMessages",
                columns: new[] { "ScheduledForUtc", "DateCreated" });

            migrationBuilder.CreateIndex(
                name: "IX_MessagePollOptions_MessagePollId",
                table: "MessagePollOptions",
                column: "MessagePollId");

            migrationBuilder.CreateIndex(
                name: "IX_MessagePolls_CreatedByAppUserId",
                table: "MessagePolls",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MessagePolls_OrgMessageId",
                table: "MessagePolls",
                column: "OrgMessageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MessagePollVotes_AppUserId",
                table: "MessagePollVotes",
                column: "AppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MessagePollVotes_MessagePollId_MessagePollOptionId_AppUserId",
                table: "MessagePollVotes",
                columns: new[] { "MessagePollId", "MessagePollOptionId", "AppUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MessagePollVotes_MessagePollOptionId",
                table: "MessagePollVotes",
                column: "MessagePollOptionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MessagePollVotes");

            migrationBuilder.DropTable(
                name: "MessagePollOptions");

            migrationBuilder.DropTable(
                name: "MessagePolls");

            migrationBuilder.DropIndex(
                name: "IX_OrgMessages_ScheduledForUtc_DateCreated",
                table: "OrgMessages");

            migrationBuilder.DropColumn(
                name: "PostedLatitude",
                table: "OrgMessages");

            migrationBuilder.DropColumn(
                name: "PostedLongitude",
                table: "OrgMessages");

            migrationBuilder.DropColumn(
                name: "PostedPlaceName",
                table: "OrgMessages");

            migrationBuilder.DropColumn(
                name: "ScheduledForUtc",
                table: "OrgMessages");
        }
    }
}
