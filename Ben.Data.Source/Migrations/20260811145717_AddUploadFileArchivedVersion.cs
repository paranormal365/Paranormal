using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddUploadFileArchivedVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ArchivedFromUploadFileId",
                table: "UploadFiles",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_UploadFiles_ArchivedFromUploadFileId",
                table: "UploadFiles",
                column: "ArchivedFromUploadFileId");

            migrationBuilder.AddForeignKey(
                name: "FK_UploadFiles_UploadFiles_ArchivedFromUploadFileId",
                table: "UploadFiles",
                column: "ArchivedFromUploadFileId",
                principalTable: "UploadFiles",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UploadFiles_UploadFiles_ArchivedFromUploadFileId",
                table: "UploadFiles");

            migrationBuilder.DropIndex(
                name: "IX_UploadFiles_ArchivedFromUploadFileId",
                table: "UploadFiles");

            migrationBuilder.DropColumn(
                name: "ArchivedFromUploadFileId",
                table: "UploadFiles");
        }
    }
}
