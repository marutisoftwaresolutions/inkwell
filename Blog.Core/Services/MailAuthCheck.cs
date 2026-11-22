namespace Blog.Core.Services;

/// <summary>
/// Reads a sender domain's published mail records and says, in plain words, whether mailbox providers
/// will accept newsletters sent in its name. Since 2024 the large providers require SPF or DKIM, and
/// a DMARC record, from anyone sending in bulk; without them a newsletter is filtered or refused however
/// good the list is. This only reports what the DNS says: it cannot see whether the mail server
/// actually signs, and it never changes anything. Pure.
/// </summary>
public static class MailAuthCheck
{
    public enum Level { Ok, Warning, Problem }

    public sealed record Finding(string Record, Level Level, string Message);

    /// <summary>The domain part of a sender address, lower case, or null when there is none.</summary>
    public static string? DomainOf(string? fromEmail)
    {
        var at = (fromEmail ?? "").LastIndexOf('@');
        if (at < 1 || at == fromEmail!.Length - 1) return null;
        var domain = fromEmail[(at + 1)..].Trim().TrimEnd('.').ToLowerInvariant();
        return domain.Contains('.') && domain.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-') ? domain : null;
    }

    /// <summary>A DKIM selector is one DNS label; anything else is ignored.</summary>
    public static string? CleanSelector(string? selector)
    {
        var s = (selector ?? "").Trim().ToLowerInvariant();
        return s.Length is > 0 and <= 63 && s.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') ? s : null;
    }

    public static Finding Spf(IReadOnlyList<string>? domainTxt)
    {
        if (domainTxt is null) return new("SPF", Level.Warning, "The domain's records could not be read just now. Try again later.");
        var spf = domainTxt.Where(t => t.StartsWith("v=spf1", StringComparison.OrdinalIgnoreCase)).ToList();
        if (spf.Count == 0)
            return new("SPF", Level.Problem, "No SPF record. Add a TXT record at the domain that starts with v=spf1 and names your mail service; your mail provider's help pages give the exact text.");
        if (spf.Count > 1)
            return new("SPF", Level.Problem, "More than one SPF record. Mail providers treat that as no SPF at all. Merge them into one TXT record.");
        var record = spf[0];
        if (record.Contains("+all", StringComparison.OrdinalIgnoreCase))
            return new("SPF", Level.Problem, "The SPF record ends in +all, which lets anyone send as this domain. Change it to ~all or -all.");
        // "redirect=" hands the whole decision to another record, so there is rightly no "all" here.
        if (record.Contains("redirect=", StringComparison.OrdinalIgnoreCase))
            return new("SPF", Level.Ok, "Found: " + Shorten(record));
        if (!record.Contains("all", StringComparison.OrdinalIgnoreCase))
            return new("SPF", Level.Warning, "The SPF record has no \"all\" rule at the end, so it does not say what to do with other senders. End it with ~all or -all.");
        return new("SPF", Level.Ok, "Found: " + Shorten(record));
    }

    public static Finding Dmarc(IReadOnlyList<string>? dmarcTxt)
    {
        if (dmarcTxt is null) return new("DMARC", Level.Warning, "The DMARC record could not be read just now. Try again later.");
        var record = dmarcTxt.FirstOrDefault(t => t.StartsWith("v=DMARC1", StringComparison.OrdinalIgnoreCase));
        if (record is null)
            return new("DMARC", Level.Problem, "No DMARC record. Add a TXT record at _dmarc.<your domain>, for example \"v=DMARC1; p=none; rua=mailto:you@your-domain\". Gmail and Yahoo require one from bulk senders.");
        var policy = record.Split(';').Select(p => p.Trim()).FirstOrDefault(p => p.StartsWith("p=", StringComparison.OrdinalIgnoreCase))?[2..].Trim().ToLowerInvariant();
        return policy switch
        {
            "reject" or "quarantine" => new("DMARC", Level.Ok, $"Found, policy \"{policy}\": mail that fails the checks is turned away."),
            "none" => new("DMARC", Level.Ok, "Found, policy \"none\": this meets the providers' requirement and only reports. Move to quarantine once your reports look clean."),
            _ => new("DMARC", Level.Warning, "A DMARC record exists but has no valid policy (p=none, quarantine or reject)."),
        };
    }

    public static Finding Dkim(string? selector, IReadOnlyList<string>? selectorTxt)
    {
        if (selector is null)
            return new("DKIM", Level.Warning, "Not checked: DKIM lives under a selector name that your mail service chooses. Set Smtp:DkimSelector to that name to check it here.");
        if (selectorTxt is null) return new("DKIM", Level.Warning, "The DKIM record could not be read just now. Try again later.");
        var record = selectorTxt.FirstOrDefault(t => t.Contains("p=", StringComparison.OrdinalIgnoreCase) && (t.Contains("v=DKIM1", StringComparison.OrdinalIgnoreCase) || t.Contains("k=", StringComparison.OrdinalIgnoreCase)));
        if (record is null)
            return new("DKIM", Level.Problem, $"No DKIM key at {selector}._domainkey. Publish the record your mail service gives you, or correct the selector name.");
        var key = record.Split(';').Select(p => p.Trim()).FirstOrDefault(p => p.StartsWith("p=", StringComparison.OrdinalIgnoreCase))?[2..].Trim();
        return string.IsNullOrEmpty(key)
            ? new("DKIM", Level.Problem, "The DKIM record is published with an empty key, which means the key was revoked.")
            : new("DKIM", Level.Ok, $"A key is published at {selector}._domainkey.");
    }

    /// <summary>The one-line verdict shown above the three findings.</summary>
    public static (Level Level, string Message) Verdict(IReadOnlyList<Finding> findings)
    {
        var spf = findings.FirstOrDefault(f => f.Record == "SPF");
        var dkim = findings.FirstOrDefault(f => f.Record == "DKIM");
        var dmarc = findings.FirstOrDefault(f => f.Record == "DMARC");
        var authenticated = spf?.Level == Level.Ok || dkim?.Level == Level.Ok;
        if (!authenticated || dmarc?.Level == Level.Problem)
            return (Level.Problem, "Mailbox providers are likely to filter or refuse newsletters from this domain until the problems below are fixed.");
        if (findings.Any(f => f.Level != Level.Ok))
            return (Level.Warning, "The essentials are in place. The notes below would make delivery more dependable.");
        return (Level.Ok, "This domain publishes everything the large mailbox providers ask of a bulk sender.");
    }

    private static string Shorten(string s) => s.Length <= 120 ? s : s[..117] + "...";
}
