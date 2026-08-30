using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddMemberSeatProviderRefs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProviderCustomerRef",
                table: "MemberSeatSubscriptions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderName",
                table: "MemberSeatSubscriptions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderPaymentMethodRef",
                table: "MemberSeatSubscriptions",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProviderCustomerRef",
                table: "MemberSeatSubscriptions");

            migrationBuilder.DropColumn(
                name: "ProviderName",
                table: "MemberSeatSubscriptions");

            migrationBuilder.DropColumn(
                name: "ProviderPaymentMethodRef",
                table: "MemberSeatSubscriptions");
        }
    }
}
