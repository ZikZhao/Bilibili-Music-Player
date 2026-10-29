using System;

namespace bilibili_music_player_windows.Services;

// Adapted from rocicorp/fractional-indexing 3.2.0 (CC0).
// Binary/ordinal collation is part of the protocol. Never use culture sorting.
public static class FractionalRank
{
    private const string Digits = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
    private static int IntegerLength(char head) => head is >= 'a' and <= 'z' ? head - 'a' + 2 :
        head is >= 'A' and <= 'Z' ? 'Z' - head + 2 : throw new ArgumentException("Invalid rank head.");
    private static string Integer(string key)
    { var length = IntegerLength(key[0]); return key.Length >= length ? key[..length] : throw new ArgumentException("Truncated rank."); }
    public static void Validate(string key)
    {
        if (string.IsNullOrEmpty(key) || key.Length > 2048 || key == "A" + new string('0', 26)) throw new ArgumentException("Invalid rank.");
        var integer = Integer(key);
        foreach (var c in key.AsSpan(1)) if (!Digits.Contains(c)) throw new ArgumentException("Invalid rank digit.");
        if (key.Length > integer.Length && key[^1] == '0') throw new ArgumentException("Rank fraction ends in zero.");
    }
    private static string? Step(string value, bool up)
    {
        var head = value[0]; var digits = value[1..].ToCharArray(); var carry = true;
        for (var i = digits.Length - 1; i >= 0 && carry; i--)
        {
            var n = Digits.IndexOf(digits[i]) + (up ? 1 : -1);
            if (n == 62 || n == -1) digits[i] = up ? '0' : 'z';
            else { digits[i] = Digits[n]; carry = false; }
        }
        if (!carry) return head + new string(digits);
        if (up && head == 'Z') return "a0";
        if (!up && head == 'a') return "Zz";
        if ((up && head == 'z') || (!up && head == 'A')) return null;
        var next = (char)(head + (up ? 1 : -1));
        var tail = new string(digits);
        return next + (IntegerLength(next) > value.Length ? tail + (up ? "0" : "z") : tail[..^1]);
    }
    private static string Midpoint(string a, string? b)
    {
        if (b is not null)
        {
            var n = 0;
            while (n < b.Length && (n < a.Length ? a[n] : '0') == b[n]) n++;
            if (n > 0) return b[..n] + Midpoint(n < a.Length ? a[n..] : "", b[n..]);
        }
        var lower = a.Length > 0 ? Digits.IndexOf(a[0]) : 0;
        var upper = b is not null ? Digits.IndexOf(b[0]) : 62;
        if (upper - lower > 1) return Digits[(lower + upper + 1) / 2].ToString();
        if (b is { Length: > 1 }) return b[..1];
        return Digits[lower] + Midpoint(a.Length > 0 ? a[1..] : "", null);
    }
    public static string Between(string? a, string? b)
    {
        if (a is not null) Validate(a);
        if (b is not null) Validate(b);
        if (a is not null && b is not null && string.CompareOrdinal(a, b) >= 0) throw new ArgumentException("Unordered rank bounds.");
        if (a is null)
        {
            if (b is null) return "a0";
            var ib = Integer(b); var fb = b[ib.Length..];
            if (ib == "A" + new string('0', 26)) return ib + Midpoint("", fb);
            if (ib.Length < b.Length) return ib;
            return Step(ib, false) ?? throw new ArgumentException("Rank underflow.");
        }
        var ia = Integer(a); var fa = a[ia.Length..];
        if (b is null) return Step(ia, true) ?? ia + Midpoint(fa, null);
        var integerB = Integer(b);
        if (ia == integerB) return ia + Midpoint(fa, b[integerB.Length..]);
        var next = Step(ia, true) ?? throw new ArgumentException("Rank overflow.");
        return string.CompareOrdinal(next, b) < 0 ? next : ia + Midpoint(fa, null);
    }
}
