using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddFeedLikes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrgMessageLikes",
                columns: table => new
                {
                    OrgMessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LikerAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DateLiked = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrgMessageLikes", x => new { x.OrgMessageId, x.LikerAppUserId });
                    table.ForeignKey(
                        name: "FK_OrgMessageLikes_AppUsers_LikerAppUserId",
                        column: x => x.LikerAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrgMessageLikes_OrgMessages_OrgMessageId",
                        column: x => x.OrgMessageId,
                        principalTable: "OrgMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrgMessageLikes_LikerAppUserId",
                table: "OrgMessageLikes",
                column: "LikerAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrgMessageLikes_OrgMessageId_DateLiked",
                table: "OrgMessageLikes",
                columns: new[] { "OrgMessageId", "DateLiked" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrgMessageLikes");
        }
    }
}
