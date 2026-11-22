using Blog.Core.Services;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// The sender check: the DNS question and answer are read correctly, and the SPF, DKIM and DMARC records
/// a domain publishes are turned into a verdict a non-specialist can act on.
/// </summary>
public class SenderCheckTests
{
    private static byte[] Response(ushort id, bool truncated, params string[][] txtRecords)
    {
        var query = DnsTxt.BuildQuery("example.org", id);
        var questionEnd = 12 + 1 + 7 + 1 + 3 + 1 + 4;   // example.org + type + class
        var bytes = new List<byte>(query[..questionEnd]);
        bytes[2] = (byte)(0x81 | (truncated ? 0x02 : 0));   // response, recursion desired, truncated?
        bytes[3] = 0x80;
        bytes[6] = 0; bytes[7] = (byte)txtRecords.Length;   // answers
        bytes[10] = 0; bytes[11] = 0;                        // no additional
        foreach (var record in txtRecords)
        {
            bytes.AddRange([0xC0, 0x0C, 0x00, 0x10, 0x00, 0x01, 0, 0, 0, 60]);   // pointer to the name, TXT, IN, ttl
            var data = new List<byte>();
            foreach (var chunk in record) { data.Add((byte)chunk.Length); data.AddRange(System.Text.Encoding.ASCII.GetBytes(chunk)); }
            bytes.Add((byte)(data.Count >> 8)); bytes.Add((byte)data.Count);
            bytes.AddRange(data);
        }
        return bytes.ToArray();
    }

    [Fact]
    public void A_query_names_the_domain_asks_for_txt_and_announces_a_larger_udp_buffer()
    {
        var q = DnsTxt.BuildQuery("Example.org.", 0x1234);
        Assert.Equal(0x12, q[0]); Assert.Equal(0x34, q[1]);
        Assert.Equal(7, q[12]);                              // "Example"
        Assert.Equal(3, q[20]);                              // "org"
        Assert.Equal(0, q[24]);                              // root
        Assert.Equal(DnsTxt.TypeTxt, (ushort)((q[25] << 8) | q[26]));
        Assert.Equal(41, q[^9]);                             // OPT record type
    }

    [Theory]
    [InlineData("")]
    [InlineData("bücher.example")]
    public void A_name_that_is_not_plain_ascii_is_refused(string name) =>
        Assert.Throws<ArgumentException>(() => DnsTxt.BuildQuery(name, 1));

    [Fact]
    public void A_record_split_into_several_strings_is_joined_and_other_ids_are_ignored()
    {
        var bytes = Response(77, false, ["v=spf1 include:_spf.exam", "ple.net ~all"], ["some-site-verification=abc"]);
        var answer = DnsTxt.Parse(bytes, 77);
        Assert.NotNull(answer);
        Assert.False(answer!.Truncated);
        Assert.Equal(new[] { "v=spf1 include:_spf.example.net ~all", "some-site-verification=abc" }, answer.Texts);
        Assert.Null(DnsTxt.Parse(bytes, 78));
    }

    [Fact]
    public void A_truncated_answer_says_so_and_garbage_is_no_answer()
    {
        Assert.True(DnsTxt.Parse(Response(5, true), 5)!.Truncated);
        Assert.Null(DnsTxt.Parse(new byte[] { 0, 5, 0x81 }, 5));
        var cut = Response(5, false, ["v=spf1 -all"]);
        Assert.NotNull(DnsTxt.Parse(cut.AsSpan(0, cut.Length - 4), 5));   // a short record is skipped, not thrown
    }

    [Theory]
    [InlineData("News <news@Example.ORG>", null)]
    [InlineData("news@Example.ORG", "example.org")]
    [InlineData("news@localhost", null)]
    [InlineData("not-an-address", null)]
    [InlineData(null, null)]
    public void The_sender_domain_comes_from_the_bare_address(string? from, string? expected) =>
        Assert.Equal(expected, MailAuthCheck.DomainOf(from));

    [Fact]
    public void Spf_must_exist_once_and_must_not_allow_everyone()
    {
        Assert.Equal(MailAuthCheck.Level.Ok, MailAuthCheck.Spf(["v=spf1 include:_spf.example.net ~all", "other=1"]).Level);
        Assert.Equal(MailAuthCheck.Level.Problem, MailAuthCheck.Spf(["other=1"]).Level);
        Assert.Equal(MailAuthCheck.Level.Problem, MailAuthCheck.Spf(["v=spf1 a -all", "v=spf1 mx -all"]).Level);
        Assert.Equal(MailAuthCheck.Level.Problem, MailAuthCheck.Spf(["v=spf1 +all"]).Level);
        Assert.Equal(MailAuthCheck.Level.Warning, MailAuthCheck.Spf(["v=spf1 include:x.example"]).Level);
        Assert.Equal(MailAuthCheck.Level.Ok, MailAuthCheck.Spf(["v=spf1 redirect=_spf.example.net"]).Level);   // delegated: no "all" needed
        Assert.Equal(MailAuthCheck.Level.Warning, MailAuthCheck.Spf(null).Level);   // could not be read is not "missing"
    }

    [Fact]
    public void Dmarc_with_any_valid_policy_meets_the_requirement()
    {
        Assert.Equal(MailAuthCheck.Level.Ok, MailAuthCheck.Dmarc(["v=DMARC1; p=none; rua=mailto:a@example.org"]).Level);
        Assert.Equal(MailAuthCheck.Level.Ok, MailAuthCheck.Dmarc(["v=DMARC1;p=reject"]).Level);
        Assert.Equal(MailAuthCheck.Level.Warning, MailAuthCheck.Dmarc(["v=DMARC1; rua=mailto:a@example.org"]).Level);
        Assert.Equal(MailAuthCheck.Level.Problem, MailAuthCheck.Dmarc([]).Level);
    }

    [Fact]
    public void Dkim_is_checked_only_with_a_selector_and_an_empty_key_is_a_revoked_key()
    {
        Assert.Equal(MailAuthCheck.Level.Warning, MailAuthCheck.Dkim(null, null).Level);
        Assert.Equal(MailAuthCheck.Level.Ok, MailAuthCheck.Dkim("s1", ["v=DKIM1; k=rsa; p=MIIBIjANBg"]).Level);
        Assert.Equal(MailAuthCheck.Level.Problem, MailAuthCheck.Dkim("s1", ["v=DKIM1; k=rsa; p="]).Level);
        Assert.Equal(MailAuthCheck.Level.Problem, MailAuthCheck.Dkim("s1", []).Level);
        Assert.Null(MailAuthCheck.CleanSelector("bad.selector"));
        Assert.Equal("s1", MailAuthCheck.CleanSelector(" S1 "));
    }

    [Fact]
    public void The_verdict_needs_spf_or_dkim_and_a_dmarc_record()
    {
        MailAuthCheck.Finding F(string r, MailAuthCheck.Level l) => new(r, l, "");
        Assert.Equal(MailAuthCheck.Level.Ok, MailAuthCheck.Verdict([F("SPF", MailAuthCheck.Level.Ok), F("DKIM", MailAuthCheck.Level.Ok), F("DMARC", MailAuthCheck.Level.Ok)]).Level);
        Assert.Equal(MailAuthCheck.Level.Warning, MailAuthCheck.Verdict([F("SPF", MailAuthCheck.Level.Ok), F("DKIM", MailAuthCheck.Level.Warning), F("DMARC", MailAuthCheck.Level.Ok)]).Level);
        Assert.Equal(MailAuthCheck.Level.Problem, MailAuthCheck.Verdict([F("SPF", MailAuthCheck.Level.Problem), F("DKIM", MailAuthCheck.Level.Warning), F("DMARC", MailAuthCheck.Level.Ok)]).Level);
        Assert.Equal(MailAuthCheck.Level.Problem, MailAuthCheck.Verdict([F("SPF", MailAuthCheck.Level.Ok), F("DKIM", MailAuthCheck.Level.Ok), F("DMARC", MailAuthCheck.Level.Problem)]).Level);
    }
}
