using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceActionNameWithActionsBitmask : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OrganizationAccessGrants_OrganizationId_AppUserId_TableName_ActionName",
                table: "OrganizationAccessGrants");

            migrationBuilder.DropColumn(
                name: "IsAllowed",
                table: "OrganizationAccessGrants");

            migrationBuilder.RenameColumn(
                name: "ActionName",
                table: "OrganizationAccessGrants",
                newName: "Actions");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationAccessGrants_OrganizationId_AppUserId_TableName",
                table: "OrganizationAccessGrants",
                columns: new[] { "OrganizationId", "AppUserId", "TableName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OrganizationAccessGrants_OrganizationId_AppUserId_TableName",
                table: "OrganizationAccessGrants");

            migrationBuilder.RenameColumn(
                name: "Actions",
                table: "OrganizationAccessGrants",
                newName: "ActionName");

            migrationBuilder.AddColumn<bool>(
                name: "IsAllowed",
                table: "OrganizationAccessGrants",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationAccessGrants_OrganizationId_AppUserId_TableName_ActionName",
                table: "OrganizationAccessGrants",
                columns: new[] { "OrganizationId", "AppUserId", "TableName", "ActionName" },
                unique: true);
        }
    }
}
