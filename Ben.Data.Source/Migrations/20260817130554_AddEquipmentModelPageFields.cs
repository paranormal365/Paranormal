using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddEquipmentModelPageFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LinkClickCount",
                table: "EquipmentItems",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ViewCount",
                table: "EquipmentItems",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "WebsiteUrl",
                table: "EquipmentItems",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ExcludeFromCatalog",
                table: "EquipmentItemPhotos",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LinkClickCount",
                table: "EquipmentItems");

            migrationBuilder.DropColumn(
                name: "ViewCount",
                table: "EquipmentItems");

            migrationBuilder.DropColumn(
                name: "WebsiteUrl",
                table: "EquipmentItems");

            migrationBuilder.DropColumn(
                name: "ExcludeFromCatalog",
                table: "EquipmentItemPhotos");
        }
    }
}
