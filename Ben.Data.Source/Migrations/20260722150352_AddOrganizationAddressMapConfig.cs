using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationAddressMapConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrganizationAddressMapConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationAddressId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsOnMap = table.Column<bool>(type: "bit", nullable: false),
                    ShowMarker = table.Column<bool>(type: "bit", nullable: false),
                    ShowRegion = table.Column<bool>(type: "bit", nullable: false),
                    RegionRadiusMiles = table.Column<double>(type: "float", nullable: false),
                    MarkerColor = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    MarkerIconKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RegionFillColor = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    RegionFillOpacity = table.Column<double>(type: "float", nullable: false),
                    RegionStrokeColor = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    RegionStrokeOpacity = table.Column<double>(type: "float", nullable: false),
                    RegionStrokeWidth = table.Column<double>(type: "float", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateUpdated = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationAddressMapConfigs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationAddressMapConfigs_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrganizationAddressMapConfigs_AppUsers_UpdatedByAppUserId",
                        column: x => x.UpdatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrganizationAddressMapConfigs_OrganizationAddresses_OrganizationAddressId",
                        column: x => x.OrganizationAddressId,
                        principalTable: "OrganizationAddresses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationAddressMapConfigs_CreatedByAppUserId",
                table: "OrganizationAddressMapConfigs",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationAddressMapConfigs_OrganizationAddressId",
                table: "OrganizationAddressMapConfigs",
                column: "OrganizationAddressId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationAddressMapConfigs_UpdatedByAppUserId",
                table: "OrganizationAddressMapConfigs",
                column: "UpdatedByAppUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrganizationAddressMapConfigs");
        }
    }
}
