using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class FieldSessionBundle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsBundle",
                table: "FieldSessionUploads",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AlterColumn<Guid>(
                name: "UploadFileId",
                table: "FieldSessionUploadFiles",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<string>(
                name: "BundleEntryPath",
                table: "FieldSessionUploadFiles",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContentType",
                table: "FieldSessionUploadFiles",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "FileSize",
                table: "FieldSessionUploadFiles",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsBundle",
                table: "FieldSessionUploads");

            migrationBuilder.DropColumn(
                name: "BundleEntryPath",
                table: "FieldSessionUploadFiles");

            migrationBuilder.DropColumn(
                name: "ContentType",
                table: "FieldSessionUploadFiles");

            migrationBuilder.DropColumn(
                name: "FileSize",
                table: "FieldSessionUploadFiles");

            migrationBuilder.AlterColumn<Guid>(
                name: "UploadFileId",
                table: "FieldSessionUploadFiles",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);
        }
    }
}
