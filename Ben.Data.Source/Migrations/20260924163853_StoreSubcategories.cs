using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class StoreSubcategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ParentCategoryId",
                table: "StoreCategories",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoreCategories_ParentCategoryId",
                table: "StoreCategories",
                column: "ParentCategoryId");

            migrationBuilder.AddForeignKey(
                name: "FK_StoreCategories_StoreCategories_ParentCategoryId",
                table: "StoreCategories",
                column: "ParentCategoryId",
                principalTable: "StoreCategories",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StoreCategories_StoreCategories_ParentCategoryId",
                table: "StoreCategories");

            migrationBuilder.DropIndex(
                name: "IX_StoreCategories_ParentCategoryId",
                table: "StoreCategories");

            migrationBuilder.DropColumn(
                name: "ParentCategoryId",
                table: "StoreCategories");
        }
    }
}
