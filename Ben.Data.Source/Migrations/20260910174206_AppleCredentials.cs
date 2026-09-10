using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AppleCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppleCredentials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ProtectedRefreshToken = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateRevocationFailed = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppleCredentials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppleCredentials_AppUsers_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppleCredentials_AppUserId_ClientId",
                table: "AppleCredentials",
                columns: new[] { "AppUserId", "ClientId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppleCredentials");
        }
    }
}
