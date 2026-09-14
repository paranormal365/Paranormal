using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <summary>
    /// Venues, the requests groups send them, and the yeses they give (item 235 phase 9).
    /// </summary>
    /// <remarks>
    /// Three new tables and one nullable column on <c>HostedEvents</c>. Nothing existing changes: an
    /// event with no grant goes on meaning what it meant, and no place has a verified venue until a
    /// claim is proved, so no organizer is newly stopped from publishing by this migration alone.
    /// </remarks>
    public partial class VenueProfilesAndGrants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "VenueGrantId",
                table: "HostedEvents",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OrganizationVenueGrants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VenueOrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GranteeOrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ValidFrom = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ValidTo = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AllowRooms = table.Column<bool>(type: "bit", nullable: false),
                    AllowHistory = table.Column<bool>(type: "bit", nullable: false),
                    AllowStaff = table.Column<bool>(type: "bit", nullable: false),
                    RevokedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RevokedReason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationVenueGrants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationVenueGrants_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrganizationVenueGrants_AppUsers_RevokedByAppUserId",
                        column: x => x.RevokedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrganizationVenueGrants_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrganizationVenueGrants_Organizations_GranteeOrganizationId",
                        column: x => x.GranteeOrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrganizationVenueGrants_Organizations_VenueOrganizationId",
                        column: x => x.VenueOrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrganizationVenueGrants_Places_PlaceId",
                        column: x => x.PlaceId,
                        principalTable: "Places",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "OrganizationVenueProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    History = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: true),
                    HouseRules = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    MaxOvernightGuests = table.Column<int>(type: "int", nullable: true),
                    IsPublished = table.Column<bool>(type: "bit", nullable: false),
                    VerifiedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationVenueProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationVenueProfiles_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrganizationVenueProfiles_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrganizationVenueProfiles_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrganizationVenueProfiles_Places_PlaceId",
                        column: x => x.PlaceId,
                        principalTable: "Places",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "VenueHostingRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HostedEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestingOrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VenueOrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ToDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    DecidedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DecidedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DecisionNote = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    OrganizationVenueGrantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VenueHostingRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VenueHostingRequests_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_VenueHostingRequests_AppUsers_DecidedByAppUserId",
                        column: x => x.DecidedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_VenueHostingRequests_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_VenueHostingRequests_HostedEvents_HostedEventId",
                        column: x => x.HostedEventId,
                        principalTable: "HostedEvents",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_VenueHostingRequests_OrganizationVenueGrants_OrganizationVenueGrantId",
                        column: x => x.OrganizationVenueGrantId,
                        principalTable: "OrganizationVenueGrants",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_VenueHostingRequests_Organizations_RequestingOrganizationId",
                        column: x => x.RequestingOrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_VenueHostingRequests_Organizations_VenueOrganizationId",
                        column: x => x.VenueOrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_HostedEvents_VenueGrantId",
                table: "HostedEvents",
                column: "VenueGrantId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationVenueGrants_CreatedByAppUserId",
                table: "OrganizationVenueGrants",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationVenueGrants_GranteeOrganizationId",
                table: "OrganizationVenueGrants",
                column: "GranteeOrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationVenueGrants_PlaceId",
                table: "OrganizationVenueGrants",
                column: "PlaceId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationVenueGrants_RevokedByAppUserId",
                table: "OrganizationVenueGrants",
                column: "RevokedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationVenueGrants_UpdatedByAppUserId",
                table: "OrganizationVenueGrants",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationVenueGrants_VenueOrganizationId",
                table: "OrganizationVenueGrants",
                column: "VenueOrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationVenueProfiles_CreatedByAppUserId",
                table: "OrganizationVenueProfiles",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationVenueProfiles_OrganizationId_PlaceId",
                table: "OrganizationVenueProfiles",
                columns: new[] { "OrganizationId", "PlaceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationVenueProfiles_PlaceId_Verified",
                table: "OrganizationVenueProfiles",
                column: "PlaceId",
                unique: true,
                filter: "[VerifiedUtc] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationVenueProfiles_UpdatedByAppUserId",
                table: "OrganizationVenueProfiles",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_VenueHostingRequests_CreatedByAppUserId",
                table: "VenueHostingRequests",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_VenueHostingRequests_DecidedByAppUserId",
                table: "VenueHostingRequests",
                column: "DecidedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_VenueHostingRequests_HostedEventId_Pending",
                table: "VenueHostingRequests",
                column: "HostedEventId",
                unique: true,
                filter: "[Status] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_VenueHostingRequests_OrganizationVenueGrantId",
                table: "VenueHostingRequests",
                column: "OrganizationVenueGrantId");

            migrationBuilder.CreateIndex(
                name: "IX_VenueHostingRequests_RequestingOrganizationId",
                table: "VenueHostingRequests",
                column: "RequestingOrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_VenueHostingRequests_UpdatedByAppUserId",
                table: "VenueHostingRequests",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_VenueHostingRequests_VenueOrganizationId_Status",
                table: "VenueHostingRequests",
                columns: new[] { "VenueOrganizationId", "Status" });

            migrationBuilder.AddForeignKey(
                name: "FK_HostedEvents_OrganizationVenueGrants_VenueGrantId",
                table: "HostedEvents",
                column: "VenueGrantId",
                principalTable: "OrganizationVenueGrants",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_HostedEvents_OrganizationVenueGrants_VenueGrantId",
                table: "HostedEvents");

            migrationBuilder.DropTable(
                name: "OrganizationVenueProfiles");

            migrationBuilder.DropTable(
                name: "VenueHostingRequests");

            migrationBuilder.DropTable(
                name: "OrganizationVenueGrants");

            migrationBuilder.DropIndex(
                name: "IX_HostedEvents_VenueGrantId",
                table: "HostedEvents");

            migrationBuilder.DropColumn(
                name: "VenueGrantId",
                table: "HostedEvents");
        }
    }
}
