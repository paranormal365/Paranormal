using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddOrgFilePublishingAndDeleteLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DatePublished",
                table: "OrganizationFiles",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PublishedByAppUserId",
                table: "OrganizationFiles",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OrganizationFileDeleteLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    OriginalFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    StoragePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SourceUploadFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WasPublic = table.Column<bool>(type: "bit", nullable: false),
                    WasPublishedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WasPublishedByDisplayName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    WasDatePublished = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeletedByDisplayName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    DateDeleted = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationFileDeleteLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationFiles_PublishedByAppUserId",
                table: "OrganizationFiles",
                column: "PublishedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationFileDeleteLogs_DeletedByAppUserId",
                table: "OrganizationFileDeleteLogs",
                column: "DeletedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationFileDeleteLogs_OrganizationId",
                table: "OrganizationFileDeleteLogs",
                column: "OrganizationId");

            migrationBuilder.AddForeignKey(
                name: "FK_OrganizationFiles_AppUsers_PublishedByAppUserId",
                table: "OrganizationFiles",
                column: "PublishedByAppUserId",
                principalTable: "AppUsers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrganizationFiles_AppUsers_PublishedByAppUserId",
                table: "OrganizationFiles");

            migrationBuilder.DropTable(
                name: "OrganizationFileDeleteLogs");

            migrationBuilder.DropIndex(
                name: "IX_OrganizationFiles_PublishedByAppUserId",
                table: "OrganizationFiles");

            migrationBuilder.DropColumn(
                name: "DatePublished",
                table: "OrganizationFiles");

            migrationBuilder.DropColumn(
                name: "PublishedByAppUserId",
                table: "OrganizationFiles");
        }
    }
}
