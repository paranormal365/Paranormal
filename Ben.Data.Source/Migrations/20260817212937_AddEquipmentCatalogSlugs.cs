using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddEquipmentCatalogSlugs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UrlName",
                table: "EquipmentModels",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UrlName",
                table: "EquipmentBrands",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentModels_EquipmentBrandId_UrlName",
                table: "EquipmentModels",
                columns: new[] { "EquipmentBrandId", "UrlName" },
                unique: true,
                filter: "[UrlName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentBrands_UrlName",
                table: "EquipmentBrands",
                column: "UrlName",
                unique: true,
                filter: "[UrlName] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EquipmentModels_EquipmentBrandId_UrlName",
                table: "EquipmentModels");

            migrationBuilder.DropIndex(
                name: "IX_EquipmentBrands_UrlName",
                table: "EquipmentBrands");

            migrationBuilder.DropColumn(
                name: "UrlName",
                table: "EquipmentModels");

            migrationBuilder.DropColumn(
                name: "UrlName",
                table: "EquipmentBrands");
        }
    }
}
