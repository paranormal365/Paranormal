using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddSignInEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SignInEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Succeeded = table.Column<bool>(type: "bit", nullable: false),
                    Method = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignInEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SignInEvents_AppUsers_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_SignInEvents_AppUserId_Utc",
                table: "SignInEvents",
                columns: new[] { "AppUserId", "Utc" });

            migrationBuilder.CreateIndex(
                name: "IX_SignInEvents_Utc_Succeeded",
                table: "SignInEvents",
                columns: new[] { "Utc", "Succeeded" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SignInEvents");
        }
    }
}
