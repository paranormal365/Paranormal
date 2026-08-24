using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddFeedPostMedia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MediaReviewState",
                table: "OrgMessages",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "MediaUploadFileId",
                table: "OrgMessages",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrgMessages_MediaReviewState_DateCreated",
                table: "OrgMessages",
                columns: new[] { "MediaReviewState", "DateCreated" });

            migrationBuilder.CreateIndex(
                name: "IX_OrgMessages_MediaUploadFileId",
                table: "OrgMessages",
                column: "MediaUploadFileId");

            migrationBuilder.AddForeignKey(
                name: "FK_OrgMessages_UploadFiles_MediaUploadFileId",
                table: "OrgMessages",
                column: "MediaUploadFileId",
                principalTable: "UploadFiles",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrgMessages_UploadFiles_MediaUploadFileId",
                table: "OrgMessages");

            migrationBuilder.DropIndex(
                name: "IX_OrgMessages_MediaReviewState_DateCreated",
                table: "OrgMessages");

            migrationBuilder.DropIndex(
                name: "IX_OrgMessages_MediaUploadFileId",
                table: "OrgMessages");

            migrationBuilder.DropColumn(
                name: "MediaReviewState",
                table: "OrgMessages");

            migrationBuilder.DropColumn(
                name: "MediaUploadFileId",
                table: "OrgMessages");
        }
    }
}
