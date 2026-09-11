using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class Tours : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TourId",
                table: "OrgCalendarEvents",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TourCountAtPeriodStart",
                table: "OrganizationSubscriptions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "Tours",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StartOrganizationAddressId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DurationMinutes = table.Column<int>(type: "int", nullable: true),
                    DefaultCapacity = table.Column<int>(type: "int", nullable: true),
                    RetiredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tours", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Tours_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Tours_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Tours_OrganizationAddresses_StartOrganizationAddressId",
                        column: x => x.StartOrganizationAddressId,
                        principalTable: "OrganizationAddresses",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Tours_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrgCalendarEvents_TourId",
                table: "OrgCalendarEvents",
                column: "TourId");

            migrationBuilder.CreateIndex(
                name: "IX_Tours_CreatedByAppUserId",
                table: "Tours",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Tours_OrganizationId_Name",
                table: "Tours",
                columns: new[] { "OrganizationId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tours_StartOrganizationAddressId",
                table: "Tours",
                column: "StartOrganizationAddressId");

            migrationBuilder.CreateIndex(
                name: "IX_Tours_UpdatedByAppUserId",
                table: "Tours",
                column: "UpdatedByAppUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_OrgCalendarEvents_Tours_TourId",
                table: "OrgCalendarEvents",
                column: "TourId",
                principalTable: "Tours",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrgCalendarEvents_Tours_TourId",
                table: "OrgCalendarEvents");

            migrationBuilder.DropTable(
                name: "Tours");

            migrationBuilder.DropIndex(
                name: "IX_OrgCalendarEvents_TourId",
                table: "OrgCalendarEvents");

            migrationBuilder.DropColumn(
                name: "TourId",
                table: "OrgCalendarEvents");

            migrationBuilder.DropColumn(
                name: "TourCountAtPeriodStart",
                table: "OrganizationSubscriptions");
        }
    }
}
