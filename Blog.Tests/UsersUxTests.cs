using System.Text.RegularExpressions;
using Blog.Core.Services;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// Guards for the Users screen (2026-09-19 review, findings H9-H11): an admin cannot disable their
/// own account, a role change is an explicit confirmed action rather than a select's change event,
/// and an invite never requires typing someone else's password by hand. Source-scanning where the
/// convention lives in a view or controller; pure unit tests for <see cref="PasswordGenerator"/>.
/// </summary>
public class UsersUxTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string Web(string rel) => File.ReadAllText(Path.Combine(Root(), "Blog.Web", rel.Replace('/', Path.DirectorySeparatorChar)));
    private static string View() => Web("Views/Users/Index.cshtml");
    private static string Controller() => Web("Controllers/UsersController.cs");

    // -- H9: disable / enable ----------------------------------------------------------------

    [Fact]
    public void ToggleActive_refuses_the_callers_own_account()
    {
        var src = Controller();
        var start = src.IndexOf("ToggleActive(", StringComparison.Ordinal);
        Assert.True(start > 0, "ToggleActive action not found");
        var end = src.IndexOf("[HttpPost(\"delete\")]", start, StringComparison.Ordinal);
        Assert.True(end > start, "Delete action should follow ToggleActive");
        var body = src[start..end];

        Assert.Contains("currentUserId == userId", body);
        Assert.Contains("You cannot disable your own account.", body);
        // The guard runs before the write, not after it.
        Assert.True(body.IndexOf("You cannot disable your own account.", StringComparison.Ordinal)
                  < body.IndexOf("user.IsActive = !user.IsActive", StringComparison.Ordinal),
            "self-guard must precede the status flip");
    }

    [Fact]
    public void Disable_form_opens_the_confirmation_dialog_with_consequence_copy()
    {
        var view = View();
        var forms = Regex.Matches(view, "<form\\b[^>]*action=\"/admin/users/toggle-active\"[^>]*>");
        Assert.True(forms.Count >= 1, "toggle-active form not found");
        var disable = forms.Cast<Match>().Where(m => m.Value.Contains("data-confirm-title=\"Disable this user?\"")).ToList();
        Assert.True(disable.Count == 1, "exactly one Disable form should carry the dialog; Enable needs none");
        Assert.Contains("data-confirm-tone=\"warning\"", disable[0].Value);
        Assert.Contains("data-confirm-action=\"Disable user\"", disable[0].Value);
        Assert.Contains("cannot sign in until re-enabled", disable[0].Value);
    }

    [Fact]
    public void Disable_is_not_offered_on_the_callers_own_row()
    {
        var view = View();
        // The toggle forms sit inside a guard that excludes the current user.
        Assert.Contains("@if (isAdmin && !isSelf)", view);
        Assert.Contains("var isSelf = user.Id == currentUserId;", view);
    }

    // -- H10: role change --------------------------------------------------------------------

    [Fact]
    public void Role_select_no_longer_auto_submits_on_change()
    {
        var view = View();
        Assert.DoesNotContain("onchange=\"this.form.submit()\"", view);
        Assert.DoesNotMatch(new Regex("onchange=\"[^\"]*submit", RegexOptions.IgnoreCase), view);
    }

    [Fact]
    public void Role_change_is_an_explicit_confirmed_button()
    {
        var view = View();
        var form = Regex.Match(view, "<form\\b[^>]*action=\"/admin/users/update-role\"[^>]*>");
        Assert.True(form.Success, "update-role form not found");
        Assert.Contains("data-role-form", form.Value);
        Assert.Contains("data-confirm-title=", form.Value);
        Assert.Contains("data-confirm-body=", form.Value);
        Assert.Contains("data-confirm-action=", form.Value);
        Assert.Contains("data-confirm-tone=\"warning\"", form.Value);

        // A submit button that starts disabled and is enabled only once the selection differs.
        var end = view.IndexOf("</form>", form.Index, StringComparison.Ordinal);
        var body = view[form.Index..end];
        Assert.Matches(new Regex("<button type=\"submit\" disabled\\b"), body);
        Assert.Contains("data-current=\"@currentRole\"", body);

        // The script fills the dialog with old -> new copy.
        Assert.Contains("'Change role to ' + next + '?'", view);
        Assert.Contains("moves from ' + current + ' to ' + next", view);
    }

    // -- H11: invite password ----------------------------------------------------------------

    [Fact]
    public void Invite_inputs_are_labelled_and_the_password_field_is_marked_new_password()
    {
        var view = View();
        foreach (var id in new[] { "invite-name", "invite-email", "invite-password", "invite-role" })
        {
            Assert.Contains($"for=\"{id}\"", view);
            Assert.Contains($"id=\"{id}\"", view);
        }
        var pw = Regex.Match(view, "<input\\b[^>]*id=\"invite-password\"[^>]*>");
        Assert.True(pw.Success);
        Assert.Contains("autocomplete=\"new-password\"", pw.Value);
        Assert.DoesNotContain(" required", pw.Value); // blank means "generate one for me"
    }

    [Fact]
    public void Invite_has_generate_and_a_show_hide_toggle_with_aria_pressed()
    {
        var view = View();
        Assert.Contains("id=\"invite-password-generate\"", view);
        var toggle = Regex.Match(view, "<button\\b[^>]*id=\"invite-password-toggle\"[^>]*>");
        Assert.True(toggle.Success);
        Assert.Contains("aria-pressed=\"false\"", toggle.Value);
        Assert.Contains("aria-label=", toggle.Value);
        Assert.Contains("aria-controls=\"invite-password\"", toggle.Value);
    }

    [Fact]
    public void Successful_invite_shows_the_password_once_via_TempData_with_a_copy_button()
    {
        var controller = Controller();
        Assert.Contains("TempData[\"InvitePassword\"] = password;", controller);
        Assert.Contains("PasswordGenerator.Generate()", controller); // blank password -> generated server-side

        var view = View();
        Assert.Contains("TempData[\"InvitePassword\"] as string", view);
        Assert.Contains("id=\"invite-once\"", view);
        Assert.Contains("id=\"invite-once-copy\"", view);
        Assert.Contains("aria-labelledby=\"invite-once-title\"", view);
    }

    [Fact]
    public void Nothing_is_emailed_by_the_invite_action()
    {
        var controller = Controller();
        Assert.DoesNotContain("IEmailService", controller);
        Assert.DoesNotContain("SendAsync(", controller);
    }

    // -- Copy and accessible names -----------------------------------------------------------

    [Fact]
    public void Header_and_seed_copy_are_plain_sentences_without_emoji()
    {
        var view = View();
        Assert.DoesNotContain("\U0001F331", view); // seedling
        Assert.DoesNotContain("⚠", view);     // warning sign
        Assert.DoesNotContain("Click here", view);
        Assert.DoesNotContain("Seed Data", view);  // title case on a button is a copy defect
    }

    [Fact]
    public void Every_row_action_button_has_an_accessible_name()
    {
        var view = View();
        foreach (var verb in new[] { "Disable", "Enable", "Delete", "Change role for" })
            Assert.Contains($"aria-label=\"{verb} @displayName\"", view);
        Assert.Contains("aria-label=\"Dismiss password panel\"", view);
    }

    // -- PasswordGenerator (pure) ------------------------------------------------------------

    [Theory]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(64)]
    public void Generate_returns_exactly_the_requested_length(int length)
    {
        Assert.Equal(length, PasswordGenerator.Generate(length).Length);
    }

    [Fact]
    public void Generate_defaults_to_sixteen_characters()
    {
        Assert.Equal(16, PasswordGenerator.Generate().Length);
        Assert.Equal(16, PasswordGenerator.DefaultLength);
    }

    [Fact]
    public void Generate_always_contains_a_lower_an_upper_and_a_digit()
    {
        for (var i = 0; i < 200; i++)
        {
            var p = PasswordGenerator.Generate(8);
            Assert.True(p.Any(char.IsLower), p);
            Assert.True(p.Any(char.IsUpper), p);
            Assert.True(p.Any(char.IsDigit), p);
        }
    }

    [Fact]
    public void Generate_never_emits_ambiguous_characters()
    {
        const string ambiguous = "0OIl1";
        for (var i = 0; i < 200; i++)
        {
            var p = PasswordGenerator.Generate(32);
            Assert.DoesNotContain(p, c => ambiguous.Contains(c));
            Assert.All(p, c => Assert.Contains(c, PasswordGenerator.Alphabet));
        }
        Assert.DoesNotContain(ambiguous, c => PasswordGenerator.Alphabet.Contains(c));
    }

    [Fact]
    public void Generate_produces_distinct_passwords()
    {
        var samples = Enumerable.Range(0, 100).Select(_ => PasswordGenerator.Generate()).ToList();
        Assert.Equal(100, samples.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Generate_does_not_pin_the_guaranteed_classes_to_the_front()
    {
        // If the shuffle were missing, position 0 would always be lower-case.
        var firstIsAlwaysLower = Enumerable.Range(0, 200).All(_ => char.IsLower(PasswordGenerator.Generate()[0]));
        Assert.False(firstIsAlwaysLower);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(7)]
    public void Generate_rejects_lengths_below_the_invite_minimum(int length)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PasswordGenerator.Generate(length));
    }
}
