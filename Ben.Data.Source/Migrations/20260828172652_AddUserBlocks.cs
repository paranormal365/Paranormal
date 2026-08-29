using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddUserBlocks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserBlocks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BlockerAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BlockedAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserBlocks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserBlocks_AppUsers_BlockedAppUserId",
                        column: x => x.BlockedAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UserBlocks_AppUsers_BlockerAppUserId",
                        column: x => x.BlockerAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserBlocks_BlockedAppUserId",
                table: "UserBlocks",
                column: "BlockedAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserBlocks_BlockerAppUserId",
                table: "UserBlocks",
                column: "BlockerAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserBlocks_BlockerAppUserId_BlockedAppUserId",
                table: "UserBlocks",
                columns: new[] { "BlockerAppUserId", "BlockedAppUserId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserBlocks");
        }
    }
}
