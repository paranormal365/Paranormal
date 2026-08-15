using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <summary>
    /// Adds the sharing scope, defaulting every existing investigation to group-only.
    /// </summary>
    /// <remarks>
    /// The scaffolded default was 0, which is not a member of <c>InvestigationVisibility</c> — the
    /// enum starts at 1 so that an unset value is obvious rather than silently meaning the first
    /// option. Existing rows take GroupOnly (1): they were written when the only audience was the
    /// group that ran them, so anything wider would widen a scope nobody chose.
    /// </remarks>
    public partial class AddInvestigationVisibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Visibility",
                table: "Investigations",
                type: "int",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Visibility",
                table: "Investigations");
        }
    }
}
