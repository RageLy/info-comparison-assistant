namespace InfoCompareAssistant.Services;

public static class DeathDateNormalizer
{
    public static string ToKey(object? cell)
    {
        if (cell == null)
            return "";

        if (cell is DateTime dt)
            return dt.Date.ToString("yyyyMMdd");

        var s = cell.ToString()?.Trim() ?? "";
        if (string.IsNullOrEmpty(s))
            return "";

        if (DateTime.TryParse(s, out var parsed))
            return parsed.Date.ToString("yyyyMMdd");

        var digits = new string(s.Where(char.IsDigit).ToArray());
        if (digits.Length is 8 or 6)
            return digits.Length == 6 ? digits : digits;

        return s;
    }

    public static string ToDisplay(object? cell)
    {
        if (cell == null)
            return "";
        if (cell is DateTime dt)
            return dt.ToString("yyyy-MM-dd");
        return cell.ToString()?.Trim() ?? "";
    }
}
