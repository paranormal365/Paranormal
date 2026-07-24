using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddAddressVisibilityAndOrgSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ShowAddressDirections",
                table: "Organizations",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ShowAddressMap",
                table: "Organizations",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsSearchable",
                table: "OrganizationAddresses",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MemberDisplayMode",
                table: "OrganizationAddresses",
                type: "int",
                nullable: false,
                defaultValue: 2); // FullAddressAndMap

            migrationBuilder.AddColumn<int>(
                name: "PublicDisplayMode",
                table: "OrganizationAddresses",
                type: "int",
                nullable: false,
                defaultValue: 4); // Hidden

            migrationBuilder.AddColumn<double>(
                name: "SearchRadiusMiles",
                table: "OrganizationAddresses",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SearchVisibility",
                table: "OrganizationAddresses",
                type: "int",
                nullable: false,
                defaultValue: 0); // Public

            migrationBuilder.AddColumn<int>(
                name: "Visibility",
                table: "OrganizationAddresses",
                type: "int",
                nullable: false,
                defaultValue: 3); // Private

            // Migrate IsPublic → Visibility (Public=0, Private=3)
            migrationBuilder.Sql("UPDATE OrganizationAddresses SET Visibility = CASE WHEN IsPublic = 1 THEN 0 ELSE 3 END");
            migrationBuilder.Sql("UPDATE OrganizationAddresses SET PublicDisplayMode = CASE WHEN IsPublic = 1 THEN 0 ELSE 4 END");

            migrationBuilder.DropColumn(
                name: "IsPublic",
                table: "OrganizationAddresses");

            migrationBuilder.CreateTable(
                name: "OrganizationAddressMemberAccesses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationAddressId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationUserMembershipId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationAddressMemberAccesses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationAddressMemberAccesses_AppUsers_CreatedByAppUserId",
                        column: x => x.CreatedByAppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrganizationAddressMemberAccesses_OrganizationAddresses_OrganizationAddressId",
                        column: x => x.OrganizationAddressId,
                        principalTable: "OrganizationAddresses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrganizationAddressMemberAccesses_OrganizationUserMemberships_OrganizationUserMembershipId",
                        column: x => x.OrganizationUserMembershipId,
                        principalTable: "OrganizationUserMemberships",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationAddressMemberAccesses_CreatedByAppUserId",
                table: "OrganizationAddressMemberAccesses",
                column: "CreatedByAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationAddressMemberAccesses_OrganizationAddressId_OrganizationUserMembershipId",
                table: "OrganizationAddressMemberAccesses",
                columns: new[] { "OrganizationAddressId", "OrganizationUserMembershipId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationAddressMemberAccesses_OrganizationUserMembershipId",
                table: "OrganizationAddressMemberAccesses",
                column: "OrganizationUserMembershipId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrganizationAddressMemberAccesses");

            migrationBuilder.AddColumn<bool>(
                name: "IsPublic",
                table: "OrganizationAddresses",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("UPDATE OrganizationAddresses SET IsPublic = CASE WHEN Visibility = 0 THEN 1 ELSE 0 END");

            migrationBuilder.DropColumn(
                name: "ShowAddressDirections",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "ShowAddressMap",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "IsSearchable",
                table: "OrganizationAddresses");

            migrationBuilder.DropColumn(
                name: "MemberDisplayMode",
                table: "OrganizationAddresses");

            migrationBuilder.DropColumn(
                name: "PublicDisplayMode",
                table: "OrganizationAddresses");

            migrationBuilder.DropColumn(
                name: "SearchRadiusMiles",
                table: "OrganizationAddresses");

            migrationBuilder.DropColumn(
                name: "SearchVisibility",
                table: "OrganizationAddresses");

            migrationBuilder.DropColumn(
                name: "Visibility",
                table: "OrganizationAddresses");
        }
    }
}
