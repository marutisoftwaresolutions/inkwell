using Blog.Core.Services;
using Blog.Web.Services;
using Xunit;
using Xunit.Abstractions;

namespace Blog.Tests;

/// <summary>
/// Asks this machine's real DNS resolver for well-known TXT records. Opt-in, because it needs the
/// network: set INKWELL_LIVE_DNS=1. Without it the test returns at once.
/// </summary>
public class SenderCheckLiveDnsTests
{
    private readonly ITestOutputHelper _out;
    public SenderCheckLiveDnsTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public async Task A_real_resolver_returns_spf_and_dmarc_for_a_large_mail_domain()
    {
        if (Environment.GetEnvironmentVariable("INKWELL_LIVE_DNS") != "1") return;
        var dns = new DnsTxtLookup();
        var spf = await dns.QueryAsync("gmail.com");
        var dmarc = await dns.QueryAsync("_dmarc.gmail.com");
        var missing = await dns.QueryAsync("no-such-name-" + Guid.NewGuid().ToString("N") + ".example.org");
        _out.WriteLine("SPF: " + MailAuthCheck.Spf(spf).Message);
        _out.WriteLine("DMARC: " + MailAuthCheck.Dmarc(dmarc).Message);
        Assert.NotNull(spf);
        Assert.Equal(MailAuthCheck.Level.Ok, MailAuthCheck.Spf(spf).Level);
        Assert.Equal(MailAuthCheck.Level.Ok, MailAuthCheck.Dmarc(dmarc).Level);
        Assert.NotNull(missing);          // "no such name" is an answer, not a failure
        Assert.Empty(missing!);
    }
}
