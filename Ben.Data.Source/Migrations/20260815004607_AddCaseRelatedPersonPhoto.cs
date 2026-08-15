using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddCaseRelatedPersonPhoto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "UploadFileId",
                table: "CaseRelatedPeople",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CaseRelatedPeople_UploadFileId",
                table: "CaseRelatedPeople",
                column: "UploadFileId");

            migrationBuilder.AddForeignKey(
                name: "FK_CaseRelatedPeople_UploadFiles_UploadFileId",
                table: "CaseRelatedPeople",
                column: "UploadFileId",
                principalTable: "UploadFiles",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CaseRelatedPeople_UploadFiles_UploadFileId",
                table: "CaseRelatedPeople");

            migrationBuilder.DropIndex(
                name: "IX_CaseRelatedPeople_UploadFileId",
                table: "CaseRelatedPeople");

            migrationBuilder.DropColumn(
                name: "UploadFileId",
                table: "CaseRelatedPeople");
        }
    }
}
