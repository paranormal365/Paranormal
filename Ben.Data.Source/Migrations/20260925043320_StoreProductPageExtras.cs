using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class StoreProductPageExtras : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReturnPolicyText",
                table: "StoreProducts",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ReviewsEnabled",
                table: "StoreProducts",
                type: "bit",
                nullable: false,
                // Every product already here keeps taking reviews, as a new one does (the entity's default).
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "WarrantyText",
                table: "StoreProducts",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StoreProductVideos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UploadFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreProductVideos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoreProductVideos_StoreProducts_ProductId",
                        column: x => x.ProductId,
                        principalTable: "StoreProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StoreProductVideos_UploadFiles_UploadFileId",
                        column: x => x.UploadFileId,
                        principalTable: "UploadFiles",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_StoreProductVideos_ProductId_SortOrder",
                table: "StoreProductVideos",
                columns: new[] { "ProductId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_StoreProductVideos_UploadFileId",
                table: "StoreProductVideos",
                column: "UploadFileId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StoreProductVideos");

            migrationBuilder.DropColumn(
                name: "ReturnPolicyText",
                table: "StoreProducts");

            migrationBuilder.DropColumn(
                name: "ReviewsEnabled",
                table: "StoreProducts");

            migrationBuilder.DropColumn(
                name: "WarrantyText",
                table: "StoreProducts");
        }
    }
}
