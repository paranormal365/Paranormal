using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class PlacePosts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PlaceId",
                table: "OrgMessages",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrgMessages_PlaceId_DateCreated",
                table: "OrgMessages",
                columns: new[] { "PlaceId", "DateCreated" },
                filter: "[PlaceId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_OrgMessages_Places_PlaceId",
                table: "OrgMessages",
                column: "PlaceId",
                principalTable: "Places",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrgMessages_Places_PlaceId",
                table: "OrgMessages");

            migrationBuilder.DropIndex(
                name: "IX_OrgMessages_PlaceId_DateCreated",
                table: "OrgMessages");

            migrationBuilder.DropColumn(
                name: "PlaceId",
                table: "OrgMessages");
        }
    }
}
