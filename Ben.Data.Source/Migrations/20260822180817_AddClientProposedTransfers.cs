using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddClientProposedTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ProposedByClient",
                table: "CaseTransferLogs",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ShareHistory",
                table: "CaseTransferLogs",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ShareInvestigations",
                table: "CaseTransferLogs",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProposedByClient",
                table: "CaseTransferLogs");

            migrationBuilder.DropColumn(
                name: "ShareHistory",
                table: "CaseTransferLogs");

            migrationBuilder.DropColumn(
                name: "ShareInvestigations",
                table: "CaseTransferLogs");
        }
    }
}
