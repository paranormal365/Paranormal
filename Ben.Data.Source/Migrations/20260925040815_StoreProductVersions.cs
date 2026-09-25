using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class StoreProductVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DiscontinuedUtc",
                table: "StoreProducts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PreviousVersionProductId",
                table: "StoreProducts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SellingOutSinceUtc",
                table: "StoreProducts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SupersededAppliedUtc",
                table: "StoreProducts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SupersededPolicy",
                table: "StoreProducts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "VersionLabel",
                table: "StoreProducts",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoreProducts_PreviousVersionProductId",
                table: "StoreProducts",
                column: "PreviousVersionProductId",
                unique: true,
                filter: "[PreviousVersionProductId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_StoreProducts_StoreProducts_PreviousVersionProductId",
                table: "StoreProducts",
                column: "PreviousVersionProductId",
                principalTable: "StoreProducts",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StoreProducts_StoreProducts_PreviousVersionProductId",
                table: "StoreProducts");

            migrationBuilder.DropIndex(
                name: "IX_StoreProducts_PreviousVersionProductId",
                table: "StoreProducts");

            migrationBuilder.DropColumn(
                name: "DiscontinuedUtc",
                table: "StoreProducts");

            migrationBuilder.DropColumn(
                name: "PreviousVersionProductId",
                table: "StoreProducts");

            migrationBuilder.DropColumn(
                name: "SellingOutSinceUtc",
                table: "StoreProducts");

            migrationBuilder.DropColumn(
                name: "SupersededAppliedUtc",
                table: "StoreProducts");

            migrationBuilder.DropColumn(
                name: "SupersededPolicy",
                table: "StoreProducts");

            migrationBuilder.DropColumn(
                name: "VersionLabel",
                table: "StoreProducts");
        }
    }
}
