using System.Text;

namespace Blog.Core.Domain
{
    /// <summary>
    /// Something the site owner wants readers to do, counted per day (Admin → Analytics → Goals):
    /// reaching a page, or following a link. Kept in the settings blob; the counts live in GoalCounts.
    /// </summary>
    public class Goal
    {
        /// <summary>Short lower-case id used in the counts table and by the page script.</summary>
        public string Key { get; set; } = "";
        public string Name { get; set; } = "";
        /// <summary><see cref="Blog.Core.Services.Goals.Visit"/> or <see cref="Blog.Core.Services.Goals.Click"/>.</summary>
        public string Kind { get; set; } = Blog.Core.Services.Goals.Visit;
        /// <summary>For a visit: the page's path ("/pricing"). For a click: the start of the link's address.</summary>
        public string Target { get; set; } = "";
    }

    /// <summary>One day's count for one goal, as written to and read from GoalCounts.</summary>
    public sealed record GoalHit(Guid OwnerId, DateTime Day, string GoalKey, int Hits);
}

namespace Blog.Core.Services
{
    using Blog.Core.Domain;

    /// <summary>
    /// The rules for goals: what a valid goal looks like, which goals a page view or a clicked link
    /// counts towards, and the two conversions every site gets without defining anything. A goal is a
    /// daily counter and nothing else: no visitor, no session, no address is kept with it. The list is
    /// capped and every count is checked against it, so the counts table cannot be filled with
    /// invented names. Pure.
    /// </summary>
    public static class Goals
    {
        public const string Visit = "visit";
        public const string Click = "click";

        public const int Max = 20;
        public const int MaxNameLength = 80;
        public const int MaxKeyLength = 40;
        public const int MaxTargetLength = 300;

        public const string NewsletterSignup = "newsletter-signup";
        public const string FormSubmitted = "form-submitted";

        /// <summary>Counted for every site, with nothing to set up.</summary>
        public static readonly IReadOnlyList<(string Key, string Name, string Meaning)> BuiltIn =
        [
            (NewsletterSignup, "Newsletter sign-ups", "A reader confirmed their subscription."),
            (FormSubmitted, "Form submissions", "A reader sent one of the site's forms."),
        ];

        public static bool IsBuiltIn(string? key) => BuiltIn.Any(b => b.Key == key);

        /// <summary>A key made from a name: lower-case letters, digits and hyphens, unique among <paramref name="taken"/>.</summary>
        public static string KeyFor(string? name, IEnumerable<string> taken)
        {
            var sb = new StringBuilder();
            foreach (var ch in (name ?? "").Trim().ToLowerInvariant())
            {
                if (ch is >= 'a' and <= 'z' or >= '0' and <= '9') sb.Append(ch);
                else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
            }
            var stem = sb.ToString().Trim('-');
            if (stem.Length == 0) stem = "goal";
            if (stem.Length > MaxKeyLength - 3) stem = stem[..(MaxKeyLength - 3)].TrimEnd('-');

            var used = new HashSet<string>(taken, StringComparer.Ordinal);
            foreach (var b in BuiltIn) used.Add(b.Key);
            var key = stem;
            for (var n = 2; used.Contains(key); n++) key = $"{stem}-{n}";
            return key;
        }

        /// <summary>
        /// The target as stored, or null when it is not usable. A visit target is the path of a page
        /// the site counts views for: the home page or a single-segment address ("/pricing"), without
        /// a query. A click target is the start of a link: an http(s) address, a mailto: or tel: link,
        /// or a path on this site.
        /// </summary>
        public static string? CleanTarget(string? kind, string? target)
        {
            var t = (target ?? "").Trim();
            if (t.Length == 0 || t.Length > MaxTargetLength || t.Any(char.IsControl) || t.Any(char.IsWhiteSpace)) return null;

            if (kind == Visit)
            {
                if (Uri.TryCreate(t, UriKind.Absolute, out var abs) && abs.Scheme is "http" or "https") t = abs.AbsolutePath;
                var cut = t.IndexOfAny(['?', '#']);
                if (cut >= 0) t = t[..cut];
                if (!t.StartsWith('/')) t = "/" + t;
                if (t.Length > 1) t = t.TrimEnd('/');
                t = t.ToLowerInvariant();
                return PageViewPolicy.IsTrackablePath(t) ? t : null;
            }
            if (kind == Click)
            {
                if (t.StartsWith('/') && !t.StartsWith("//", StringComparison.Ordinal)) return t;
                if (t.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) || t.StartsWith("tel:", StringComparison.OrdinalIgnoreCase)) return t;
                return Uri.TryCreate(t, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? t : null;
            }
            return null;
        }

        /// <summary>Why a goal a person typed cannot be saved, in words for the form; null when it can.</summary>
        public static string? Problem(string? name, string? kind, string? target, int existing)
        {
            if (existing >= Max) return $"A site can have {Max} goals. Remove one you no longer watch, then add this one.";
            var clean = new string((name ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
            if (clean.Length == 0) return "Give the goal a name, such as \"Reached the pricing page\".";
            if (clean.Length > MaxNameLength) return $"Keep the name under {MaxNameLength} characters.";
            if (kind != Visit && kind != Click) return "Choose what the goal counts: a page visit or a link click.";
            if (CleanTarget(kind, target) is null)
                return kind == Visit
                    ? "Enter the page's address on this site, such as /pricing. Goals can watch the home page, a post or a page; not a category, tag or search address."
                    : "Enter the start of the link to watch: a full address beginning with https://, a mailto: or tel: link, or a path on this site beginning with /.";
            return null;
        }

        /// <summary>What is stored: valid goals only, unique keys, capped. Bad entries are dropped, never repaired into something else.</summary>
        public static List<Goal> Normalize(IEnumerable<Goal>? goals)
        {
            var result = new List<Goal>();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var g in goals ?? [])
            {
                if (result.Count >= Max || g is null) break;
                var target = CleanTarget(g.Kind, g.Target);
                var name = new string((g.Name ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
                var key = (g.Key ?? "").Trim();
                if (target is null || name.Length == 0 || !IsKey(key) || IsBuiltIn(key) || !keys.Add(key)) continue;
                result.Add(new Goal { Key = key, Name = name.Length > MaxNameLength ? name[..MaxNameLength] : name, Kind = g.Kind, Target = target });
            }
            return result;
        }

        public static bool IsKey(string? key) =>
            !string.IsNullOrEmpty(key) && key.Length <= MaxKeyLength && key.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');

        /// <summary>The visit goals a viewed page counts towards.</summary>
        public static IEnumerable<string> VisitKeysFor(IEnumerable<Goal> goals, string? path)
        {
            var p = (path ?? "").Trim().ToLowerInvariant();
            if (p.Length > 1) p = p.TrimEnd('/');
            if (p.Length == 0) return [];
            return goals.Where(g => g.Kind == Visit && g.Target == p).Select(g => g.Key);
        }

        /// <summary>True when the key names one of the site's click goals: the only keys the page script may report.</summary>
        public static bool IsClickGoal(IEnumerable<Goal> goals, string? key) =>
            IsKey(key) && goals.Any(g => g.Kind == Click && g.Key == key);

        /// <summary>The longest range the Goals screen and its download cover, in days.</summary>
        public const int MaxRangeDays = 366;

        /// <summary>
        /// The days a person asked for (both ends included), from two "yyyy-MM-dd" values. Null when
        /// either is missing or unreadable. Reversed dates are swapped, a future end is pulled back to
        /// today, and a range longer than <see cref="MaxRangeDays"/> keeps its end and shortens its start.
        /// </summary>
        public static (DateTime From, DateTime To)? Range(string? from, string? to, DateTime todayUtc)
        {
            const string format = "yyyy-MM-dd";
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            if (!DateTime.TryParseExact((from ?? "").Trim(), format, culture, System.Globalization.DateTimeStyles.None, out var f)) return null;
            if (!DateTime.TryParseExact((to ?? "").Trim(), format, culture, System.Globalization.DateTimeStyles.None, out var t)) return null;
            if (f > t) (f, t) = (t, f);
            var today = todayUtc.Date;
            if (t > today) t = today;
            if (f > t) f = t;
            if ((t - f).TotalDays > MaxRangeDays - 1) f = t.AddDays(-(MaxRangeDays - 1));
            return (f, t);
        }

        /// <summary>The download: one line per day and goal that was reached, oldest first, with the goal's name as the Desk shows it.</summary>
        public static string Csv(IEnumerable<GoalHit> daily, IReadOnlyDictionary<string, string> namesByKey)
        {
            var sb = new StringBuilder("Date,Goal,Count\r\n");
            foreach (var hit in daily.OrderBy(h => h.Day).ThenBy(h => h.GoalKey, StringComparer.Ordinal))
                sb.Append(hit.Day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                  .Append(SubscriberCsv.Cell(namesByKey.TryGetValue(hit.GoalKey, out var name) ? name : hit.GoalKey)).Append(',')
                  .Append(hit.Hits.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append("\r\n");
            return sb.ToString();
        }

        /// <summary>The same day's hits for one goal added together, so a batch writes each row once.</summary>
        public static List<GoalHit> Merge(IEnumerable<GoalHit> hits) =>
            hits.Where(h => h.Hits > 0 && IsKey(h.GoalKey))
                .GroupBy(h => (h.OwnerId, Day: h.Day.Date, h.GoalKey))
                .Select(g => new GoalHit(g.Key.OwnerId, g.Key.Day, g.Key.GoalKey, g.Sum(h => h.Hits)))
                .ToList();
    }
}
