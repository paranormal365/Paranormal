namespace Ben.Data.Common.Helpers;

/// <summary>
/// Static utility methods for formatting and comparing <see cref="DateTime"/> values.
/// </summary>
/// <remarks>
/// Despite being named <c>DateTimeService</c> this class is stateless — no DI
/// registration is required.  All members are <c>static</c> and safe for use
/// from any context.
/// </remarks>
public static class DateTimeService
{
    /// <summary>Formats a date as <c>MM/dd/yyyy</c>, or returns <see cref="string.Empty"/> if the value is <c>null</c>.</summary>
    /// <param name="dateTime">The date to format, or <c>null</c>.</param>
    public static string ToDateString(DateTime? dateTime = null)
    {
        return dateTime.HasValue ? dateTime.Value.ToString("MM/dd/yyyy") : string.Empty;
    }

    /// <summary>Formats a date as <c>yyyy-MM-dd</c> (ISO 8601 date), or returns <see cref="string.Empty"/> if the value is <c>null</c>.</summary>
    /// <param name="dateTime">The date to format, or <c>null</c>.</param>
    public static string ToDateStringYearFirst(DateTime? dateTime = null)
    {
        return dateTime.HasValue ? dateTime.Value.ToString("yyyy-MM-dd") : string.Empty;
    }

    /// <summary>Formats a date and time as <c>MM/dd/yyyy HH:mm:ss</c>, or returns <see cref="string.Empty"/> if the value is <c>null</c>.</summary>
    /// <param name="dateTime">The date/time to format, or <c>null</c>.</param>
    public static string ToDateStringWithTime(DateTime? dateTime = null)
    {
        return dateTime.HasValue ? dateTime.Value.ToString("MM/dd/yyyy HH:mm:ss") : string.Empty;
    }

    /// <summary>Formats a date and time as <c>yyyy-MM-dd HH:mm:ss</c>, or returns <see cref="string.Empty"/> if the value is <c>null</c>.</summary>
    /// <param name="dateTime">The date/time to format, or <c>null</c>.</param>
    public static string ToDateStringWithTimeYearFirst(DateTime? dateTime = null)
    {
        return dateTime.HasValue ? dateTime.Value.ToString("yyyy-MM-dd HH:mm:ss") : string.Empty;
    }

    /// <summary>
    /// Returns <c>true</c> if the date component of <paramref name="dateTime"/> is earlier than today.
    /// </summary>
    /// <param name="dateTime">The date to compare, or <c>null</c>.</param>
    /// <param name="defaultIfNull">The value to return when <paramref name="dateTime"/> is <c>null</c>. Defaults to <c>false</c>.</param>
    public static bool DateIsLessThanNow(DateTime? dateTime = null, bool defaultIfNull = false)
    {
        return dateTime.HasValue ? dateTime.Value.Date < DateTime.Now.Date : defaultIfNull;
    }

    /// <summary>
    /// Returns <c>true</c> if the date component of <paramref name="dateTime"/> is today or earlier.
    /// </summary>
    /// <param name="dateTime">The date to compare, or <c>null</c>.</param>
    /// <param name="defaultIfNull">The value to return when <paramref name="dateTime"/> is <c>null</c>. Defaults to <c>false</c>.</param>
    public static bool DateIsLessThanOrEqualToToday(DateTime? dateTime = null, bool defaultIfNull = false)
    {
        return dateTime.HasValue ? dateTime.Value.Date <= DateTime.Now.Date : defaultIfNull;
    }

    /// <summary>
    /// Returns <c>true</c> if the date component of <paramref name="dateTime"/> is later than today.
    /// </summary>
    /// <param name="dateTime">The date to compare, or <c>null</c>.</param>
    /// <param name="defaultIfNull">The value to return when <paramref name="dateTime"/> is <c>null</c>. Defaults to <c>true</c>.</param>
    public static bool DateIsLaterThanNow(DateTime? dateTime = null, bool defaultIfNull = true)
    {
        return (dateTime.HasValue) ? dateTime.Value.Date > DateTime.Now.Date : defaultIfNull;
    }

    /// <summary>
    /// Returns <c>true</c> if the date components of <paramref name="dateOne"/> and <paramref name="dateTwo"/> are the same calendar day.
    /// </summary>
    /// <param name="dateOne">First date, or <c>null</c>.</param>
    /// <param name="dateTwo">Second date, or <c>null</c>.</param>
    /// <param name="defaultIfBothNull">Value returned when <b>both</b> inputs are <c>null</c>. Defaults to <c>true</c>.</param>
    /// <returns>
    /// <c>false</c> if exactly one of the inputs is <c>null</c>;
    /// <paramref name="defaultIfBothNull"/> if both are <c>null</c>;
    /// otherwise a day-level equality comparison.
    /// </returns>
    public static bool DateIsEqual(DateTime? dateOne, DateTime? dateTwo, bool defaultIfBothNull = true)
    {
        if (!dateOne.HasValue && !dateTwo.HasValue)
            return defaultIfBothNull;
        else if (!dateOne.HasValue || !dateTwo.HasValue)
            return false;
        else
            return dateOne.Value.Date == dateTwo.Value.Date;
    }
}

