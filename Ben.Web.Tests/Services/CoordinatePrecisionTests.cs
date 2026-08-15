using Ben.Data.Source.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Every latitude and longitude column must be declared with enough precision to be a location.
/// </summary>
/// <remarks>
/// <para>An unconfigured <c>decimal</c> becomes <c>decimal(18,2)</c> on SQL Server, which for a
/// coordinate is about 1.1km. Nothing warns; the number still looks like a number.</para>
///
/// <para><c>Investigation.Latitude</c> and <c>Longitude</c> were exactly that until P4, while every
/// sibling column was <c>decimal(18,10)</c> — 36.5893 stored as 36.59. Caught by reading a live API
/// response rather than by any test, hence this one.</para>
///
/// <para>Checks the EF model, not the database, so it fails when the property is added rather than
/// whenever someone next looks closely at a map.</para>
/// </remarks>
public class CoordinatePrecisionTests
{
    /// <summary>Ten decimal places — roughly a hundredth of a millimetre, and the house standard.</summary>
    private const int RequiredScale = 10;

    private static readonly string[] CoordinateNames = ["Latitude", "Longitude", "CenterLatitude", "CenterLongitude"];

    [Fact]
    public void Every_coordinate_property_declares_enough_precision()
    {
        using var db = new BenDataContext(
            new DbContextOptionsBuilder<BenDataContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);

        var offenders = new List<string>();
        var checkedCount = 0;

        foreach (var entity in db.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                if (!CoordinateNames.Contains(property.Name)) continue;
                if (property.ClrType != typeof(decimal) && property.ClrType != typeof(decimal?)) continue;

                checkedCount++;
                var scale = property.GetScale();

                if (scale is null)
                    offenders.Add(
                        $"{entity.ShortName()}.{property.Name} declares no precision, so SQL Server " +
                        $"will use decimal(18,2) — about 1.1km.");
                else if (scale < RequiredScale)
                    offenders.Add(
                        $"{entity.ShortName()}.{property.Name} has scale {scale}, below the required {RequiredScale}.");
            }
        }

        Assert.True(offenders.Count == 0,
            "Coordinate columns without usable precision:\n  " + string.Join("\n  ", offenders));

        // A test that silently examined nothing would be worse than no test — the property names
        // are matched by string, so a rename could quietly empty this out.
        Assert.True(checkedCount >= 8,
            $"Only {checkedCount} coordinate properties were found; the name list is probably stale.");
    }
}
