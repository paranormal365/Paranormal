using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddUploadFileSharing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UploadFileOrganizationShares",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UploadFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SharedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Visibility = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RemovedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RemovalDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UploadFileOrganizationShares", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UploadFileOrganizationShares_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UploadFileOrganizationShares_AppUsers_RemovedByAppUserId",
                        column: x => x.RemovedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UploadFileOrganizationShares_AppUsers_SharedByAppUserId",
                        column: x => x.SharedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UploadFileOrganizationShares_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UploadFileOrganizationShares_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UploadFileOrganizationShares_UploadFiles_UploadFileId",
                        column: x => x.UploadFileId,
                        principalTable: "UploadFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UploadFilePermissionRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UploadFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RequestedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PermissionType = table.Column<int>(type: "int", nullable: false),
                    RequestStatus = table.Column<int>(type: "int", nullable: false),
                    RequestNotes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReviewNotes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReviewedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DateReviewed = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UploadFilePermissionRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UploadFilePermissionRequests_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UploadFilePermissionRequests_AppUsers_RequestedByAppUserId",
                        column: x => x.RequestedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UploadFilePermissionRequests_AppUsers_ReviewedByAppUserId",
                        column: x => x.ReviewedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UploadFilePermissionRequests_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UploadFilePermissionRequests_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UploadFilePermissionRequests_UploadFiles_UploadFileId",
                        column: x => x.UploadFileId,
                        principalTable: "UploadFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UploadFileOrganizationShares_CreatedByAppUserId",
                table: "UploadFileOrganizationShares",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UploadFileOrganizationShares_OrganizationId",
                table: "UploadFileOrganizationShares",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_UploadFileOrganizationShares_RemovedByAppUserId",
                table: "UploadFileOrganizationShares",
                column: "RemovedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UploadFileOrganizationShares_SharedByAppUserId",
                table: "UploadFileOrganizationShares",
                column: "SharedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UploadFileOrganizationShares_UpdatedByAppUserId",
                table: "UploadFileOrganizationShares",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UploadFileOrganizationShares_UploadFileId_OrganizationId",
                table: "UploadFileOrganizationShares",
                columns: new[] { "UploadFileId", "OrganizationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UploadFilePermissionRequests_CreatedByAppUserId",
                table: "UploadFilePermissionRequests",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UploadFilePermissionRequests_OrganizationId",
                table: "UploadFilePermissionRequests",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_UploadFilePermissionRequests_RequestedByAppUserId",
                table: "UploadFilePermissionRequests",
                column: "RequestedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UploadFilePermissionRequests_ReviewedByAppUserId",
                table: "UploadFilePermissionRequests",
                column: "ReviewedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UploadFilePermissionRequests_UpdatedByAppUserId",
                table: "UploadFilePermissionRequests",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UploadFilePermissionRequests_UploadFileId",
                table: "UploadFilePermissionRequests",
                column: "UploadFileId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UploadFileOrganizationShares");

            migrationBuilder.DropTable(
                name: "UploadFilePermissionRequests");
        }
    }
}
