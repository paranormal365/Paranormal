using System.Text;

namespace Ben.Data.WebApi.Services.Store;

/// <summary>
/// The store's CSV files — the stock export and import now, the order and refund exports later
/// (storefront S1.4). RFC 4180: a field holding a comma, a quote or a line break is quoted, and a
/// quote inside one is doubled.
/// </summary>
public static class StoreCsv
{
    public static string Field(string? value)
    {
        value ??= string.Empty;
        // A leading =, +, - or @ is a formula to a spreadsheet; an apostrophe keeps it text.
        if (value.Length > 0 && "=+-@".Contains(value[0]) && !decimal.TryParse(value, out _)) value = "'" + value;
        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }

    public static string Line(params object?[] fields)
        => string.Join(',', fields.Select(f => Field(f switch
        {
            null => null,
            IFormattable x => x.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
            _ => f.ToString(),
        })));

    /// <summary>UTF-8 with a byte-order mark, so Excel opens a name like "Café" as written.</summary>
    public static byte[] Bytes(IEnumerable<string> lines)
        => Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(string.Join("\r\n", lines) + "\r\n")).ToArray();

    /// <summary>The rows of a CSV file, each a list of fields, quotes understood.</summary>
    public static List<List<string>> Parse(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else field.Append(c);
                continue;
            }
            switch (c)
            {
                case '"': quoted = true; break;
                case ',': row.Add(field.ToString()); field.Clear(); break;
                case '\r': break;
                case '\n': row.Add(field.ToString()); field.Clear(); rows.Add(row); row = []; break;
                case '﻿' when i == 0: break;
                default: field.Append(c); break;
            }
        }
        if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); rows.Add(row); }
        return rows.Where(r => r.Any(f => f.Trim().Length > 0)).ToList();
    }
}
