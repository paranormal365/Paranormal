using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddEventEvidencePlacePublication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ArchiveReviewNote",
                table: "EventEvidenceSubmissions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ArchiveReviewState",
                table: "EventEvidenceSubmissions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "PublishedToPlaceAtUtc",
                table: "EventEvidenceSubmissions",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ArchiveReviewNote",
                table: "EventEvidenceSubmissions");

            migrationBuilder.DropColumn(
                name: "ArchiveReviewState",
                table: "EventEvidenceSubmissions");

            migrationBuilder.DropColumn(
                name: "PublishedToPlaceAtUtc",
                table: "EventEvidenceSubmissions");
        }
    }
}
