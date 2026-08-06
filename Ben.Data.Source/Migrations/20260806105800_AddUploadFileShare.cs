using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddUploadFileShare : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UploadFileShares",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UploadFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetType = table.Column<int>(type: "int", nullable: false),
                    TargetAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TargetInvestigationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TargetOrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SharedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                    table.PrimaryKey("PK_UploadFileShares", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UploadFileShares_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UploadFileShares_AppUsers_RemovedByAppUserId",
                        column: x => x.RemovedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UploadFileShares_AppUsers_SharedByAppUserId",
                        column: x => x.SharedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UploadFileShares_AppUsers_TargetAppUserId",
                        column: x => x.TargetAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UploadFileShares_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UploadFileShares_Investigations_TargetInvestigationId",
                        column: x => x.TargetInvestigationId,
                        principalTable: "Investigations",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UploadFileShares_Organizations_TargetOrganizationId",
                        column: x => x.TargetOrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UploadFileShares_UploadFiles_UploadFileId",
                        column: x => x.UploadFileId,
                        principalTable: "UploadFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UploadFileShares_CreatedByAppUserId",
                table: "UploadFileShares",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UploadFileShares_RemovedByAppUserId",
                table: "UploadFileShares",
                column: "RemovedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UploadFileShares_SharedByAppUserId",
                table: "UploadFileShares",
                column: "SharedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UploadFileShares_TargetAppUserId",
                table: "UploadFileShares",
                column: "TargetAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UploadFileShares_TargetInvestigationId",
                table: "UploadFileShares",
                column: "TargetInvestigationId");

            migrationBuilder.CreateIndex(
                name: "IX_UploadFileShares_TargetOrganizationId",
                table: "UploadFileShares",
                column: "TargetOrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_UploadFileShares_UpdatedByAppUserId",
                table: "UploadFileShares",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UploadFileShares_UploadFileId_TargetType_IsActive",
                table: "UploadFileShares",
                columns: new[] { "UploadFileId", "TargetType", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UploadFileShares");
        }
    }
}
