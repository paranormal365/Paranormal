using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ben.Data.Source.Migrations
{
    /// <summary>
    /// Gives every existing case a <c>Place</c> carrying the address it already had.
    /// </summary>
    /// <remarks>
    /// <para>Raw SQL rather than a data-seeding API, because this has to run once against real
    /// rows and never again. <b>It is therefore invisible to the EF InMemory provider, so no test
    /// may depend on it</b> — anything asserting backfilled data would pass in the test suite and
    /// tell you nothing about the database.</para>
    ///
    /// <para>Every backfilled place is <c>PrivateResidence</c> (1). That is the safe default, not a
    /// claim of fact: some of these addresses are certainly businesses or landmarks, but guessing
    /// wrong towards "public" would widen the sharing scope of somebody's home, while guessing
    /// wrong towards "private" only means an organization has to change a setting. The error that
    /// can be corrected is the one to make.</para>
    ///
    /// <para>Guarded by <c>PlaceId IS NULL</c> so re-running is harmless, and it deliberately does
    /// not touch cases that already have one.</para>
    /// </remarks>
    public partial class BackfillPlacesFromCases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // One place per case, coordinates carried across where the case already had them.
            // DateGeocoded is only set when there are coordinates to have been geocoded — an
            // address that was never resolved must not look like one that was.
            migrationBuilder.Sql("""
                INSERT INTO Places (
                    Id, Name, StreetAddress1, StreetAddress2, City, State, ZipCode, Country,
                    Latitude, Longitude, GeocodeNote, DateGeocoded, Kind, IsApproved,
                    DateCreated, DateUpdated, CreatedByAppUserId, UpdatedByAppUserId)
                SELECT
                    NEWID(), NULL, c.StreetAddress1, c.StreetAddress2, c.City, c.State, c.ZipCode,
                    c.Country, c.Latitude, c.Longitude, NULL,
                    CASE WHEN c.Latitude IS NOT NULL AND c.Longitude IS NOT NULL
                         THEN c.DateCreated ELSE NULL END,
                    1, 0,
                    c.DateCreated, NULL, c.CreatedByAppUserId, NULL
                FROM Cases c
                WHERE c.PlaceId IS NULL;
                """);

            // Match each case back to the row just created for it. Cases are matched on the whole
            // address plus the creating user and creation timestamp, which is what the insert above
            // copied — two cases at genuinely the same address get one place each rather than
            // sharing, which is correct here: deduplication is a later, deliberate design pass
            // (P8), and silently merging two organizations' cases into one place on a migration
            // would be exactly the kind of guess that pass exists to avoid.
            migrationBuilder.Sql("""
                UPDATE c
                SET PlaceId = p.Id
                FROM Cases c
                INNER JOIN Places p
                    ON  p.DateCreated        = c.DateCreated
                    AND p.CreatedByAppUserId = c.CreatedByAppUserId
                    AND ISNULL(p.StreetAddress1, '') = ISNULL(c.StreetAddress1, '')
                    AND ISNULL(p.City,           '') = ISNULL(c.City,           '')
                    AND ISNULL(p.State,          '') = ISNULL(c.State,          '')
                    AND ISNULL(p.ZipCode,        '') = ISNULL(c.ZipCode,        '')
                WHERE c.PlaceId IS NULL;
                """);

            // Investigations inherit the place of the case they belong to. Their own Location text
            // and coordinates are left alone — a team often works somewhere other than the address
            // on file, and overwriting that would lose where they actually were.
            migrationBuilder.Sql("""
                UPDATE i
                SET PlaceId = c.PlaceId
                FROM Investigations i
                INNER JOIN Cases c ON c.Id = i.CaseId
                WHERE i.PlaceId IS NULL AND c.PlaceId IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Unlink first, then remove only the places this migration could have created — a
            // place with a name was made by a person, not by the backfill, and deleting it would
            // destroy data this migration never owned.
            migrationBuilder.Sql("UPDATE Investigations SET PlaceId = NULL;");
            migrationBuilder.Sql("UPDATE Cases SET PlaceId = NULL;");
            migrationBuilder.Sql("DELETE FROM Places WHERE Name IS NULL;");
        }
    }
}
