using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class InvestigationJoinCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InvestigationJoinCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvestigationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Token = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TypedCode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ExpiresUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RevokedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvestigationJoinCodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvestigationJoinCodes_Investigations_InvestigationId",
                        column: x => x.InvestigationId,
                        principalTable: "Investigations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InvestigationGuestPasses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvestigationJoinCodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvestigationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IssuedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RevokedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvestigationGuestPasses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvestigationGuestPasses_AppUsers_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_InvestigationGuestPasses_InvestigationJoinCodes_InvestigationJoinCodeId",
                        column: x => x.InvestigationJoinCodeId,
                        principalTable: "InvestigationJoinCodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvestigationGuestPasses_AppUserId",
                table: "InvestigationGuestPasses",
                column: "AppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_InvestigationGuestPasses_InvestigationId_AppUserId",
                table: "InvestigationGuestPasses",
                columns: new[] { "InvestigationId", "AppUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_InvestigationGuestPasses_InvestigationJoinCodeId_AppUserId",
                table: "InvestigationGuestPasses",
                columns: new[] { "InvestigationJoinCodeId", "AppUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvestigationJoinCodes_InvestigationId_ExpiresUtc",
                table: "InvestigationJoinCodes",
                columns: new[] { "InvestigationId", "ExpiresUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_InvestigationJoinCodes_Token",
                table: "InvestigationJoinCodes",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvestigationJoinCodes_TypedCode",
                table: "InvestigationJoinCodes",
                column: "TypedCode",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvestigationGuestPasses");

            migrationBuilder.DropTable(
                name: "InvestigationJoinCodes");
        }
    }
}
