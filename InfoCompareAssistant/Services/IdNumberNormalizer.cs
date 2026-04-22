using System.Text;

namespace InfoCompareAssistant.Services;

public static class IdNumberNormalizer
{
    public static string Clean(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return "";

        var sb = new StringBuilder(raw.Length);
        foreach (var ch in raw)
        {
            if (char.IsControl(ch))
                continue;
            if (char.IsWhiteSpace(ch))
                continue;
            sb.Append(ch);
        }

        var s = sb.ToString().Trim().ToUpperInvariant();
        if (s.Length == 15 && IsAllDigits(s.AsSpan(0, 15)))
            s = Convert15To18(s);

        return s;
    }

    public static bool LooksLikeIdNumber(string? s)
    {
        if (string.IsNullOrEmpty(s))
            return false;
        var n = Clean(s);
        return n.Length == 18 && IsAllDigits(n.AsSpan(0, 17)) && (char.IsDigit(n[17]) || n[17] == 'X');
    }

    /// <summary>18 位（含由 15 位规范化得到的 18 位）大陆居民身份证末位校验是否正确。</summary>
    public static bool IsValidChecksum18(string normalized18)
    {
        if (string.IsNullOrEmpty(normalized18) || normalized18.Length != 18)
            return false;
        if (!LooksLikeIdNumber(normalized18))
            return false;
        var expected = char.ToUpperInvariant(ComputeCheckDigit(normalized18.AsSpan(0, 17)));
        return char.ToUpperInvariant(normalized18[17]) == expected;
    }

    private static bool IsAllDigits(ReadOnlySpan<char> span)
    {
        foreach (var c in span)
        {
            if (!char.IsDigit(c))
                return false;
        }
        return true;
    }

    /// <summary>GB11643-1999 校验位（18 位末位）。</summary>
    public static char ComputeCheckDigit(ReadOnlySpan<char> first17)
    {
        ReadOnlySpan<int> w = stackalloc int[] { 7, 9, 10, 5, 8, 4, 2, 1, 6, 3, 7, 9, 10, 5, 8, 4, 2 };
        var sum = 0;
        for (var i = 0; i < 17; i++)
            sum += (first17[i] - '0') * w[i];
        var m = sum % 11;
        return m switch
        {
            0 => '1',
            1 => '0',
            2 => 'X',
            3 => '9',
            4 => '8',
            5 => '7',
            6 => '6',
            7 => '5',
            8 => '4',
            9 => '3',
            10 => '2',
            _ => '0'
        };
    }

    private static string Convert15To18(string id15)
    {
        if (id15.Length != 15 || !IsAllDigits(id15.AsSpan()))
            return id15;

        var yy = int.Parse(id15.AsSpan(6, 2), System.Globalization.NumberStyles.None, null);
        var century = yy <= int.Parse(DateTime.Now.ToString("yy")) ? "20" : "19";
        var core = string.Concat(id15.AsSpan(0, 6), century, id15.AsSpan(6, 9)); // 17 chars
        var check = ComputeCheckDigit(core.AsSpan());
        return core + check;
    }
}
