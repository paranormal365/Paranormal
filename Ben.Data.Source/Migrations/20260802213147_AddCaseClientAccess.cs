using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddCaseClientAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CaseClientAccesses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CaseClientAccesses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CaseClientAccesses_AppUsers_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CaseClientAccesses_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CaseClientAccesses_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CaseClientAccesses_Cases_CaseId",
                        column: x => x.CaseId,
                        principalTable: "Cases",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CaseClientAccesses_AppUserId",
                table: "CaseClientAccesses",
                column: "AppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CaseClientAccesses_CaseId_AppUserId",
                table: "CaseClientAccesses",
                columns: new[] { "CaseId", "AppUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CaseClientAccesses_CreatedByAppUserId",
                table: "CaseClientAccesses",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CaseClientAccesses_UpdatedByAppUserId",
                table: "CaseClientAccesses",
                column: "UpdatedByAppUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CaseClientAccesses");
        }
    }
}
