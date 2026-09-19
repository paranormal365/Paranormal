using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddRateLimitRefusalTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RateLimitRefusals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PolicyName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Refusals = table.Column<long>(type: "bigint", nullable: false),
                    DistinctCallers = table.Column<int>(type: "int", nullable: false),
                    PeakDistinctCallers = table.Column<int>(type: "int", nullable: false),
                    DateFirstSeen = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateLastSeen = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DateNotified = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RateLimitRefusals", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RateLimitRefusals_PolicyName",
                table: "RateLimitRefusals",
                column: "PolicyName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RateLimitRefusals");
        }
    }
}
