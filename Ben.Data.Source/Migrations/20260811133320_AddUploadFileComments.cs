using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddUploadFileComments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowClientComments",
                table: "UploadFiles",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AllowInvestigationTeamComments",
                table: "UploadFiles",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AllowOrganizationComments",
                table: "UploadFiles",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AllowPublicComments",
                table: "UploadFiles",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "CaseCopyOfUploadFileId",
                table: "UploadFiles",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "UploadFileComments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UploadFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuthorAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsOwner = table.Column<bool>(type: "bit", nullable: false),
                    IsInvestigationTeamMember = table.Column<bool>(type: "bit", nullable: false),
                    IsClient = table.Column<bool>(type: "bit", nullable: false),
                    IsOrganizationMember = table.Column<bool>(type: "bit", nullable: false),
                    IsPublicCommenter = table.Column<bool>(type: "bit", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UploadFileComments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UploadFileComments_AppUsers_AuthorAppUserId",
                        column: x => x.AuthorAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UploadFileComments_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UploadFileComments_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UploadFileComments_UploadFiles_UploadFileId",
                        column: x => x.UploadFileId,
                        principalTable: "UploadFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UploadFiles_CaseCopyOfUploadFileId",
                table: "UploadFiles",
                column: "CaseCopyOfUploadFileId");

            migrationBuilder.CreateIndex(
                name: "IX_UploadFileComments_AuthorAppUserId",
                table: "UploadFileComments",
                column: "AuthorAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UploadFileComments_CreatedByAppUserId",
                table: "UploadFileComments",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UploadFileComments_UpdatedByAppUserId",
                table: "UploadFileComments",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UploadFileComments_UploadFileId_DateCreated",
                table: "UploadFileComments",
                columns: new[] { "UploadFileId", "DateCreated" });

            migrationBuilder.AddForeignKey(
                name: "FK_UploadFiles_UploadFiles_CaseCopyOfUploadFileId",
                table: "UploadFiles",
                column: "CaseCopyOfUploadFileId",
                principalTable: "UploadFiles",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UploadFiles_UploadFiles_CaseCopyOfUploadFileId",
                table: "UploadFiles");

            migrationBuilder.DropTable(
                name: "UploadFileComments");

            migrationBuilder.DropIndex(
                name: "IX_UploadFiles_CaseCopyOfUploadFileId",
                table: "UploadFiles");

            migrationBuilder.DropColumn(
                name: "AllowClientComments",
                table: "UploadFiles");

            migrationBuilder.DropColumn(
                name: "AllowInvestigationTeamComments",
                table: "UploadFiles");

            migrationBuilder.DropColumn(
                name: "AllowOrganizationComments",
                table: "UploadFiles");

            migrationBuilder.DropColumn(
                name: "AllowPublicComments",
                table: "UploadFiles");

            migrationBuilder.DropColumn(
                name: "CaseCopyOfUploadFileId",
                table: "UploadFiles");
        }
    }
}
