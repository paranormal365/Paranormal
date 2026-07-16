using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddUploadFileTypeExtensions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowAllExtensions",
                table: "UploadFileTypes",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "UploadFileTypeExtensions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UploadFileTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Pattern = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UploadFileTypeExtensions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UploadFileTypeExtensions_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UploadFileTypeExtensions_UploadFileTypes_UploadFileTypeId",
                        column: x => x.UploadFileTypeId,
                        principalTable: "UploadFileTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UploadFileTypeExtensions_CreatedByAppUserId",
                table: "UploadFileTypeExtensions",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UploadFileTypeExtensions_UploadFileTypeId_Pattern",
                table: "UploadFileTypeExtensions",
                columns: new[] { "UploadFileTypeId", "Pattern" },
                unique: true,
                filter: "[Pattern] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UploadFileTypeExtensions");

            migrationBuilder.DropColumn(
                name: "AllowAllExtensions",
                table: "UploadFileTypes");
        }
    }
}
