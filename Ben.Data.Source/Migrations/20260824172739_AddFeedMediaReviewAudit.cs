using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddFeedMediaReviewAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MediaReviewNote",
                table: "OrgMessages",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MediaReviewedByAppUserId",
                table: "OrgMessages",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "MediaReviewedUtc",
                table: "OrgMessages",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MediaReviewNote",
                table: "OrgMessages");

            migrationBuilder.DropColumn(
                name: "MediaReviewedByAppUserId",
                table: "OrgMessages");

            migrationBuilder.DropColumn(
                name: "MediaReviewedUtc",
                table: "OrgMessages");
        }
    }
}
