using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class TourDetailsAndGuides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowReviews",
                table: "Tours",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ContactLine",
                table: "Tours",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsBookable",
                table: "Tours",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "MailBodyTemplate",
                table: "Tours",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MailSubjectTemplate",
                table: "Tours",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TimeZoneId",
                table: "Tours",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "UrlName",
                table: "Tours",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "OrgCalendarEventGuides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrgCalendarEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrgCalendarEventGuides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrgCalendarEventGuides_AppUsers_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrgCalendarEventGuides_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrgCalendarEventGuides_OrgCalendarEvents_OrgCalendarEventId",
                        column: x => x.OrgCalendarEventId,
                        principalTable: "OrgCalendarEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TourGuides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TourId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TourGuides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TourGuides_AppUsers_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TourGuides_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TourGuides_Tours_TourId",
                        column: x => x.TourId,
                        principalTable: "Tours",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tours_OrganizationId_UrlName",
                table: "Tours",
                columns: new[] { "OrganizationId", "UrlName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrgCalendarEventGuides_AppUserId",
                table: "OrgCalendarEventGuides",
                column: "AppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrgCalendarEventGuides_CreatedByAppUserId",
                table: "OrgCalendarEventGuides",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrgCalendarEventGuides_OrgCalendarEventId_AppUserId",
                table: "OrgCalendarEventGuides",
                columns: new[] { "OrgCalendarEventId", "AppUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TourGuides_AppUserId",
                table: "TourGuides",
                column: "AppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TourGuides_CreatedByAppUserId",
                table: "TourGuides",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TourGuides_TourId_AppUserId",
                table: "TourGuides",
                columns: new[] { "TourId", "AppUserId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrgCalendarEventGuides");

            migrationBuilder.DropTable(
                name: "TourGuides");

            migrationBuilder.DropIndex(
                name: "IX_Tours_OrganizationId_UrlName",
                table: "Tours");

            migrationBuilder.DropColumn(
                name: "AllowReviews",
                table: "Tours");

            migrationBuilder.DropColumn(
                name: "ContactLine",
                table: "Tours");

            migrationBuilder.DropColumn(
                name: "IsBookable",
                table: "Tours");

            migrationBuilder.DropColumn(
                name: "MailBodyTemplate",
                table: "Tours");

            migrationBuilder.DropColumn(
                name: "MailSubjectTemplate",
                table: "Tours");

            migrationBuilder.DropColumn(
                name: "TimeZoneId",
                table: "Tours");

            migrationBuilder.DropColumn(
                name: "UrlName",
                table: "Tours");
        }
    }
}
