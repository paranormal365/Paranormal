using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddInvestigationWorkflowFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "EvidenceDueDate",
                table: "Investigations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Rsvp",
                table: "InvestigationAttendees",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EvidenceDueDate",
                table: "Investigations");

            migrationBuilder.DropColumn(
                name: "Rsvp",
                table: "InvestigationAttendees");
        }
    }
}
