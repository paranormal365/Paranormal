using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddFieldSessionPlaceAndPublication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PlaceId",
                table: "FieldSessionUploads",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PublishedAtUtc",
                table: "FieldSessionUploads",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_FieldSessionUploads_PlaceId_PublishedAtUtc",
                table: "FieldSessionUploads",
                columns: new[] { "PlaceId", "PublishedAtUtc" });

            migrationBuilder.AddForeignKey(
                name: "FK_FieldSessionUploads_Places_PlaceId",
                table: "FieldSessionUploads",
                column: "PlaceId",
                principalTable: "Places",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FieldSessionUploads_Places_PlaceId",
                table: "FieldSessionUploads");

            migrationBuilder.DropIndex(
                name: "IX_FieldSessionUploads_PlaceId_PublishedAtUtc",
                table: "FieldSessionUploads");

            migrationBuilder.DropColumn(
                name: "PlaceId",
                table: "FieldSessionUploads");

            migrationBuilder.DropColumn(
                name: "PublishedAtUtc",
                table: "FieldSessionUploads");
        }
    }
}
