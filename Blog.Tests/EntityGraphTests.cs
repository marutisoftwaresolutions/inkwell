using Blog.Core.Domain;
using Blog.Core.Services;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// Entity data goes into structured data that search and answer engines act on, so the rule here is
/// stricter than "does it render": nothing may be inferred, reshaped, or emitted when the operator
/// did not supply it.
/// </summary>
public class EntityGraphTests
{
    // ── Publisher type ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Organization", "Organization")]
    [InlineData("Person", "Person")]
    [InlineData("person", "Person")]        // case is normalised to the schema.org spelling
    [InlineData("LocalBusiness", "Organization")]
    [InlineData("", "Organization")]
    [InlineData(null, "Organization")]
    public void An_unsupported_publisher_type_falls_back_instead_of_emitting_invalid_schema(
        string? configured, string expected) =>
        Assert.Equal(expected, EntityGraph.ResolveType(configured));

    // ── sameAs ────────────────────────────────────────────────────────────────

    [Fact]
    public void Social_links_and_authority_profiles_are_combined_in_order()
    {
        var settings = new UserSettings
        {
            EntitySameAs = "https://en.wikipedia.org/wiki/Example\nhttps://www.wikidata.org/wiki/Q1"
        };

        var result = EntityGraph.SameAs(settings, "https://x.com/example", "https://github.com/example");

        Assert.Equal(
            ["https://x.com/example", "https://github.com/example",
             "https://en.wikipedia.org/wiki/Example", "https://www.wikidata.org/wiki/Q1"],
            result);
    }

    [Theory]
    [InlineData("@example")]                 // a bare handle
    [InlineData("/about")]                   // a relative path
    [InlineData("example.com")]              // no scheme
    [InlineData("ftp://example.com")]        // not http(s)
    public void Anything_that_is_not_an_absolute_http_url_is_dropped(string value)
    {
        // An invalid entry in sameAs devalues the whole node, so it must never reach the output.
        var settings = new UserSettings { EntitySameAs = value };

        Assert.Empty(EntityGraph.SameAs(settings));
    }

    [Fact]
    public void Duplicates_are_removed_regardless_of_case()
    {
        var settings = new UserSettings { EntitySameAs = "https://X.com/Example" };

        Assert.Single(EntityGraph.SameAs(settings, "https://x.com/example"));
    }

    [Fact]
    public void Profiles_may_be_separated_by_newlines_or_commas()
    {
        var settings = new UserSettings { EntitySameAs = "https://a.test/x, https://b.test/y\r\nhttps://c.test/z" };

        Assert.Equal(3, EntityGraph.SameAs(settings).Count);
    }

    [Fact]
    public void Nothing_configured_produces_an_empty_list_not_a_null() =>
        Assert.Empty(EntityGraph.SameAs(new UserSettings(), null, "", "   "));

    // ── Founding date ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("2019-04-01", "2019-04-01")]
    [InlineData("2019", "2019")]
    [InlineData("1 April 2019", "2019-04-01")]
    public void A_usable_founding_date_is_normalised(string input, string expected) =>
        Assert.Equal(expected, EntityGraph.FoundingDate(input));

    [Theory]
    [InlineData("sometime in 2019")]
    [InlineData("recently")]
    [InlineData("")]
    [InlineData(null)]
    public void An_unusable_founding_date_is_dropped_rather_than_guessed(string? input) =>
        Assert.Null(EntityGraph.FoundingDate(input));

    // ── Identity statement ────────────────────────────────────────────────────

    [Fact]
    public void The_operators_own_statement_wins()
    {
        var settings = new UserSettings
        {
            EntityIdentityStatement = "Inkwell Review is a reviews blog. It is not the software vendor of the same name."
        };

        Assert.Equal(settings.EntityIdentityStatement,
            EntityGraph.IdentityStatement(settings, "Inkwell Review", "https://example.test", ["optics"]));
    }

    [Fact]
    public void A_blog_that_never_opens_settings_still_states_who_it_is()
    {
        var text = EntityGraph.IdentityStatement(new UserSettings(), "Example Review", "https://example.test", ["optics", "billing"]);

        Assert.Contains("Example Review", text);
        Assert.Contains("https://example.test", text);
        Assert.Contains("optics", text);
    }

    [Fact]
    public void A_blog_with_no_categories_still_produces_a_readable_statement()
    {
        var text = EntityGraph.IdentityStatement(new UserSettings(), "Example", "https://example.test", []);

        Assert.DoesNotContain("covering ,", text);
        Assert.Contains("Example", text);
    }

    // ── Facts ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Only_supplied_facts_are_listed()
    {
        var settings = new UserSettings { EntityFounder = "Ada Lovelace", EntityFoundingDate = "not a date" };

        var facts = EntityGraph.IdentityFacts(settings);

        Assert.Equal(("Founder", "Ada Lovelace"), Assert.Single(facts));
    }

    [Fact]
    public void Nothing_supplied_means_nothing_claimed() =>
        Assert.Empty(EntityGraph.IdentityFacts(new UserSettings()));

    [Fact]
    public void Whitespace_only_values_are_not_treated_as_facts() =>
        Assert.Empty(EntityGraph.IdentityFacts(new UserSettings
        {
            EntityLegalName = "   ",
            EntityFounder = "\t"
        }));

    [Fact]
    public void A_default_settings_object_declares_an_organization() =>
        Assert.Equal("Organization", EntityGraph.ResolveType(new UserSettings().EntityType));
}
