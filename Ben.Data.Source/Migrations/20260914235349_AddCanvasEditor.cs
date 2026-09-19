using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddCanvasEditor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CanvasDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    DocumentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    PublishedUploadFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PublishedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PublishedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CanvasDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CanvasDocuments_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CanvasDocuments_AppUsers_PublishedByAppUserId",
                        column: x => x.PublishedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CanvasDocuments_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CanvasDocuments_Cases_CaseId",
                        column: x => x.CaseId,
                        principalTable: "Cases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_CanvasDocuments_UploadFiles_PublishedUploadFileId",
                        column: x => x.PublishedUploadFileId,
                        principalTable: "UploadFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "LinkUnfurlCache",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UrlHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Url = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    StatusCode = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    ImageSourceUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    SiteName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    FetchedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LinkUnfurlCache", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CanvasDocuments_CaseId",
                table: "CanvasDocuments",
                column: "CaseId");

            migrationBuilder.CreateIndex(
                name: "IX_CanvasDocuments_CreatedByAppUserId",
                table: "CanvasDocuments",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CanvasDocuments_PublishedByAppUserId",
                table: "CanvasDocuments",
                column: "PublishedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CanvasDocuments_PublishedUploadFileId",
                table: "CanvasDocuments",
                column: "PublishedUploadFileId");

            migrationBuilder.CreateIndex(
                name: "IX_CanvasDocuments_UpdatedByAppUserId",
                table: "CanvasDocuments",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_LinkUnfurlCache_ExpiresAtUtc",
                table: "LinkUnfurlCache",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_LinkUnfurlCache_UrlHash",
                table: "LinkUnfurlCache",
                column: "UrlHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CanvasDocuments");

            migrationBuilder.DropTable(
                name: "LinkUnfurlCache");
        }
    }
}
