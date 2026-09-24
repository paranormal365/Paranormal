using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class StoreProductSeller : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SellerAppUserId",
                table: "StoreProducts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoreProducts_SellerAppUserId",
                table: "StoreProducts",
                column: "SellerAppUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_StoreProducts_AppUsers_SellerAppUserId",
                table: "StoreProducts",
                column: "SellerAppUserId",
                principalTable: "AppUsers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StoreProducts_AppUsers_SellerAppUserId",
                table: "StoreProducts");

            migrationBuilder.DropIndex(
                name: "IX_StoreProducts_SellerAppUserId",
                table: "StoreProducts");

            migrationBuilder.DropColumn(
                name: "SellerAppUserId",
                table: "StoreProducts");
        }
    }
}
