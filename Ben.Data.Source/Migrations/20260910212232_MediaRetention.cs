using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class MediaRetention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresAtUtc",
                table: "UploadFiles",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiryNoticeSentAtUtc",
                table: "UploadFiles",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "KeptAtUtc",
                table: "UploadFiles",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "KeptByAppUserId",
                table: "UploadFiles",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_UploadFiles_ExpiresAtUtc",
                table: "UploadFiles",
                column: "ExpiresAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UploadFiles_ExpiresAtUtc",
                table: "UploadFiles");

            migrationBuilder.DropColumn(
                name: "ExpiresAtUtc",
                table: "UploadFiles");

            migrationBuilder.DropColumn(
                name: "ExpiryNoticeSentAtUtc",
                table: "UploadFiles");

            migrationBuilder.DropColumn(
                name: "KeptAtUtc",
                table: "UploadFiles");

            migrationBuilder.DropColumn(
                name: "KeptByAppUserId",
                table: "UploadFiles");
        }
    }
}
