#nullable enable
using System;
using System.Security.Cryptography;
using System.Text;

namespace ArpgGame.Net;

/// <summary>
/// The party's join code — docs/design/multiplayer.md -> "Security". Six
/// characters from an alphabet with no look-alikes (no 0/O, 1/I/L), shown as
/// <c>K7Q-29M</c>. The host draws a fresh one per lobby; a joiner types it next
/// to the address and it travels in the very first message (the engine's
/// authentication step), before the host reads anything else from them.
/// 31^6 ≈ 887 million codes against 5 guesses a minute per address.
/// </summary>
public static class JoinCode
{
    public const int Length = 6;

    public const string Alphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";

    /// <summary>A fixed code for automated tests and benches on one machine (they
    /// also skip UPnP). The game never uses it: a lobby always draws a fresh code.</summary>
    public const string TestCode = "TEST42";

    public static string Generate()
    {
        var chars = new char[Length];
        for (int i = 0; i < Length; i++)
        {
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return new string(chars);
    }

    /// <summary>What the player typed → the bare code: case, spaces and dashes
    /// don't matter. Anything outside the alphabet makes it malformed.</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        var sb = new StringBuilder(Length);
        foreach (char ch in text)
        {
            if (ch is '-' or ' ' or '\t')
            {
                continue;
            }

            sb.Append(char.ToUpperInvariant(ch));
            if (sb.Length > 16)
            {
                break; // a paste of something else: no need to read it all
            }
        }

        return sb.ToString();
    }

    public static bool IsWellFormed(string code)
    {
        if (code.Length != Length)
        {
            return false;
        }

        foreach (char ch in code)
        {
            if (Alphabet.IndexOf(ch) < 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>"K7Q29M" → "K7Q-29M".</summary>
    public static string Format(string code) => code.Length == Length ? $"{code[..3]}-{code[3..]}" : code;

    /// <summary>Constant-time comparison of two normalised codes.</summary>
    public static bool Matches(string expected, string given)
    {
        byte[] a = Encoding.ASCII.GetBytes(expected);
        byte[] b = Encoding.ASCII.GetBytes(given);
        return a.Length == b.Length && a.Length > 0 && CryptographicOperations.FixedTimeEquals(a, b);
    }
}
