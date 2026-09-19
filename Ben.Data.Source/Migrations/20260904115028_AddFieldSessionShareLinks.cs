using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddFieldSessionShareLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FieldSessionShareLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Token = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    FieldSessionUploadId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FieldSessionUploadFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ExpiresUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RevokedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IncludePositions = table.Column<bool>(type: "bit", nullable: false),
                    ViewCount = table.Column<int>(type: "int", nullable: false),
                    LastViewedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FieldSessionShareLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FieldSessionShareLinks_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FieldSessionShareLinks_AppUsers_RevokedByAppUserId",
                        column: x => x.RevokedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FieldSessionShareLinks_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FieldSessionShareLinks_FieldSessionUploadFiles_FieldSessionUploadFileId",
                        column: x => x.FieldSessionUploadFileId,
                        principalTable: "FieldSessionUploadFiles",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FieldSessionShareLinks_FieldSessionUploads_FieldSessionUploadId",
                        column: x => x.FieldSessionUploadId,
                        principalTable: "FieldSessionUploads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FieldSessionShareLinkViews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FieldSessionShareLinkId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ViewedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ViewerHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    FieldSessionUploadFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FieldSessionShareLinkViews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FieldSessionShareLinkViews_FieldSessionShareLinks_FieldSessionShareLinkId",
                        column: x => x.FieldSessionShareLinkId,
                        principalTable: "FieldSessionShareLinks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FieldSessionShareLinks_CreatedByAppUserId",
                table: "FieldSessionShareLinks",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_FieldSessionShareLinks_FieldSessionUploadFileId",
                table: "FieldSessionShareLinks",
                column: "FieldSessionUploadFileId");

            migrationBuilder.CreateIndex(
                name: "IX_FieldSessionShareLinks_FieldSessionUploadId_DateCreated",
                table: "FieldSessionShareLinks",
                columns: new[] { "FieldSessionUploadId", "DateCreated" });

            migrationBuilder.CreateIndex(
                name: "IX_FieldSessionShareLinks_RevokedByAppUserId",
                table: "FieldSessionShareLinks",
                column: "RevokedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_FieldSessionShareLinks_Token",
                table: "FieldSessionShareLinks",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FieldSessionShareLinks_UpdatedByAppUserId",
                table: "FieldSessionShareLinks",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_FieldSessionShareLinkViews_FieldSessionShareLinkId_ViewedUtc",
                table: "FieldSessionShareLinkViews",
                columns: new[] { "FieldSessionShareLinkId", "ViewedUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FieldSessionShareLinkViews");

            migrationBuilder.DropTable(
                name: "FieldSessionShareLinks");
        }
    }
}
