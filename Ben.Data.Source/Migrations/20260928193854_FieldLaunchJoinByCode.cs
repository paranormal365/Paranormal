using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class FieldLaunchJoinByCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "JoinToken",
                table: "FieldLaunches",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FieldLaunchJoinRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FieldLaunchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RequestedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DecidedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DecidedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FieldLaunchJoinRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FieldLaunchJoinRequests_FieldLaunches_FieldLaunchId",
                        column: x => x.FieldLaunchId,
                        principalTable: "FieldLaunches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FieldLaunches_JoinToken",
                table: "FieldLaunches",
                column: "JoinToken",
                unique: true,
                filter: "[JoinToken] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FieldLaunchJoinRequests_FieldLaunchId_AppUserId",
                table: "FieldLaunchJoinRequests",
                columns: new[] { "FieldLaunchId", "AppUserId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FieldLaunchJoinRequests");

            migrationBuilder.DropIndex(
                name: "IX_FieldLaunches_JoinToken",
                table: "FieldLaunches");

            migrationBuilder.DropColumn(
                name: "JoinToken",
                table: "FieldLaunches");
        }
    }
}
