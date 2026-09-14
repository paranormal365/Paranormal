using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class HostedEventGoNoGoDecidedBy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_HostedEvents_GoNoGoDecidedByAppUserId",
                table: "HostedEvents",
                column: "GoNoGoDecidedByAppUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_HostedEvents_AppUsers_GoNoGoDecidedByAppUserId",
                table: "HostedEvents",
                column: "GoNoGoDecidedByAppUserId",
                principalTable: "AppUsers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_HostedEvents_AppUsers_GoNoGoDecidedByAppUserId",
                table: "HostedEvents");

            migrationBuilder.DropIndex(
                name: "IX_HostedEvents_GoNoGoDecidedByAppUserId",
                table: "HostedEvents");
        }
    }
}
