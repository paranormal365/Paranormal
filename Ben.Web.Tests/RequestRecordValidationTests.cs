using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Ben.Service.Models.Admin;
using Xunit;

namespace Ben.Web.Tests;

/// <summary>
/// No request record carries a validation attribute on a property that is also a primary
/// constructor parameter.
/// </summary>
/// <remarks>
/// <para>ASP.NET refuses that shape at model binding with an InvalidOperationException ("has
/// validation metadata defined on property … that will be ignored"), so the endpoint answers 500
/// to every call. <c>AdminSetUserRolesRequest</c> was written <c>[property: Required]</c> on
/// 09/04/2026 and every Save on a person's site roles failed from then until the full browser
/// run's API log showed it on 10/14/2026. Its controller tests call the action directly, past
/// binding, so they passed throughout. The attribute belongs on the parameter.</para>
/// </remarks>
public class RequestRecordValidationTests
{
    [Fact]
    public void No_record_puts_validation_on_a_property_its_constructor_sets()
    {
        var offenders =
            from type in typeof(AdminSetUserRolesRequest).Assembly.GetTypes()
            // The compiler gives every record a <Clone>$ method; nothing else has one.
            where type.GetMethod("<Clone>$") is not null
            let parameters = type.GetConstructors()
                .SelectMany(c => c.GetParameters())
                .Select(p => p.Name)
                .ToHashSet(StringComparer.Ordinal)
            from property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            where parameters.Contains(property.Name)
               && property.GetCustomAttributes<ValidationAttribute>(inherit: true).Any()
            select $"{type.FullName}.{property.Name}";

        Assert.Empty(offenders);
    }
}
