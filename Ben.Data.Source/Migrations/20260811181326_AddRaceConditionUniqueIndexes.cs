using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddRaceConditionUniqueIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OrganizationMembershipRequests_OrganizationId_AppUserId",
                table: "OrganizationMembershipRequests");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "UserMessageTypes",
                type: "nvarchar(450)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserMessageTypes_Name",
                table: "UserMessageTypes",
                column: "Name",
                unique: true,
                filter: "[Name] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMembershipRequests_OrganizationId_AppUserId",
                table: "OrganizationMembershipRequests",
                columns: new[] { "OrganizationId", "AppUserId" },
                unique: true,
                filter: "[Status] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserMessageTypes_Name",
                table: "UserMessageTypes");

            migrationBuilder.DropIndex(
                name: "IX_OrganizationMembershipRequests_OrganizationId_AppUserId",
                table: "OrganizationMembershipRequests");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "UserMessageTypes",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMembershipRequests_OrganizationId_AppUserId",
                table: "OrganizationMembershipRequests",
                columns: new[] { "OrganizationId", "AppUserId" });
        }
    }
}
