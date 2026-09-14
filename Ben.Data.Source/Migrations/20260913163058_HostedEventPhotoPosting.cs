using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <summary>
    /// Who may add photos to an event's room and photo wall (item 235 phase 11). One column; zero is
    /// "the team and confirmed guests", which is what every existing room already allowed.
    /// </summary>
    public partial class HostedEventPhotoPosting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PhotoPosting",
                table: "HostedEvents",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PhotoPosting",
                table: "HostedEvents");
        }
    }
}
