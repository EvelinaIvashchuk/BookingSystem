using System.Globalization;
using System.Text;

namespace BookingSystem.Evaluation;

/// <summary>Minimal RFC 4180 CSV writer/reader (UTF-8, comma, quoted when needed).</summary>
public static class Csv
{
    private static readonly SemaphoreSlim AppendLock = new(1, 1);

    public static string Escape(object? value)
    {
        var text = value switch
        {
            null => string.Empty,
            bool b => b ? "1" : "0",
            double d => d.ToString("0.######", CultureInfo.InvariantCulture),
            float f => f.ToString("0.######", CultureInfo.InvariantCulture),
            decimal m => m.ToString("0.######", CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };
        return text.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{text.Replace("\"", "\"\"")}\"" : text;
    }

    public static string Line(IEnumerable<object?> values) => string.Join(',', values.Select(Escape));

    /// <summary>Appends a row, writing the header first if the file does not exist (thread-safe).</summary>
    public static async Task AppendAsync(string path, IReadOnlyList<string> header, IEnumerable<object?> values)
    {
        await AppendLock.WaitAsync();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var exists = File.Exists(path);
            await using var writer = new StreamWriter(path, append: true, new UTF8Encoding(false));
            if (!exists) await writer.WriteLineAsync(Line(header));
            await writer.WriteLineAsync(Line(values));
        }
        finally
        {
            AppendLock.Release();
        }
    }

    public static void Write(string path, IReadOnlyList<string> header, IEnumerable<IEnumerable<object?>> rows)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var sb = new StringBuilder().AppendLine(Line(header));
        foreach (var row in rows) sb.AppendLine(Line(row));
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    /// <summary>Reads a CSV file into dictionaries keyed by header names.</summary>
    public static List<Dictionary<string, string>> Read(string path)
    {
        if (!File.Exists(path)) return [];
        var records = Parse(File.ReadAllText(path));
        if (records.Count == 0) return [];

        var header = records[0];
        return records.Skip(1)
            .Where(r => r.Count == header.Count)
            .Select(r => header.Zip(r).ToDictionary(p => p.First, p => p.Second))
            .ToList();
    }

    private static List<List<string>> Parse(string text)
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
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { row.Add(field.ToString()); field.Clear(); }
            else if (c == '\n')
            {
                row.Add(field.ToString()); field.Clear();
                rows.Add(row); row = [];
            }
            else if (c != '\r') field.Append(c);
        }

        if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); rows.Add(row); }
        return rows;
    }

    public static double D(this Dictionary<string, string> row, string key) =>
        double.TryParse(row.GetValueOrDefault(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;

    public static bool B(this Dictionary<string, string> row, string key) => row.GetValueOrDefault(key) == "1";
}
