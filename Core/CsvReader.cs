using System.Text;

namespace FixletBuilder.Core;

public static class CsvReader
{
    public static List<string[]> Parse(string text)
    {
        var rows = new List<string[]>();
        var row = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;
        int i = 0;

        while (i < text.Length)
        {
            char c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            else
            {
                if (c == '"' && sb.Length == 0)
                {
                    inQuotes = true;
                }
                else if (c == ',')
                {
                    row.Add(sb.ToString());
                    sb.Clear();
                }
                else if (c == '\r' || c == '\n')
                {
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                        i++;
                    row.Add(sb.ToString());
                    sb.Clear();
                    rows.Add(row.ToArray());
                    row.Clear();
                }
                else
                {
                    sb.Append(c);
                }
            }
            i++;
        }

        if (sb.Length > 0 || row.Count > 0)
        {
            row.Add(sb.ToString());
            rows.Add(row.ToArray());
        }

        while (rows.Count > 0 && rows[^1].All(string.IsNullOrWhiteSpace))
            rows.RemoveAt(rows.Count - 1);

        return rows;
    }
}
