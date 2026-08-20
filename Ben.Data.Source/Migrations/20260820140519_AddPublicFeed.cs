using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddPublicFeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "HiddenByAppUserId",
                table: "OrgMessages",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "HiddenUtc",
                table: "OrgMessages",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OrgMessageHashtags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrgMessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Tag = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrgMessageHashtags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrgMessageHashtags_OrgMessages_OrgMessageId",
                        column: x => x.OrgMessageId,
                        principalTable: "OrgMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrgMessageMentions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrgMessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MentionedAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrgMessageMentions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrgMessageMentions_AppUsers_MentionedAppUserId",
                        column: x => x.MentionedAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrgMessageMentions_OrgMessages_OrgMessageId",
                        column: x => x.OrgMessageId,
                        principalTable: "OrgMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrgMessageReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrgMessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReportedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Outcome = table.Column<int>(type: "int", nullable: false),
                    ResolvedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResolvedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrgMessageReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrgMessageReports_AppUsers_ReportedByAppUserId",
                        column: x => x.ReportedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrgMessageReports_AppUsers_ResolvedByAppUserId",
                        column: x => x.ResolvedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrgMessageReports_OrgMessages_OrgMessageId",
                        column: x => x.OrgMessageId,
                        principalTable: "OrgMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserFollows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FollowerAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FollowedAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserFollows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserFollows_AppUsers_FollowedAppUserId",
                        column: x => x.FollowedAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UserFollows_AppUsers_FollowerAppUserId",
                        column: x => x.FollowerAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrgMessages_ChannelType_HiddenUtc_DateCreated",
                table: "OrgMessages",
                columns: new[] { "ChannelType", "HiddenUtc", "DateCreated" });

            migrationBuilder.CreateIndex(
                name: "IX_OrgMessages_HiddenByAppUserId",
                table: "OrgMessages",
                column: "HiddenByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrgMessageHashtags_OrgMessageId_Tag",
                table: "OrgMessageHashtags",
                columns: new[] { "OrgMessageId", "Tag" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrgMessageHashtags_Tag_DateCreated",
                table: "OrgMessageHashtags",
                columns: new[] { "Tag", "DateCreated" });

            migrationBuilder.CreateIndex(
                name: "IX_OrgMessageMentions_MentionedAppUserId_DateCreated",
                table: "OrgMessageMentions",
                columns: new[] { "MentionedAppUserId", "DateCreated" });

            migrationBuilder.CreateIndex(
                name: "IX_OrgMessageMentions_OrgMessageId_MentionedAppUserId",
                table: "OrgMessageMentions",
                columns: new[] { "OrgMessageId", "MentionedAppUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrgMessageReports_OrgMessageId_ReportedByAppUserId",
                table: "OrgMessageReports",
                columns: new[] { "OrgMessageId", "ReportedByAppUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrgMessageReports_Outcome_DateCreated",
                table: "OrgMessageReports",
                columns: new[] { "Outcome", "DateCreated" });

            migrationBuilder.CreateIndex(
                name: "IX_OrgMessageReports_ReportedByAppUserId",
                table: "OrgMessageReports",
                column: "ReportedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrgMessageReports_ResolvedByAppUserId",
                table: "OrgMessageReports",
                column: "ResolvedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserFollows_FollowedAppUserId",
                table: "UserFollows",
                column: "FollowedAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserFollows_FollowerAppUserId_FollowedAppUserId",
                table: "UserFollows",
                columns: new[] { "FollowerAppUserId", "FollowedAppUserId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_OrgMessages_AppUsers_HiddenByAppUserId",
                table: "OrgMessages",
                column: "HiddenByAppUserId",
                principalTable: "AppUsers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrgMessages_AppUsers_HiddenByAppUserId",
                table: "OrgMessages");

            migrationBuilder.DropTable(
                name: "OrgMessageHashtags");

            migrationBuilder.DropTable(
                name: "OrgMessageMentions");

            migrationBuilder.DropTable(
                name: "OrgMessageReports");

            migrationBuilder.DropTable(
                name: "UserFollows");

            migrationBuilder.DropIndex(
                name: "IX_OrgMessages_ChannelType_HiddenUtc_DateCreated",
                table: "OrgMessages");

            migrationBuilder.DropIndex(
                name: "IX_OrgMessages_HiddenByAppUserId",
                table: "OrgMessages");

            migrationBuilder.DropColumn(
                name: "HiddenByAppUserId",
                table: "OrgMessages");

            migrationBuilder.DropColumn(
                name: "HiddenUtc",
                table: "OrgMessages");
        }
    }
}
