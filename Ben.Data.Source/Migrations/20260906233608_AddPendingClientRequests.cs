using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddPendingClientRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PendingClientRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    SecretHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    StreetAddress1 = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    StreetAddress2 = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    City = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    State = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ZipCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Country = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Latitude = table.Column<decimal>(type: "decimal(18,10)", precision: 18, scale: 10, nullable: true),
                    Longitude = table.Column<decimal>(type: "decimal(18,10)", precision: 18, scale: 10, nullable: true),
                    Gender = table.Column<int>(type: "int", nullable: false),
                    BirthYear = table.Column<int>(type: "int", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OrganizationIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateExpires = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingClientRequests", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PendingClientRequests_NormalizedEmail",
                table: "PendingClientRequests",
                column: "NormalizedEmail");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PendingClientRequests");
        }
    }
}
