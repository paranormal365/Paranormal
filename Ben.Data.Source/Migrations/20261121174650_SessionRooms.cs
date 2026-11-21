using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class SessionRooms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PlaceRoomId",
                table: "FieldSessionUploads",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RoomName",
                table: "FieldSessionUploads",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_FieldSessionUploads_PlaceRoomId",
                table: "FieldSessionUploads",
                column: "PlaceRoomId");

            migrationBuilder.AddForeignKey(
                name: "FK_FieldSessionUploads_PlaceRooms_PlaceRoomId",
                table: "FieldSessionUploads",
                column: "PlaceRoomId",
                principalTable: "PlaceRooms",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FieldSessionUploads_PlaceRooms_PlaceRoomId",
                table: "FieldSessionUploads");

            migrationBuilder.DropIndex(
                name: "IX_FieldSessionUploads_PlaceRoomId",
                table: "FieldSessionUploads");

            migrationBuilder.DropColumn(
                name: "PlaceRoomId",
                table: "FieldSessionUploads");

            migrationBuilder.DropColumn(
                name: "RoomName",
                table: "FieldSessionUploads");
        }
    }
}
