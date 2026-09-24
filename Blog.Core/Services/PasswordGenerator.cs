using System.Security.Cryptography;

namespace Blog.Core.Services;

/// <summary>
/// Generates the initial password for an invited account. Pure and dependency-free so it can be
/// unit tested; the only source of randomness is <see cref="RandomNumberGenerator"/>.
///
/// The alphabet leaves out the characters people misread when a password is copied by eye or read
/// aloud (0 and O, 1 and l and I), and every password carries at least one lower-case letter, one
/// upper-case letter and one digit so it satisfies the usual minimum-complexity rules. The Desk's
/// "Generate" button in Users mirrors this alphabet in the browser; keep the two in step.
/// </summary>
public static class PasswordGenerator
{
    /// <summary>Lower-case letters without the ambiguous <c>l</c>.</summary>
    public const string Lower = "abcdefghijkmnopqrstuvwxyz";

    /// <summary>Upper-case letters without the ambiguous <c>I</c> and <c>O</c>.</summary>
    public const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";

    /// <summary>Digits without the ambiguous <c>0</c> and <c>1</c>.</summary>
    public const string Digits = "23456789";

    /// <summary>Every character a generated password can contain.</summary>
    public const string Alphabet = Lower + Upper + Digits;

    /// <summary>Matches the minimum the invite action accepts for a typed password.</summary>
    public const int MinimumLength = 8;

    public const int DefaultLength = 16;

    /// <summary>
    /// Returns a password of exactly <paramref name="length"/> characters drawn from
    /// <see cref="Alphabet"/>, guaranteed to contain at least one character from each of
    /// <see cref="Lower"/>, <see cref="Upper"/> and <see cref="Digits"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="length"/> is below <see cref="MinimumLength"/>.
    /// </exception>
    public static string Generate(int length = DefaultLength)
    {
        if (length < MinimumLength)
            throw new ArgumentOutOfRangeException(nameof(length), length,
                $"A generated password must be at least {MinimumLength} characters.");

        var chars = new char[length];

        // One of each class first, so the guarantee holds whatever the rest draws.
        chars[0] = Pick(Lower);
        chars[1] = Pick(Upper);
        chars[2] = Pick(Digits);
        for (var i = 3; i < length; i++)
            chars[i] = Pick(Alphabet);

        // Shuffle so the guaranteed characters do not always sit at the front.
        RandomNumberGenerator.Shuffle(chars.AsSpan());
        return new string(chars);
    }

    private static char Pick(string set) => set[RandomNumberGenerator.GetInt32(set.Length)];
}
