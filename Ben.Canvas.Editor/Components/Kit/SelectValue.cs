using System.Collections.Concurrent;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;

namespace Ben.Canvas.Editor.Components.Kit;

// Copied from Ben.Web.Website.Library/Kit/SelectValue.cs, because Kit cannot be referenced from
// WebAssembly. Behaviour identical.

/// <summary>
/// Turns option values into strings for an <c>&lt;option value="…"&gt;</c> and back again, and reads a
/// named property off an arbitrary item.
/// </summary>
/// <remarks>
/// A value that round-trips incorrectly means a native select silently selects nothing, or writes
/// back a default. <c>Guid</c> is the case that matters most.
/// </remarks>
public static class SelectValue
{
    private static readonly ConcurrentDictionary<(Type, string), PropertyInfo?> PropertyCache = new();

    /// <summary>The string that goes in the option's value attribute. Null becomes empty.</summary>
    public static string ToOptionString<TValue>(TValue? value) => value switch
    {
        null           => string.Empty,
        string s       => s,
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _              => value.ToString() ?? string.Empty,
    };

    /// <summary>
    /// Reverses <see cref="ToOptionString"/>. An empty string yields <c>default</c>, which is how the
    /// "nothing selected" option reports itself.
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
        }

        return false;
    }

    /// <summary>
    /// Reads <paramref name="propertyName"/> off <paramref name="item"/>, or returns the item itself
    /// when no name is given.
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

        return property is null ? item : property.GetValue(item);
    }
}
