using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddOrgMediaStrippingSetting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "StripMediaMetadata",
                table: "Organizations",
                type: "bit",
                nullable: false,
                // Item 181: existing groups get it ON. A privacy protection nobody has to
                // discover is worth more than one everybody has to find and switch on; the tier
                // capability still decides whether the preference can take effect.
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StripMediaMetadata",
                table: "Organizations");
        }
    }
}
