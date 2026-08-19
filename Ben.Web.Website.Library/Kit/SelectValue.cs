using System.Collections.Concurrent;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;

namespace Ben.Web.Website.Library.Kit;

/// <summary>
/// Turns option values into strings for an <c>&lt;option value="…"&gt;</c> and back again, and reads
/// a named property off an arbitrary item.
/// </summary>
/// <remarks>
/// <para>
/// This exists as plain C# rather than living inside <c>BenSelect</c> so it can be tested directly.
/// It is the only part of a native select that can quietly go wrong: everything else is markup,
/// but a value that round-trips incorrectly means the control silently selects nothing, or writes
/// back a default. <c>Guid</c> is the case that matters here — most of the site's dropdowns are
/// keyed by one.
/// </para>
/// <para>
/// Property lookup is by name because that is the contract the call sites already use
/// (<c>TextField="@nameof(Foo.Name)"</c>), inherited from the Telerik control these replaced.
/// </para>
/// </remarks>
public static class SelectValue
{
    private static readonly ConcurrentDictionary<(Type, string), PropertyInfo?> PropertyCache = new();

    /// <summary>The string that goes in the option's value attribute. Null becomes empty.</summary>
    public static string ToOptionString<TValue>(TValue? value) => value switch
    {
        null            => string.Empty,
        string s        => s,
        IFormattable f  => f.ToString(null, CultureInfo.InvariantCulture),
        _               => value.ToString() ?? string.Empty,
    };

    /// <summary>
    /// Reverses <see cref="ToOptionString"/>. An empty string yields <c>default</c>, which is how
    /// the "nothing selected" option reports itself.
    /// </summary>
    public static bool TryParse<TValue>(string? text, out TValue? value)
    {
        value = default;
        if (string.IsNullOrEmpty(text)) return true;

        var target = Nullable.GetUnderlyingType(typeof(TValue)) ?? typeof(TValue);

        if (target == typeof(string))
        {
            value = (TValue)(object)text;
            return true;
        }

        try
        {
            // TypeConverter covers Guid, the numeric types and enums, and is what a bound
            // <select> would use anyway.
            var converter = TypeDescriptor.GetConverter(target);
            if (converter.CanConvertFrom(typeof(string)))
            {
                var converted = converter.ConvertFromInvariantString(text);
                if (converted is not null)
                {
                    value = (TValue)converted;
                    return true;
                }
            }
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or NotSupportedException)
        {
            // An option value that does not fit the bound type is a caller bug, not a user error.
            // Report it as "no value" rather than throwing inside a render.
        }

        return false;
    }

    /// <summary>
    /// Reads <paramref name="propertyName"/> off <paramref name="item"/>. Returns the item itself
    /// when no name is given, which is how a list of plain strings or enums binds with no
    /// TextField/ValueField at all.
    /// </summary>
    public static object? GetMember(object? item, string? propertyName)
    {
        if (item is null) return null;
        if (string.IsNullOrEmpty(propertyName)) return item;

        var property = PropertyCache.GetOrAdd(
            (item.GetType(), propertyName),
            key => key.Item1.GetProperty(
                key.Item2,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase));

        // A misspelled field name falls back to the item rather than throwing mid-render; the
        // option then shows the type name, which is visible and traceable in a way an exception
        // inside a render tree is not.
        return property is null ? item : property.GetValue(item);
    }
}
