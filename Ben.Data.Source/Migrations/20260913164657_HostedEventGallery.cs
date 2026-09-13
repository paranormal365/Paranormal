using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <summary>An event's public gallery (item 235 phase 11). One new table.</summary>
    public partial class HostedEventGallery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HostedEventGalleryImages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HostedEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UploadFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Caption = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostedEventGalleryImages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HostedEventGalleryImages_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_HostedEventGalleryImages_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_HostedEventGalleryImages_HostedEvents_HostedEventId",
                        column: x => x.HostedEventId,
                        principalTable: "HostedEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_HostedEventGalleryImages_UploadFiles_UploadFileId",
                        column: x => x.UploadFileId,
                        principalTable: "UploadFiles",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventGalleryImages_CreatedByAppUserId",
                table: "HostedEventGalleryImages",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventGalleryImages_HostedEventId_SortOrder",
                table: "HostedEventGalleryImages",
                columns: new[] { "HostedEventId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventGalleryImages_UpdatedByAppUserId",
                table: "HostedEventGalleryImages",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventGalleryImages_UploadFileId",
                table: "HostedEventGalleryImages",
                column: "UploadFileId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HostedEventGalleryImages");
        }
    }
}
