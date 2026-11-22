using System.Text.RegularExpressions;

namespace Blog.Core.Services;

/// <summary>
/// The floating contact button on the reader surface: one fixed link that opens WhatsApp, a phone
/// call or an email. It is an ordinary link; nothing is loaded from any service. This type validates
/// what the operator typed and builds the link, so an address that is not a real number or email is
/// never written into a page. Pure.
/// </summary>
public static partial class ContactButton
{
    public const string None = "";
    public const string WhatsApp = "whatsapp";
    public const string Phone = "phone";
    public const string Email = "email";

    public const int MaxLabelLength = 40;

    public static readonly IReadOnlyList<string> Kinds = new[] { WhatsApp, Phone, Email };

    /// <summary>What the layout renders.</summary>
    public sealed record Link(string Kind, string Href, string Label, string Icon, bool OpensNewTab);

    [GeneratedRegex(@"^[^@\s<>""',;]+@[^@\s<>""',;]+\.[^@\s<>""',;.]+$", RegexOptions.CultureInvariant, 200)]
    private static partial Regex EmailShape();

    /// <summary>One of the known kinds, or empty (no button) for anything else.</summary>
    public static string NormalizeKind(string? kind)
    {
        var k = (kind ?? "").Trim().ToLowerInvariant();
        return Kinds.Contains(k) ? k : None;
    }

    /// <summary>The digits of a phone number in international form, or null when it is not one (7 to 15 digits, E.164).</summary>
    public static string? Digits(string? value)
    {
        var raw = (value ?? "").Trim();
        if (raw.Length == 0 || raw.Length > 40) return null;
        if (raw.Any(c => !(char.IsAsciiDigit(c) || c is '+' or ' ' or '-' or '(' or ')' or '.'))) return null;
        if (raw.IndexOf('+') > 0 || raw.Count(c => c == '+') > 1) return null;
        var digits = new string(raw.Where(char.IsAsciiDigit).ToArray());
        return digits.Length is >= 7 and <= 15 ? digits : null;
    }

    /// <summary>What is wrong with the value for this kind, in words for the Settings screen, or null when it is fine.</summary>
    public static string? Validate(string? kind, string? value)
    {
        switch (NormalizeKind(kind))
        {
            case None:
                return null;
            case WhatsApp:
                return Digits(value) is null
                    ? "Enter the WhatsApp number in international form with the country code, for example +44 20 7946 0958."
                    : null;
            case Phone:
                return Digits(value) is null
                    ? "Enter the phone number in international form with the country code, for example +1 415 555 0132."
                    : null;
            default:
                var address = (value ?? "").Trim();
                return address.Length is > 0 and <= 254 && EmailShape().IsMatch(address)
                    ? null
                    : "Enter one email address, for example hello@example.com.";
        }
    }

    /// <summary>The label as stored: one line, capped; empty means "use the default for this kind".</summary>
    public static string CleanLabel(string? label)
    {
        var text = new string((label ?? "").Select(c => char.IsControl(c) ? ' ' : c).ToArray()).Trim();
        return text.Length > MaxLabelLength ? text[..MaxLabelLength].TrimEnd() : text;
    }

    public static string DefaultLabel(string kind) => kind switch
    {
        WhatsApp => "Chat on WhatsApp",
        Phone => "Call us",
        Email => "Email us",
        _ => "",
    };

    /// <summary>The link to render, or null when the button is off or the value would not make a working link.</summary>
    public static Link? Build(string? kind, string? value, string? label)
    {
        var k = NormalizeKind(kind);
        if (k == None || Validate(k, value) is not null) return null;
        var text = CleanLabel(label);
        if (text.Length == 0) text = DefaultLabel(k);
        return k switch
        {
            WhatsApp => new Link(k, "https://wa.me/" + Digits(value), text, "bi-whatsapp", true),
            Phone => new Link(k, "tel:+" + Digits(value), text, "bi-telephone", false),
            _ => new Link(k, "mailto:" + Uri.EscapeDataString(value!.Trim()).Replace("%40", "@"), text, "bi-envelope", false),
        };
    }
}
