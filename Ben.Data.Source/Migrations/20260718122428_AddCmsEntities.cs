using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddCmsEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPublic",
                table: "OrganizationPages",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "ParentPageId",
                table: "OrganizationPages",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CmsSections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationPageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SectionType = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CmsSections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CmsSections_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CmsSections_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CmsSections_OrganizationPages_OrganizationPageId",
                        column: x => x.OrganizationPageId,
                        principalTable: "OrganizationPages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationLogos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UploadFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AltText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationLogos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationLogos_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrganizationLogos_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrganizationLogos_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrganizationLogos_UploadFiles_UploadFileId",
                        column: x => x.UploadFileId,
                        principalTable: "UploadFiles",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "OrgMemberGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrgMemberGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrgMemberGroups_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrgMemberGroups_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrgMemberGroups_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CmsPagePermissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationPageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OrgMemberGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Actions = table.Column<int>(type: "int", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CmsPagePermissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CmsPagePermissions_AppUsers_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CmsPagePermissions_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CmsPagePermissions_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CmsPagePermissions_OrgMemberGroups_OrgMemberGroupId",
                        column: x => x.OrgMemberGroupId,
                        principalTable: "OrgMemberGroups",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CmsPagePermissions_OrganizationPages_OrganizationPageId",
                        column: x => x.OrganizationPageId,
                        principalTable: "OrganizationPages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrgMemberGroupMemberships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrgMemberGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationUserMembershipId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrgMemberGroupMemberships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrgMemberGroupMemberships_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrgMemberGroupMemberships_OrgMemberGroups_OrgMemberGroupId",
                        column: x => x.OrgMemberGroupId,
                        principalTable: "OrgMemberGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrgMemberGroupMemberships_OrganizationUserMemberships_OrganizationUserMembershipId",
                        column: x => x.OrganizationUserMembershipId,
                        principalTable: "OrganizationUserMemberships",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationPages_ParentPageId",
                table: "OrganizationPages",
                column: "ParentPageId");

            migrationBuilder.CreateIndex(
                name: "IX_CmsPagePermissions_AppUserId",
                table: "CmsPagePermissions",
                column: "AppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CmsPagePermissions_CreatedByAppUserId",
                table: "CmsPagePermissions",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CmsPagePermissions_OrganizationPageId",
                table: "CmsPagePermissions",
                column: "OrganizationPageId");

            migrationBuilder.CreateIndex(
                name: "IX_CmsPagePermissions_OrgMemberGroupId",
                table: "CmsPagePermissions",
                column: "OrgMemberGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_CmsPagePermissions_UpdatedByAppUserId",
                table: "CmsPagePermissions",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CmsSections_CreatedByAppUserId",
                table: "CmsSections",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CmsSections_OrganizationPageId",
                table: "CmsSections",
                column: "OrganizationPageId");

            migrationBuilder.CreateIndex(
                name: "IX_CmsSections_UpdatedByAppUserId",
                table: "CmsSections",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationLogos_CreatedByAppUserId",
                table: "OrganizationLogos",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationLogos_OrganizationId",
                table: "OrganizationLogos",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationLogos_UpdatedByAppUserId",
                table: "OrganizationLogos",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationLogos_UploadFileId",
                table: "OrganizationLogos",
                column: "UploadFileId");

            migrationBuilder.CreateIndex(
                name: "IX_OrgMemberGroupMemberships_CreatedByAppUserId",
                table: "OrgMemberGroupMemberships",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrgMemberGroupMemberships_OrganizationUserMembershipId",
                table: "OrgMemberGroupMemberships",
                column: "OrganizationUserMembershipId");

            migrationBuilder.CreateIndex(
                name: "IX_OrgMemberGroupMemberships_OrgMemberGroupId_OrganizationUserMembershipId",
                table: "OrgMemberGroupMemberships",
                columns: new[] { "OrgMemberGroupId", "OrganizationUserMembershipId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrgMemberGroups_CreatedByAppUserId",
                table: "OrgMemberGroups",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrgMemberGroups_OrganizationId",
                table: "OrgMemberGroups",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_OrgMemberGroups_UpdatedByAppUserId",
                table: "OrgMemberGroups",
                column: "UpdatedByAppUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_OrganizationPages_OrganizationPages_ParentPageId",
                table: "OrganizationPages",
                column: "ParentPageId",
                principalTable: "OrganizationPages",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrganizationPages_OrganizationPages_ParentPageId",
                table: "OrganizationPages");

            migrationBuilder.DropTable(
                name: "CmsPagePermissions");

            migrationBuilder.DropTable(
                name: "CmsSections");

            migrationBuilder.DropTable(
                name: "OrganizationLogos");

            migrationBuilder.DropTable(
                name: "OrgMemberGroupMemberships");

            migrationBuilder.DropTable(
                name: "OrgMemberGroups");

            migrationBuilder.DropIndex(
                name: "IX_OrganizationPages_ParentPageId",
                table: "OrganizationPages");

            migrationBuilder.DropColumn(
                name: "IsPublic",
                table: "OrganizationPages");

            migrationBuilder.DropColumn(
                name: "ParentPageId",
                table: "OrganizationPages");
        }
    }
}
