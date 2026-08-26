using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddMemberLevelSuggestedRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrganizationMemberLevelRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationMemberLevelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationRoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationMemberLevelRoles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationMemberLevelRoles_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrganizationMemberLevelRoles_OrganizationMemberLevels_OrganizationMemberLevelId",
                        column: x => x.OrganizationMemberLevelId,
                        principalTable: "OrganizationMemberLevels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrganizationMemberLevelRoles_OrganizationRoles_OrganizationRoleId",
                        column: x => x.OrganizationRoleId,
                        principalTable: "OrganizationRoles",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMemberLevelRoles_CreatedByAppUserId",
                table: "OrganizationMemberLevelRoles",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMemberLevelRoles_OrganizationMemberLevelId_OrganizationRoleId",
                table: "OrganizationMemberLevelRoles",
                columns: new[] { "OrganizationMemberLevelId", "OrganizationRoleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMemberLevelRoles_OrganizationRoleId",
                table: "OrganizationMemberLevelRoles",
                column: "OrganizationRoleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrganizationMemberLevelRoles");
        }
    }
}
