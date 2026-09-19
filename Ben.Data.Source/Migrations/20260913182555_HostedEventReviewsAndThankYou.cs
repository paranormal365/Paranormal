using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <summary>
    /// Item 235 phase 12: reviews of hosted events, and the thank-you letter. Existing events keep both
    /// switched on (defaults written by hand as true).
    /// </summary>
    public partial class HostedEventReviewsAndThankYou : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowReviews",
                table: "HostedEvents",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "SendThankYou",
                table: "HostedEvents",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "ThankYouNote",
                table: "HostedEvents",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ThankYouSentUtc",
                table: "HostedEvents",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ThankedUtc",
                table: "HostedEventBookings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "HostedEventReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HostedEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Stars = table.Column<int>(type: "int", nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    HiddenAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    HiddenByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostedEventReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HostedEventReviews_AppUsers_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_HostedEventReviews_HostedEvents_HostedEventId",
                        column: x => x.HostedEventId,
                        principalTable: "HostedEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventReviews_AppUserId",
                table: "HostedEventReviews",
                column: "AppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventReviews_HostedEventId_AppUserId",
                table: "HostedEventReviews",
                columns: new[] { "HostedEventId", "AppUserId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HostedEventReviews");

            migrationBuilder.DropColumn(
                name: "AllowReviews",
                table: "HostedEvents");

            migrationBuilder.DropColumn(
                name: "SendThankYou",
                table: "HostedEvents");

            migrationBuilder.DropColumn(
                name: "ThankYouNote",
                table: "HostedEvents");

            migrationBuilder.DropColumn(
                name: "ThankYouSentUtc",
                table: "HostedEvents");

            migrationBuilder.DropColumn(
                name: "ThankedUtc",
                table: "HostedEventBookings");
        }
    }
}
