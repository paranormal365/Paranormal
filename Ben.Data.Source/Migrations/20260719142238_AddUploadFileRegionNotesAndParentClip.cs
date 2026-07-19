using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddUploadFileRegionNotesAndParentClip : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ParentFileId",
                table: "UploadFiles",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "RegionEnd",
                table: "UploadFiles",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "RegionStart",
                table: "UploadFiles",
                type: "float",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "UploadFileRegionNotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UploadFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RegionStart = table.Column<double>(type: "float", nullable: false),
                    RegionEnd = table.Column<double>(type: "float", nullable: false),
                    RegionLabel = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TimeOffset = table.Column<double>(type: "float", nullable: true),
                    NoteHtml = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsPublic = table.Column<bool>(type: "bit", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UploadFileRegionNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UploadFileRegionNotes_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UploadFileRegionNotes_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UploadFileRegionNotes_UploadFiles_UploadFileId",
                        column: x => x.UploadFileId,
                        principalTable: "UploadFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UploadFiles_ParentFileId",
                table: "UploadFiles",
                column: "ParentFileId");

            migrationBuilder.CreateIndex(
                name: "IX_UploadFileRegionNotes_CreatedByAppUserId",
                table: "UploadFileRegionNotes",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UploadFileRegionNotes_UpdatedByAppUserId",
                table: "UploadFileRegionNotes",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UploadFileRegionNotes_UploadFileId",
                table: "UploadFileRegionNotes",
                column: "UploadFileId");

            migrationBuilder.AddForeignKey(
                name: "FK_UploadFiles_UploadFiles_ParentFileId",
                table: "UploadFiles",
                column: "ParentFileId",
                principalTable: "UploadFiles",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UploadFiles_UploadFiles_ParentFileId",
                table: "UploadFiles");

            migrationBuilder.DropTable(
                name: "UploadFileRegionNotes");

            migrationBuilder.DropIndex(
                name: "IX_UploadFiles_ParentFileId",
                table: "UploadFiles");

            migrationBuilder.DropColumn(
                name: "ParentFileId",
                table: "UploadFiles");

            migrationBuilder.DropColumn(
                name: "RegionEnd",
                table: "UploadFiles");

            migrationBuilder.DropColumn(
                name: "RegionStart",
                table: "UploadFiles");
        }
    }
}
