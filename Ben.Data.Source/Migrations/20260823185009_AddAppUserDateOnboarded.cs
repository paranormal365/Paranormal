using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddAppUserDateOnboarded : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DateOnboarded",
                table: "AppUsers",
                type: "datetime2",
                nullable: true);

            // Everyone who exists before this column is already onboard — the wizard exists for
            // people meeting the site cold, and a first-run nag aimed at a long-standing member
            // would be worse than no wizard. Stamped HERE, in the same migration, so there is no
            // window where an existing account reads as un-onboarded.
            migrationBuilder.Sql("UPDATE AppUsers SET DateOnboarded = GETUTCDATE() WHERE DateOnboarded IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DateOnboarded",
                table: "AppUsers");
        }
    }
}
