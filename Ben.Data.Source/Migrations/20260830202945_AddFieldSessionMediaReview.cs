using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddFieldSessionMediaReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MediaReviewNote",
                table: "FieldSessionUploads",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MediaReviewState",
                table: "FieldSessionUploads",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "MediaReviewedByAppUserId",
                table: "FieldSessionUploads",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "MediaReviewedUtc",
                table: "FieldSessionUploads",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_FieldSessionUploads_MediaReviewState_PublishedAtUtc",
                table: "FieldSessionUploads",
                columns: new[] { "MediaReviewState", "PublishedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FieldSessionUploads_MediaReviewState_PublishedAtUtc",
                table: "FieldSessionUploads");

            migrationBuilder.DropColumn(
                name: "MediaReviewNote",
                table: "FieldSessionUploads");

            migrationBuilder.DropColumn(
                name: "MediaReviewState",
                table: "FieldSessionUploads");

            migrationBuilder.DropColumn(
                name: "MediaReviewedByAppUserId",
                table: "FieldSessionUploads");

            migrationBuilder.DropColumn(
                name: "MediaReviewedUtc",
                table: "FieldSessionUploads");
        }
    }
}
