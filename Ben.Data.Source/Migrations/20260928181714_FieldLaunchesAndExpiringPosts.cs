using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class FieldLaunchesAndExpiringPosts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresUtc",
                table: "OrgMessages",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FieldLaunchId",
                table: "OrgMessages",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FieldLaunches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Target = table.Column<int>(type: "int", nullable: false),
                    InvestigationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OrgCalendarEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    HostedEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    LocationLabel = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    LaunchedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LaunchedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndsUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsPublic = table.Column<bool>(type: "bit", nullable: false),
                    FeedPostId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PeopleCount = table.Column<int>(type: "int", nullable: false),
                    PeopleWithTheApp = table.Column<int>(type: "int", nullable: false),
                    PhonesReached = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FieldLaunches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FieldLaunchRecipients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FieldLaunchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FieldLaunchRecipients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FieldLaunchRecipients_FieldLaunches_FieldLaunchId",
                        column: x => x.FieldLaunchId,
                        principalTable: "FieldLaunches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FieldLaunches_ExpiresUtc",
                table: "FieldLaunches",
                column: "ExpiresUtc");

            migrationBuilder.CreateIndex(
                name: "IX_FieldLaunches_Target_InvestigationId_OrgCalendarEventId_HostedEventId",
                table: "FieldLaunches",
                columns: new[] { "Target", "InvestigationId", "OrgCalendarEventId", "HostedEventId" });

            migrationBuilder.CreateIndex(
                name: "IX_FieldLaunchRecipients_AppUserId_FieldLaunchId",
                table: "FieldLaunchRecipients",
                columns: new[] { "AppUserId", "FieldLaunchId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FieldLaunchRecipients_FieldLaunchId",
                table: "FieldLaunchRecipients",
                column: "FieldLaunchId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FieldLaunchRecipients");

            migrationBuilder.DropTable(
                name: "FieldLaunches");

            migrationBuilder.DropColumn(
                name: "ExpiresUtc",
                table: "OrgMessages");

            migrationBuilder.DropColumn(
                name: "FieldLaunchId",
                table: "OrgMessages");
        }
    }
}
