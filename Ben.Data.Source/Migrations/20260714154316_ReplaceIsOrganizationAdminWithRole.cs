using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceIsOrganizationAdminWithRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add Role with a safe default (4 = Member)
            migrationBuilder.AddColumn<int>(
                name: "Role",
                table: "OrganizationUserMemberships",
                type: "int",
                nullable: false,
                defaultValue: 4);

            // Migrate data: IsOrganizationAdmin = true → Owner (1), false → Member (4)
            migrationBuilder.Sql(
                "UPDATE OrganizationUserMemberships SET Role = CASE WHEN IsOrganizationAdmin = 1 THEN 1 ELSE 4 END");

            migrationBuilder.DropColumn(
                name: "IsOrganizationAdmin",
                table: "OrganizationUserMemberships");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Role",
                table: "OrganizationUserMemberships");

            migrationBuilder.AddColumn<bool>(
                name: "IsOrganizationAdmin",
                table: "OrganizationUserMemberships",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }
    }
}
