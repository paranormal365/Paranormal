using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <summary>
    /// A place's contact details, and groups' claims to run a place (item 235 phase 9).
    /// </summary>
    /// <remarks>
    /// Two new tables and nothing else. No place gains a confirmed venue by this migration; that
    /// only happens when a claim is proved and its week for objections has passed, or a person
    /// approves it.
    /// </remarks>
    public partial class PlaceContactsAndVenueClaims : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlaceContacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    IsPublic = table.Column<bool>(type: "bit", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ConfirmedByVenueUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaceContacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlaceContacts_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PlaceContacts_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PlaceContacts_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PlaceContacts_Places_PlaceId",
                        column: x => x.PlaceId,
                        principalTable: "Places",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "VenuePlaceClaims",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClaimantAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClaimantRole = table.Column<int>(type: "int", nullable: false),
                    Evidence = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    State = table.Column<int>(type: "int", nullable: false),
                    PlaceContactId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CodeHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CodeSentUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CodeAttempts = table.Column<int>(type: "int", nullable: false),
                    ProvedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ObjectionsCloseUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ObjectedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ObjectedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ObjectingOrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ObjectionText = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    DecidedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DecidedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DecisionNote = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VenuePlaceClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VenuePlaceClaims_AppUsers_ClaimantAppUserId",
                        column: x => x.ClaimantAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_VenuePlaceClaims_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_VenuePlaceClaims_AppUsers_DecidedByAppUserId",
                        column: x => x.DecidedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_VenuePlaceClaims_AppUsers_ObjectedByAppUserId",
                        column: x => x.ObjectedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_VenuePlaceClaims_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_VenuePlaceClaims_Organizations_ObjectingOrganizationId",
                        column: x => x.ObjectingOrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_VenuePlaceClaims_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_VenuePlaceClaims_PlaceContacts_PlaceContactId",
                        column: x => x.PlaceContactId,
                        principalTable: "PlaceContacts",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_VenuePlaceClaims_Places_PlaceId",
                        column: x => x.PlaceId,
                        principalTable: "Places",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlaceContacts_CreatedByAppUserId",
                table: "PlaceContacts",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PlaceContacts_OrganizationId",
                table: "PlaceContacts",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_PlaceContacts_PlaceId_IsPublic",
                table: "PlaceContacts",
                columns: new[] { "PlaceId", "IsPublic" });

            migrationBuilder.CreateIndex(
                name: "IX_PlaceContacts_UpdatedByAppUserId",
                table: "PlaceContacts",
                column: "UpdatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_VenuePlaceClaims_ClaimantAppUserId",
                table: "VenuePlaceClaims",
                column: "ClaimantAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_VenuePlaceClaims_CreatedByAppUserId",
                table: "VenuePlaceClaims",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_VenuePlaceClaims_DecidedByAppUserId",
                table: "VenuePlaceClaims",
                column: "DecidedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_VenuePlaceClaims_ObjectedByAppUserId",
                table: "VenuePlaceClaims",
                column: "ObjectedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_VenuePlaceClaims_ObjectingOrganizationId",
                table: "VenuePlaceClaims",
                column: "ObjectingOrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_VenuePlaceClaims_OrganizationId",
                table: "VenuePlaceClaims",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_VenuePlaceClaims_PlaceContactId",
                table: "VenuePlaceClaims",
                column: "PlaceContactId");

            migrationBuilder.CreateIndex(
                name: "IX_VenuePlaceClaims_PlaceId_OrganizationId_Open",
                table: "VenuePlaceClaims",
                columns: new[] { "PlaceId", "OrganizationId" },
                unique: true,
                filter: "[State] IN (0, 1, 5)");

            migrationBuilder.CreateIndex(
                name: "IX_VenuePlaceClaims_State_ObjectionsCloseUtc",
                table: "VenuePlaceClaims",
                columns: new[] { "State", "ObjectionsCloseUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_VenuePlaceClaims_UpdatedByAppUserId",
                table: "VenuePlaceClaims",
                column: "UpdatedByAppUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VenuePlaceClaims");

            migrationBuilder.DropTable(
                name: "PlaceContacts");
        }
    }
}
