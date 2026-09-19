using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class DashboardDateIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Cases_DateCreated",
                table: "Cases",
                column: "DateCreated");

            migrationBuilder.CreateIndex(
                name: "IX_AppUsers_DateCreated",
                table: "AppUsers",
                column: "DateCreated");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Cases_DateCreated",
                table: "Cases");

            migrationBuilder.DropIndex(
                name: "IX_AppUsers_DateCreated",
                table: "AppUsers");
        }
    }
}
