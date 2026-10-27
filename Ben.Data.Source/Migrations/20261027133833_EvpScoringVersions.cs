using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class EvpScoringVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ScoringVersion",
                table: "EvpScans",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "EvpScoringVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    WeightProminence = table.Column<double>(type: "float", nullable: false),
                    WeightBand = table.Column<double>(type: "float", nullable: false),
                    WeightDuration = table.Column<double>(type: "float", nullable: false),
                    Decisions = table.Column<int>(type: "int", nullable: false),
                    Recordings = table.Column<int>(type: "int", nullable: false),
                    HeldOutDecisions = table.Column<int>(type: "int", nullable: false),
                    TrainAuc = table.Column<double>(type: "float", nullable: false),
                    HeldOutAuc = table.Column<double>(type: "float", nullable: false),
                    BaselineHeldOutAuc = table.Column<double>(type: "float", nullable: false),
                    PassedGate = table.Column<bool>(type: "bit", nullable: false),
                    GateNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ActivatedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ActivatedByAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvpScoringVersions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EvpScoringVersions_OneActive",
                table: "EvpScoringVersions",
                column: "IsActive",
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_EvpScoringVersions_Version",
                table: "EvpScoringVersions",
                column: "Version",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EvpScoringVersions");

            migrationBuilder.DropColumn(
                name: "ScoringVersion",
                table: "EvpScans");
        }
    }
}
