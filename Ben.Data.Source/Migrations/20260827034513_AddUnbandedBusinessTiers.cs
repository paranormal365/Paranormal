using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddUnbandedBusinessTiers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsBandedByMembers",
                table: "SubscriptionTiers",
                type: "bit",
                nullable: false,
                // TRUE, not EF's bool default of false. Every tier that exists today IS a ladder
                // band, and defaulting to false silently emptied the ladder — the next price-list
                // validation answered "there are no active price bands, so no organization can be
                // priced", which is a 400 on every tier edit and a 503 on every quote.
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsBandedByMembers",
                table: "SubscriptionTiers");
        }
    }
}
