using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <summary>
    /// Drops the block-editor research pages. Research is canvas boards from 2026-09-16.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This destroys rows and cannot be undone by running Down.</b> Down rebuilds the two tables
    /// empty — the schema comes back, the writing does not. Before this runs on a database anybody
    /// has written research in, take a copy of CaseResearchEntries and CaseResearchAttachments; the
    /// deploy runbook says so too.
    /// </para>
    /// <para>
    /// Why it goes rather than staying harmlessly: two ways to write up a case is one more than
    /// anybody needed, and the half nobody chose still had to be kept working — its own controller,
    /// its own file-visibility rules, its own place in every purge. The boards do the same job and
    /// keep the parts Ben liked: a page of your own that nobody sees until you publish it, and paste
    /// that knows what you pasted.
    /// </para>
    /// </remarks>
    public partial class RetireResearchPages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CaseResearchAttachments");

            migrationBuilder.DropTable(
                name: "CaseResearchEntries");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CaseResearchEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UploadFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Body = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DraftAuthorAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DraftBlocksJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DraftRevision = table.Column<int>(type: "int", nullable: false),
                    DraftSaveId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DraftSavedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EventDateTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Excerpt = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    PublishedBlocksJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PublishedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PublishedRevision = table.Column<int>(type: "int", nullable: true),
                    PublishedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResearchType = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Url = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CaseResearchEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CaseResearchEntries_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CaseResearchEntries_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CaseResearchEntries_Cases_CaseId",
                        column: x => x.CaseId,
                        principalTable: "Cases",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CaseResearchEntries_UploadFiles_UploadFileId",
                        column: x => x.UploadFileId,
                        principalTable: "UploadFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "CaseResearchAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LinkPreviewId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ResearchEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UploadFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Url = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
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
                name: "IX_CaseResearchEntries_CaseId_PublishedUtc",
                table: "CaseResearchEntries",
                columns: new[] { "CaseId", "PublishedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CaseResearchEntries_CaseId_SortOrder",
                table: "CaseResearchEntries",
                columns: new[] { "CaseId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_CaseResearchEntries_CreatedByAppUserId",
                table: "CaseResearchEntries",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CaseResearchEntries_UpdatedByAppUserId",
                table: "CaseResearchEntries",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CaseResearchEntries_UploadFileId",
                table: "CaseResearchEntries",
                column: "UploadFileId");
        }
    }
}
