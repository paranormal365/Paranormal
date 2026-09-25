using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class StoreProductParts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OtherCostNote",
                table: "StoreProducts",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OtherCostPerUnit",
                table: "StoreProducts",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "StoreProductParts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PriceBasis = table.Column<int>(type: "int", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    PiecesPerPack = table.Column<int>(type: "int", nullable: false),
                    QuantityPerUnit = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    InfoUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    BuyUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ThumbnailUploadFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OnHand = table.Column<int>(type: "int", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreProductParts", x => x.Id);
                    table.CheckConstraint("CK_StoreProductParts_Pack", "[PiecesPerPack] >= 1");
                    table.CheckConstraint("CK_StoreProductParts_PerUnit", "[QuantityPerUnit] >= 0");
                    table.CheckConstraint("CK_StoreProductParts_Price", "[Price] >= 0");
                    table.ForeignKey(
                        name: "FK_StoreProductParts_StoreProducts_ProductId",
                        column: x => x.ProductId,
                        principalTable: "StoreProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StoreProductParts_ProductId_SortOrder",
                table: "StoreProductParts",
                columns: new[] { "ProductId", "SortOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StoreProductParts");

            migrationBuilder.DropColumn(
                name: "OtherCostNote",
                table: "StoreProducts");

            migrationBuilder.DropColumn(
                name: "OtherCostPerUnit",
                table: "StoreProducts");
        }
    }
}
