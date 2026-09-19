using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationMemberLevels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "MemberLevelId",
                table: "OrganizationUserMemberships",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OrganizationMemberLevels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationMemberLevels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationMemberLevels_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrganizationMemberLevels_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrganizationMemberLevels_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationUserMemberships_MemberLevelId",
                table: "OrganizationUserMemberships",
                column: "MemberLevelId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMemberLevels_CreatedByAppUserId",
                table: "OrganizationMemberLevels",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMemberLevels_OrganizationId_SortOrder",
                table: "OrganizationMemberLevels",
                columns: new[] { "OrganizationId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMemberLevels_UpdatedByAppUserId",
                table: "OrganizationMemberLevels",
                column: "UpdatedByAppUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_OrganizationUserMemberships_OrganizationMemberLevels_MemberLevelId",
                table: "OrganizationUserMemberships",
                column: "MemberLevelId",
                principalTable: "OrganizationMemberLevels",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrganizationUserMemberships_OrganizationMemberLevels_MemberLevelId",
                table: "OrganizationUserMemberships");

            migrationBuilder.DropTable(
                name: "OrganizationMemberLevels");

            migrationBuilder.DropIndex(
                name: "IX_OrganizationUserMemberships_MemberLevelId",
                table: "OrganizationUserMemberships");

            migrationBuilder.DropColumn(
                name: "MemberLevelId",
                table: "OrganizationUserMemberships");
        }
    }
}
