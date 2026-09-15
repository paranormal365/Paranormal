using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class ResearchPages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DraftAuthorAppUserId",
                table: "CaseResearchEntries",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DraftBlocksJson",
                table: "CaseResearchEntries",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DraftRevision",
                table: "CaseResearchEntries",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "DraftSaveId",
                table: "CaseResearchEntries",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DraftSavedUtc",
                table: "CaseResearchEntries",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EventDateTime",
                table: "CaseResearchEntries",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Excerpt",
                table: "CaseResearchEntries",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PublishedBlocksJson",
                table: "CaseResearchEntries",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PublishedByAppUserId",
                table: "CaseResearchEntries",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PublishedRevision",
                table: "CaseResearchEntries",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PublishedUtc",
                table: "CaseResearchEntries",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "LinkPreviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Url = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    UrlHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Domain = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SiteName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ThumbnailStoragePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ThumbnailContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Fetched = table.Column<bool>(type: "bit", nullable: false),
                    FailureReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    FetchedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FetchedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LinkPreviews", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CaseResearchAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResearchEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    UploadFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Url = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    LinkPreviewId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CaseResearchAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CaseResearchAttachments_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CaseResearchAttachments_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CaseResearchAttachments_CaseResearchEntries_ResearchEntryId",
                        column: x => x.ResearchEntryId,
                        principalTable: "CaseResearchEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CaseResearchAttachments_LinkPreviews_LinkPreviewId",
                        column: x => x.LinkPreviewId,
                        principalTable: "LinkPreviews",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CaseResearchAttachments_UploadFiles_UploadFileId",
                        column: x => x.UploadFileId,
                        principalTable: "UploadFiles",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CaseResearchEntries_CaseId_PublishedUtc",
                table: "CaseResearchEntries",
                columns: new[] { "CaseId", "PublishedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CaseResearchAttachments_CreatedByAppUserId",
                table: "CaseResearchAttachments",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CaseResearchAttachments_LinkPreviewId",
                table: "CaseResearchAttachments",
                column: "LinkPreviewId");

            migrationBuilder.CreateIndex(
                name: "IX_CaseResearchAttachments_ResearchEntryId_SortOrder",
                table: "CaseResearchAttachments",
                columns: new[] { "ResearchEntryId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_CaseResearchAttachments_UpdatedByAppUserId",
                table: "CaseResearchAttachments",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CaseResearchAttachments_UploadFileId",
                table: "CaseResearchAttachments",
                column: "UploadFileId");

            migrationBuilder.CreateIndex(
                name: "IX_LinkPreviews_ExpiresUtc",
                table: "LinkPreviews",
                column: "ExpiresUtc");

            migrationBuilder.CreateIndex(
                name: "IX_LinkPreviews_UrlHash",
                table: "LinkPreviews",
                column: "UrlHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CaseResearchAttachments");

            migrationBuilder.DropTable(
                name: "LinkPreviews");

            migrationBuilder.DropIndex(
                name: "IX_CaseResearchEntries_CaseId_PublishedUtc",
                table: "CaseResearchEntries");

            migrationBuilder.DropColumn(
                name: "DraftAuthorAppUserId",
                table: "CaseResearchEntries");

            migrationBuilder.DropColumn(
                name: "DraftBlocksJson",
                table: "CaseResearchEntries");

            migrationBuilder.DropColumn(
                name: "DraftRevision",
                table: "CaseResearchEntries");

            migrationBuilder.DropColumn(
                name: "DraftSaveId",
                table: "CaseResearchEntries");

            migrationBuilder.DropColumn(
                name: "DraftSavedUtc",
                table: "CaseResearchEntries");

            migrationBuilder.DropColumn(
                name: "EventDateTime",
                table: "CaseResearchEntries");

            migrationBuilder.DropColumn(
                name: "Excerpt",
                table: "CaseResearchEntries");

            migrationBuilder.DropColumn(
                name: "PublishedBlocksJson",
                table: "CaseResearchEntries");

            migrationBuilder.DropColumn(
                name: "PublishedByAppUserId",
                table: "CaseResearchEntries");

            migrationBuilder.DropColumn(
                name: "PublishedRevision",
                table: "CaseResearchEntries");

            migrationBuilder.DropColumn(
                name: "PublishedUtc",
                table: "CaseResearchEntries");
        }
    }
}
