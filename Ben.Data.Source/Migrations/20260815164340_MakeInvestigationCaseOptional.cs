using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <summary>
    /// Lets an investigation exist without a case, and gives it a direct owning organization.
    /// </summary>
    /// <remarks>
    /// <para>The scaffolded version of this migration added <c>OrganizationId</c> as
    /// <c>NOT NULL DEFAULT '00000000-…'</c> and then added the foreign key in the same breath.
    /// That works on an empty table and fails on a real one: every existing row would be handed
    /// an organization id that does not exist, and the FK would reject it. Hence the four-step
    /// order below — add nullable, fill it in from the case, tighten to NOT NULL, and only then
    /// add the constraint that the data now satisfies.</para>
    ///
    /// <para>The backfill is raw SQL and so invisible to the InMemory provider used by the tests.
    /// Nothing in the suite may depend on it.</para>
    /// </remarks>
    public partial class MakeInvestigationCaseOptional : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Nullable to begin with, so existing rows survive the addition.
            migrationBuilder.AddColumn<Guid>(
                name: "OrganizationId",
                table: "Investigations",
                type: "uniqueidentifier",
                nullable: true);

            // 2. Every investigation that exists today reaches an organization through its case —
            //    that is precisely the indirection this column removes.
            migrationBuilder.Sql("""
                UPDATE i
                SET OrganizationId = c.OrganizationId
                FROM Investigations i
                INNER JOIN Cases c ON c.Id = i.CaseId
                WHERE i.OrganizationId IS NULL;
                """);

            // 3. Now that every row has one, require it. An investigation nobody owns is not a
            //    state worth being able to represent.
            migrationBuilder.Sql(
                "ALTER TABLE Investigations ALTER COLUMN OrganizationId uniqueidentifier NOT NULL;");

            // 4. CaseId becomes optional only after the organization is secured, so there is never
            //    a moment where a row could have neither.
            migrationBuilder.AlterColumn<Guid>(
                name: "CaseId",
                table: "Investigations",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.CreateIndex(
                name: "IX_Investigations_OrganizationId",
                table: "Investigations",
                column: "OrganizationId");

            // NoAction: Investigations already reach AppUser through their created-by columns, and
            // SQL Server refuses a second cascade path (error 1785).
            migrationBuilder.AddForeignKey(
                name: "FK_Investigations_Organizations_OrganizationId",
                table: "Investigations",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.NoAction);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Case-less investigations cannot exist in the old shape at all. Removing them is the
            // only honest reversal — leaving them with a zeroed CaseId would produce rows that
            // point at a case which does not exist, and every screen would show them as broken
            // rather than absent.
            migrationBuilder.Sql("""
                DELETE FROM InvestigationAttendees
                WHERE InvestigationId IN (SELECT Id FROM Investigations WHERE CaseId IS NULL);
                """);
            migrationBuilder.Sql("DELETE FROM Investigations WHERE CaseId IS NULL;");

            migrationBuilder.DropForeignKey(
                name: "FK_Investigations_Organizations_OrganizationId",
                table: "Investigations");

            migrationBuilder.DropIndex(
                name: "IX_Investigations_OrganizationId",
                table: "Investigations");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "Investigations");

            migrationBuilder.AlterColumn<Guid>(
                name: "CaseId",
                table: "Investigations",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);
        }
    }
}
