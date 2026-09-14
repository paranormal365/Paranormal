using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class HostedEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "HostedEventId",
                table: "OrgCalendarEvents",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "HostedEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    UrlName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Tagline = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PlaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HideExactLocation = table.Column<bool>(type: "bit", nullable: false),
                    TimeZoneId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    StartsOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndsOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DatesAreSeparate = table.Column<bool>(type: "bit", nullable: false),
                    DefaultStartLocal = table.Column<TimeSpan>(type: "time", nullable: true),
                    DefaultEndLocal = table.Column<TimeSpan>(type: "time", nullable: true),
                    IsPublished = table.Column<bool>(type: "bit", nullable: false),
                    FirstPublishedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DayPassCapacity = table.Column<int>(type: "int", nullable: true),
                    ContactLine = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CoverUploadFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MailSubjectTemplate = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    MailBodyTemplate = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CollectsEvidence = table.Column<bool>(type: "bit", nullable: false),
                    ArchivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostedEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HostedEvents_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_HostedEvents_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_HostedEvents_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_HostedEvents_Places_PlaceId",
                        column: x => x.PlaceId,
                        principalTable: "Places",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "HostedEventNights",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HostedEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    StartLocal = table.Column<TimeSpan>(type: "time", nullable: true),
                    EndLocal = table.Column<TimeSpan>(type: "time", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostedEventNights", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HostedEventNights_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_HostedEventNights_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_HostedEventNights_HostedEvents_HostedEventId",
                        column: x => x.HostedEventId,
                        principalTable: "HostedEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrgCalendarEvents_HostedEventId",
                table: "OrgCalendarEvents",
                column: "HostedEventId",
                unique: true,
                filter: "[HostedEventId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventNights_CreatedByAppUserId",
                table: "HostedEventNights",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventNights_HostedEventId_Date",
                table: "HostedEventNights",
                columns: new[] { "HostedEventId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HostedEventNights_UpdatedByAppUserId",
                table: "HostedEventNights",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_HostedEvents_CreatedByAppUserId",
                table: "HostedEvents",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_HostedEvents_OrganizationId_IsPublished_ArchivedAtUtc",
                table: "HostedEvents",
                columns: new[] { "OrganizationId", "IsPublished", "ArchivedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_HostedEvents_OrganizationId_Name",
                table: "HostedEvents",
                columns: new[] { "OrganizationId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HostedEvents_OrganizationId_UrlName",
                table: "HostedEvents",
                columns: new[] { "OrganizationId", "UrlName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HostedEvents_PlaceId",
                table: "HostedEvents",
                column: "PlaceId");

            migrationBuilder.CreateIndex(
                name: "IX_HostedEvents_UpdatedByAppUserId",
                table: "HostedEvents",
                column: "UpdatedByAppUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_OrgCalendarEvents_HostedEvents_HostedEventId",
                table: "OrgCalendarEvents",
                column: "HostedEventId",
                principalTable: "HostedEvents",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrgCalendarEvents_HostedEvents_HostedEventId",
                table: "OrgCalendarEvents");

            migrationBuilder.DropTable(
                name: "HostedEventNights");

            migrationBuilder.DropTable(
                name: "HostedEvents");

            migrationBuilder.DropIndex(
                name: "IX_OrgCalendarEvents_HostedEventId",
                table: "OrgCalendarEvents");

            migrationBuilder.DropColumn(
                name: "HostedEventId",
                table: "OrgCalendarEvents");
        }
    }
}
