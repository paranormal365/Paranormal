using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <summary>
    /// Item 235 phase 12: a venue's photo library — its own pictures, and ones organizers offer from their events.
    /// </summary>
    public partial class VenuePhotos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VenuePhotos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationVenueProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UploadFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Caption = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    OfferedByOrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OfferedFromHostedEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AcceptedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VenuePhotos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VenuePhotos_HostedEvents_OfferedFromHostedEventId",
                        column: x => x.OfferedFromHostedEventId,
                        principalTable: "HostedEvents",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_VenuePhotos_OrganizationVenueProfiles_OrganizationVenueProfileId",
                        column: x => x.OrganizationVenueProfileId,
                        principalTable: "OrganizationVenueProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_VenuePhotos_Organizations_OfferedByOrganizationId",
                        column: x => x.OfferedByOrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_VenuePhotos_UploadFiles_UploadFileId",
                        column: x => x.UploadFileId,
                        principalTable: "UploadFiles",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_VenuePhotos_OfferedByOrganizationId",
                table: "VenuePhotos",
                column: "OfferedByOrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_VenuePhotos_OfferedFromHostedEventId",
                table: "VenuePhotos",
                column: "OfferedFromHostedEventId");

            migrationBuilder.CreateIndex(
                name: "IX_VenuePhotos_OrganizationVenueProfileId_UploadFileId",
                table: "VenuePhotos",
                columns: new[] { "OrganizationVenueProfileId", "UploadFileId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VenuePhotos_UploadFileId",
                table: "VenuePhotos",
                column: "UploadFileId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VenuePhotos");
        }
    }
}
