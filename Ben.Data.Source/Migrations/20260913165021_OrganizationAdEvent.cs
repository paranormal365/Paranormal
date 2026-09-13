using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <summary>An ad that leads to an event (item 235 phase 11): one nullable column on OrganizationAds.</summary>
    public partial class OrganizationAdEvent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "HostedEventId",
                table: "OrganizationAds",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationAds_HostedEventId",
                table: "OrganizationAds",
                column: "HostedEventId");

            migrationBuilder.AddForeignKey(
                name: "FK_OrganizationAds_HostedEvents_HostedEventId",
                table: "OrganizationAds",
                column: "HostedEventId",
                principalTable: "HostedEvents",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrganizationAds_HostedEvents_HostedEventId",
                table: "OrganizationAds");

            migrationBuilder.DropIndex(
                name: "IX_OrganizationAds_HostedEventId",
                table: "OrganizationAds");

            migrationBuilder.DropColumn(
                name: "HostedEventId",
                table: "OrganizationAds");
        }
    }
}
