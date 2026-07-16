# Ben.Data.Common — Helpers

## `AuditChangeTracker`

**Namespace:** `Ben.Data.Common.Helpers`  
**File:** [`Ben.Data.Common/Helpers/AuditChangeTracker.cs`](../../../Ben.Data.Common/Helpers/AuditChangeTracker.cs)  
**Type:** Static class

### Summary

Produces property snapshots and before/after diffs for the audit logging system.  
Used by [`AuditLogService`](../../Ben.Service.RepositoryService/Services.md#auditlogservice) to build the `ChangesJson` column on `AuditLog` rows.

Only **scalar properties** are tracked — primitives, `string`, `Guid`, `DateTime`, `DateTimeOffset`, `decimal`, and enums. Navigation properties and collections are silently excluded.

### Methods

#### `ToPropertySnapshot(object entity)`

| Detail | Value |
|---|---|
| **Returns** | `IReadOnlyDictionary<string, string?>` |
| **Throws** | `ArgumentNullException` when `entity` is null |

Iterates all public readable scalar properties and returns a name→value dictionary.  
Used for **Create** and **Delete** audit entries — captures a complete snapshot of what was created or what existed before deletion.

**Parameters:**

| Parameter | Type | Description |
|---|---|---|
| `entity` | `object` | Any object whose scalar properties should be captured. |

---

#### `GetChanges(object before, object after)`

| Detail | Value |
|---|---|
| **Returns** | `IReadOnlyList<PropertyChange>` |
| **Throws** | `ArgumentNullException` when either argument is null |
| **Throws** | `ArgumentException` when `before` and `after` are different types |

Compares scalar properties of two objects of the same type and returns only the properties whose values differ.  
Used for **Update** audit entries — the resulting list is serialised to JSON and stored in `AuditLog.ChangesJson`.

**Parameters:**

| Parameter | Type | Description |
|---|---|---|
| `before` | `object` | Entity state loaded from the database before the update. |
| `after` | `object` | Entity state after the update was applied. |

**Returns:** List of [`PropertyChange`](#propertychange) records — one per changed property.

**Remarks:**  
- If no properties changed, returns an empty list (the audit row is still written with an empty JSON array).
- `DateTime` values are formatted as `"yyyy-MM-dd HH:mm:ss"` for consistent display.

---

### Nested type: `PropertyChange`

```csharp
public record PropertyChange(string Property, string? Before, string? After);
```

| Field | Type | Description |
|---|---|---|
| `Property` | `string` | Name of the changed property. |
| `Before` | `string?` | String representation of the value before the update, or `null`. |
| `After` | `string?` | String representation of the value after the update, or `null`. |

---

## `FileExtensionPatternMatcher`

**Namespace:** `Ben.Data.Common.Helpers`  
**File:** [`Ben.Data.Common/Helpers/FileExtensionPatternMatcher.cs`](../../../Ben.Data.Common/Helpers/FileExtensionPatternMatcher.cs)  
**Type:** Static class

### Summary

Matches file extensions against patterns stored on `UploadFileTypeExtension` rows.  
Supports two pattern forms:

| Form | Example | Matches |
|---|---|---|
| Exact | `.txt` | Only `.txt` (case-insensitive) |
| Suffix wildcard | `.tx*` | Any extension starting with `.tx` (`.txa`, `.txb`, `.txzzz`) |

The `*` wildcard is only supported as the **final character**; interior wildcards are treated as literals.  
Comparison is always **case-insensitive**.

### Methods

#### `Matches(string pattern, string fileExtension)`

| Detail | Value |
|---|---|
| **Returns** | `bool` |
| **Returns `false`** | When either argument is null/whitespace |

Tests whether a single pattern matches a single file extension.  
Leading dots are normalised automatically — `.txt` and `txt` are both accepted.

**Parameters:**

| Parameter | Type | Description |
|---|---|---|
| `pattern` | `string` | An extension pattern from `UploadFileTypeExtension.Pattern`. |
| `fileExtension` | `string` | The actual file extension to test (from `Path.GetExtension(fileName)`). |

---

#### `IsAllowedByPatterns(IEnumerable<string> patterns, string fileExtension)`

| Detail | Value |
|---|---|
| **Returns** | `bool` |

Returns `true` if **any** pattern in the collection matches the file extension (logical OR across all patterns).

**Runtime usage at upload time:**
```csharp
bool ok = type.AllowAllExtensions
       || FileExtensionPatternMatcher.IsAllowedByPatterns(
              type.AllowedExtensions.Select(e => e.Pattern),
              Path.GetExtension(fileName));
```

**Parameters:**

| Parameter | Type | Description |
|---|---|---|
| `patterns` | `IEnumerable<string>` | Patterns from all `UploadFileTypeExtension` rows for the file type. |
| `fileExtension` | `string` | The actual file extension to validate. |

---

## `PredicateHelper`

**Namespace:** `Ben.Data.Common.Helpers`  
**File:** [`Ben.Data.Common/Helpers/PredicateHelper.cs`](../../../Ben.Data.Common/Helpers/PredicateHelper.cs)  
**Type:** Static class (extension methods)

### Summary

Composable LINQ expression predicate builder for dynamic EF Core query filters.  
Allows incremental construction of `Expression<Func<T, bool>>` predicates that EF Core can translate to SQL.

**Key design note:** The `Or` and `And` combinators re-use a single parameter instance across both branches using an internal reflection-based `Replace` helper. This is necessary because independently created lambda expressions have distinct `ParameterExpression` objects, which would cause EF Core translation to fail.

### Methods

#### `Get<T>()`
Returns `null` — a typed null predicate suitable as a starting point.

#### `Get<T>(Expression<Func<T, bool>> predicate)`  
Identity overload — returns the supplied predicate unchanged.

#### `Or<T>(expr, or)` *(extension on `Expression<Func<T,bool>>`)*
Combines two predicates with logical OR. Returns `or` directly when `expr` is `null`.

#### `And<T>(expr, and)` *(extension on `Expression<Func<T,bool>>`)*
Combines two predicates with logical AND. Returns `and` directly when `expr` is `null`.

**Example:**
```csharp
Expression<Func<AppUser, bool>>? filter = null;
if (!string.IsNullOrEmpty(email))
    filter = filter.Or(u => u.Email == email);
if (!string.IsNullOrEmpty(name))
    filter = filter.Or(u => u.DisplayName == name);
```

---

## `DateTimeHelper` (class name: `DateTimeService`)

**Namespace:** `Ben.Data.Common.Helpers`  
**File:** [`Ben.Data.Common/Helpers/DateTimeHelper.cs`](../../../Ben.Data.Common/Helpers/DateTimeHelper.cs)  
**Type:** Static class (named `DateTimeService`)

### Summary

Static utility methods for formatting `DateTime` values and performing date comparisons. Stateless — no DI required.

### Methods

| Method | Returns | Description |
|---|---|---|
| `ToDateString(DateTime?)` | `string` | Formats as `MM/dd/yyyy`, or empty string if null. |
| `ToDateStringYearFirst(DateTime?)` | `string` | Formats as `yyyy-MM-dd` (ISO 8601 date), or empty string if null. |
| `ToDateStringWithTime(DateTime?)` | `string` | Formats as `MM/dd/yyyy HH:mm:ss`, or empty string if null. |
| `ToDateStringWithTimeYearFirst(DateTime?)` | `string` | Formats as `yyyy-MM-dd HH:mm:ss`, or empty string if null. |
| `DateIsLessThanNow(DateTime?, bool)` | `bool` | `true` if date is before today. `defaultIfNull` applied when value is null (default `false`). |
| `DateIsLessThanOrEqualToToday(DateTime?, bool)` | `bool` | `true` if date is today or earlier. |
| `DateIsLaterThanNow(DateTime?, bool)` | `bool` | `true` if date is after today. `defaultIfNull` defaults to `true`. |
| `DateIsEqual(DateTime?, DateTime?, bool)` | `bool` | Day-level equality. Returns `defaultIfBothNull` (default `true`) when both are null; `false` when exactly one is null. |

---

## `ColorHelper`

**Namespace:** `Ben.Data.Common.Helpers`  
**File:** [`Ben.Data.Common/Helpers/ColorHelper.cs`](../../../Ben.Data.Common/Helpers/ColorHelper.cs)  
**Type:** Class

### Summary

Utility class for converting between hex color strings, `System.Drawing.Color` objects, and RGBA components.  
Also provides a contrast helper (`GetContrastColor`) that determines whether black or white text provides better readability against a given background.

### Constants

| Name | Value | Description |
|---|---|---|
| `LIGHT` | `"#ffffff"` | Default light color (white). |
| `DARK` | `"#4f4f4f"` | Default dark color (dark grey). |
| `BRIGHTNESS_LEVEL` | `130` | Threshold used by contrast detection. |

### Key Properties

| Property | Type | Description |
|---|---|---|
| `HexColor` | `string` | The hex string representation of the current color. |
| `ColorObj` | `Color` | The `System.Drawing.Color` object for the current color. |

### Key Methods

| Method | Description |
|---|---|
| `GetRgba()` | Returns the color as an RGBA CSS string (e.g. `"rgba(255,255,255,1)"`). |
| `GetContrastColor()` | Returns `LIGHT` or `DARK` depending on which provides better contrast. |
| `GetBrightness()` | Computes perceived brightness using the standard luminance formula. |
