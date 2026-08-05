using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class UpdateVideoProjectsForUserOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_VideoProjects_Cases_CaseId",
                table: "VideoProjects");

            migrationBuilder.AlterColumn<Guid>(
                name: "CaseId",
                table: "VideoProjects",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "PublishedUploadFileId",
                table: "VideoProjects",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_VideoProjects_PublishedUploadFileId",
                table: "VideoProjects",
                column: "PublishedUploadFileId");

            migrationBuilder.AddForeignKey(
                name: "FK_VideoProjects_Cases_CaseId",
                table: "VideoProjects",
                column: "CaseId",
                principalTable: "Cases",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_VideoProjects_UploadFiles_PublishedUploadFileId",
                table: "VideoProjects",
                column: "PublishedUploadFileId",
                principalTable: "UploadFiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_VideoProjects_Cases_CaseId",
                table: "VideoProjects");

            migrationBuilder.DropForeignKey(
                name: "FK_VideoProjects_UploadFiles_PublishedUploadFileId",
                table: "VideoProjects");

            migrationBuilder.DropIndex(
                name: "IX_VideoProjects_PublishedUploadFileId",
                table: "VideoProjects");

            migrationBuilder.DropColumn(
                name: "PublishedUploadFileId",
                table: "VideoProjects");

            migrationBuilder.AlterColumn<Guid>(
                name: "CaseId",
                table: "VideoProjects",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_VideoProjects_Cases_CaseId",
                table: "VideoProjects",
                column: "CaseId",
                principalTable: "Cases",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
