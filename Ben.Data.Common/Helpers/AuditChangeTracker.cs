using System.Reflection;

namespace Ben.Data.Common.Helpers;

/// <summary>
/// Compares scalar properties between two object instances and produces a list of changed
/// property names with their before/after string representations.
/// Only value-type properties (primitives, string, Guid, DateTime, decimal, enum) are tracked;
/// navigation properties and collections are ignored.
/// </summary>
public static class AuditChangeTracker
{
    // ── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a snapshot dictionary of all scalar property names → string values for an entity.
    /// Used for Create and Delete audit entries.
    /// </summary>
    public static IReadOnlyDictionary<string, string?> ToPropertySnapshot(object entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        return GetScalarProperties(entity.GetType())
            .ToDictionary(p => p.Name, p => Format(p.GetValue(entity)));
    }

    /// <summary>
    /// Compares scalar properties of two objects of the same type and returns
    /// only the properties whose values differ.
    /// </summary>
    public static IReadOnlyList<PropertyChange> GetChanges(object before, object after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        if (before.GetType() != after.GetType())
            throw new ArgumentException(
                $"before ({before.GetType().Name}) and after ({after.GetType().Name}) must be the same type.");

        var changes = new List<PropertyChange>();

        foreach (var prop in GetScalarProperties(before.GetType()))
        {
            var beforeVal = prop.GetValue(before);
            var afterVal  = prop.GetValue(after);

            if (!Equals(beforeVal, afterVal))
                changes.Add(new PropertyChange(prop.Name, Format(beforeVal), Format(afterVal)));
        }

        return changes;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static IEnumerable<PropertyInfo> GetScalarProperties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && IsScalar(p.PropertyType));

    internal static bool IsScalar(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        return underlying.IsPrimitive
            || underlying == typeof(string)
            || underlying == typeof(Guid)
            || underlying == typeof(DateTime)
            || underlying == typeof(DateTimeOffset)
            || underlying == typeof(decimal)
            || underlying.IsEnum;
    }

    private static string? Format(object? value) => value switch
    {
        null                     => null,
        DateTime dt              => dt.ToString("yyyy-MM-dd HH:mm:ss"),
        DateTimeOffset dto       => dto.ToString("yyyy-MM-dd HH:mm:ss zzz"),
        _                        => value.ToString()
    };
}

/// <summary>Represents a single property change captured during an update audit.</summary>
public record PropertyChange(string Property, string? Before, string? After);
