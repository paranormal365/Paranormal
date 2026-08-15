using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <summary>
    /// Widens the investigation coordinate columns to the precision every other coordinate column
    /// in the schema already uses, and recovers the values already truncated.
    /// </summary>
    /// <remarks>
    /// <para><c>AddInvestigationCoordinates</c> created these without <c>HasPrecision</c>, so they
    /// got SQL Server's default <c>decimal(18,2)</c> — about 1.1km. Every sibling coordinate column
    /// is <c>decimal(18,10)</c>. Nothing wrote them until P2, so the truncation had never shown up.</para>
    ///
    /// <para>Altering the type does not un-round what was already rounded, so the second statement
    /// re-copies from the investigation's place, which kept the full value. Rows with no place keep
    /// what they have.</para>
    /// </remarks>
    public partial class FixInvestigationCoordinatePrecision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "Longitude",
                table: "Investigations",
                type: "decimal(18,10)",
                precision: 18,
                scale: 10,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "Latitude",
                table: "Investigations",
                type: "decimal(18,10)",
                precision: 18,
                scale: 10,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldNullable: true);

            // Recover the precision that was lost on the way in. Restricted to rows whose stored
            // value actually differs from the place's, so it touches only what was damaged.
            migrationBuilder.Sql("""
                UPDATE i
                SET Latitude  = p.Latitude,
                    Longitude = p.Longitude
                FROM Investigations i
                INNER JOIN Places p ON p.Id = i.PlaceId
                WHERE p.Latitude IS NOT NULL
                  AND p.Longitude IS NOT NULL
                  AND (i.Latitude <> p.Latitude OR i.Longitude <> p.Longitude);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "Longitude",
                table: "Investigations",
                type: "decimal(18,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,10)",
                oldPrecision: 18,
                oldScale: 10,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "Latitude",
                table: "Investigations",
                type: "decimal(18,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,10)",
                oldPrecision: 18,
                oldScale: 10,
                oldNullable: true);
        }
    }
}
