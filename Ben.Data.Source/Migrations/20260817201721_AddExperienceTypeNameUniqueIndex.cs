using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <inheritdoc />
    public partial class AddExperienceTypeNameUniqueIndex : Migration
    {
        /// <inheritdoc />
        /// <remarks>
        /// The index cannot be created while duplicates exist, and duplicates are exactly what this
        /// table was free to accumulate — neither create path checked, so "Knocking" could sit
        /// beside "knocking" in one category. The fold below runs first: taggings move to the
        /// oldest row of each name, and the later rows go.
        ///
        /// <para>Written as SQL rather than as C# because a migration must run against a database
        /// whose rows this build knows nothing about, and because the whole fold has to be one
        /// statement per step — a half-applied dedupe would leave taggings pointing at a type that
        /// was about to be deleted.</para>
        /// </remarks>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Survivor per (category, lowercased name): the oldest, so the row everything else was
            // probably a re-typing of is the one that stays.
            migrationBuilder.Sql("""
                WITH Survivors AS (
                    SELECT Id,
                           ExperienceCategoryId,
                           LOWER(LTRIM(RTRIM(Name))) AS NormalizedName,
                           ROW_NUMBER() OVER (
                               PARTITION BY ExperienceCategoryId, LOWER(LTRIM(RTRIM(Name)))
                               ORDER BY DateCreated, Id) AS Rn
                    FROM ExperienceTypes
                    WHERE Name IS NOT NULL
                )
                UPDATE j
                SET j.ExperienceTypeId = keep.Id
                FROM CaseTimelineEntryExperienceTypes j
                INNER JOIN Survivors dup  ON dup.Id = j.ExperienceTypeId AND dup.Rn > 1
                INNER JOIN Survivors keep ON keep.ExperienceCategoryId = dup.ExperienceCategoryId
                                         AND keep.NormalizedName = dup.NormalizedName
                                         AND keep.Rn = 1
                -- An entry already tagged with the survivor would collide on the join's composite
                -- key, so those rows are dropped by the delete below instead of being moved.
                WHERE NOT EXISTS (
                    SELECT 1 FROM CaseTimelineEntryExperienceTypes existing
                    WHERE existing.CaseTimelineEntryId = j.CaseTimelineEntryId
                      AND existing.ExperienceTypeId = keep.Id);
                """);

            migrationBuilder.Sql("""
                WITH Duplicates AS (
                    SELECT Id,
                           ROW_NUMBER() OVER (
                               PARTITION BY ExperienceCategoryId, LOWER(LTRIM(RTRIM(Name)))
                               ORDER BY DateCreated, Id) AS Rn
                    FROM ExperienceTypes
                    WHERE Name IS NOT NULL
                )
                DELETE FROM CaseTimelineEntryExperienceTypes
                WHERE ExperienceTypeId IN (SELECT Id FROM Duplicates WHERE Rn > 1);
                """);

            migrationBuilder.Sql("""
                WITH Duplicates AS (
                    SELECT Id,
                           ROW_NUMBER() OVER (
                               PARTITION BY ExperienceCategoryId, LOWER(LTRIM(RTRIM(Name)))
                               ORDER BY DateCreated, Id) AS Rn
                    FROM ExperienceTypes
                    WHERE Name IS NOT NULL
                )
                DELETE FROM ExperienceTypes
                WHERE Id IN (SELECT Id FROM Duplicates WHERE Rn > 1);
                """);

            migrationBuilder.DropIndex(
                name: "IX_ExperienceTypes_ExperienceCategoryId",
                table: "ExperienceTypes");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "ExperienceTypes",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExperienceTypes_ExperienceCategoryId_Name",
                table: "ExperienceTypes",
                columns: new[] { "ExperienceCategoryId", "Name" },
                unique: true,
                filter: "[Name] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ExperienceTypes_ExperienceCategoryId_Name",
                table: "ExperienceTypes");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "ExperienceTypes",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExperienceTypes_ExperienceCategoryId",
                table: "ExperienceTypes",
                column: "ExperienceCategoryId");
        }
    }
}
