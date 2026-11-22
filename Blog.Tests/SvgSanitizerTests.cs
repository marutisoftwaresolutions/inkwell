using System.Text;
using Blog.Core.Services;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// An uploaded SVG is served from the site's own origin, so anything in it that can run or load is
/// removed before it is stored. Each case is a known way to carry script inside an "image".
/// </summary>
public class SvgSanitizerTests
{
    private static string? Clean(string svg)
    {
        var bytes = SvgSanitizer.Sanitize(Encoding.UTF8.GetBytes(svg));
        return bytes is null ? null : Encoding.UTF8.GetString(bytes);
    }

    private const string Ns = "xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\"";

    [Fact]
    public void An_ordinary_drawing_survives_intact()
    {
        var clean = Clean($"<svg {Ns} viewBox=\"0 0 10 10\"><title>Logo</title><defs><linearGradient id=\"g\"><stop offset=\"0\" stop-color=\"#fff\"/></linearGradient></defs><path d=\"M0 0h10v10z\" fill=\"url(#g)\" style=\"stroke:#000\"/><use href=\"#g\"/><a href=\"https://example.com/\"><circle r=\"2\"/></a></svg>");
        Assert.NotNull(clean);
        Assert.Contains("<path", clean);
        Assert.Contains("linearGradient", clean);
        Assert.Contains("viewBox=\"0 0 10 10\"", clean);
        Assert.Contains("style=\"stroke:#000\"", clean);
        Assert.Contains("href=\"#g\"", clean);
        Assert.Contains("https://example.com/", clean);
    }

    [Theory]
    [InlineData("<script>alert(1)</script>", "script")]
    [InlineData("<script xlink:href=\"https://evil.example/x.js\"/>", "evil.example")]
    [InlineData("<foreignObject><body xmlns=\"http://www.w3.org/1999/xhtml\"><img src=\"x\" onerror=\"alert(1)\"/></body></foreignObject>", "foreignObject")]
    [InlineData("<circle r=\"5\" onload=\"alert(1)\" onclick=\"alert(2)\"/>", "alert")]
    [InlineData("<a xlink:href=\"javascript:alert(1)\"><text>x</text></a>", "javascript")]
    [InlineData("<a href=\"java&#10;script:alert(1)\"><text>x</text></a>", "alert")]
    [InlineData("<a href=\"data:text/html,&lt;script&gt;alert(1)&lt;/script&gt;\"><text>x</text></a>", "data:text/html")]
    [InlineData("<a href=\"#\"><set attributeName=\"href\" to=\"javascript:alert(1)\"/><text>x</text></a>", "javascript")]
    [InlineData("<a href=\"#\"><animate attributeName=\"xlink:href\" values=\"javascript:alert(1)\"/><text>x</text></a>", "javascript")]
    [InlineData("<style>@import url(https://evil.example/x.css);</style>", "evil.example")]
    [InlineData("<rect style=\"background:url(javascript:alert(1))\"/>", "javascript")]
    [InlineData("<use href=\"https://evil.example/sprite.svg#x\"/>", "evil.example")]
    [InlineData("<image href=\"https://evil.example/track.png\"/>", "evil.example")]
    [InlineData("<iframe src=\"https://evil.example\"/>", "iframe")]
    public void Anything_that_can_run_or_load_is_removed(string payload, string mustBeGone)
    {
        var clean = Clean($"<svg {Ns}><rect width=\"1\" height=\"1\"/>{payload}</svg>");
        Assert.NotNull(clean);
        Assert.DoesNotContain(mustBeGone, clean, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<rect", clean);   // the drawing itself is kept
    }

    [Fact]
    public void An_event_handler_on_the_root_is_removed()
    {
        var clean = Clean($"<svg {Ns} onload=\"alert(document.cookie)\"><rect/></svg>");
        Assert.NotNull(clean);
        Assert.DoesNotContain("onload", clean);
    }

    [Fact]
    public void A_stylesheet_instruction_and_a_doctype_are_dropped()
    {
        var clean = Clean($"<?xml version=\"1.0\"?><?xml-stylesheet href=\"https://evil.example/x.xsl\" type=\"text/xsl\"?><!DOCTYPE svg PUBLIC \"-//W3C//DTD SVG 1.1//EN\" \"http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd\"><svg {Ns}><rect/></svg>");
        Assert.NotNull(clean);
        Assert.DoesNotContain("xml-stylesheet", clean);
        Assert.DoesNotContain("DOCTYPE", clean);
    }

    [Theory]
    [InlineData("<!DOCTYPE svg [<!ENTITY xxe SYSTEM \"file:///etc/passwd\">]><svg xmlns=\"http://www.w3.org/2000/svg\"><text>&xxe;</text></svg>")]
    [InlineData("<html><body><script>alert(1)</script></body></html>")]
    [InlineData("not xml at all")]
    [InlineData("")]
    public void A_file_that_is_not_a_plain_svg_is_refused(string input)
    {
        Assert.Null(Clean(input));
    }

    [Fact]
    public void An_embedded_raster_image_is_kept_but_an_embedded_document_is_not()
    {
        var clean = Clean($"<svg {Ns}><image href=\"data:image/png;base64,iVBORw0KGgo=\"/><image href=\"data:image/svg+xml;base64,PHN2Zz48L3N2Zz4=\"/></svg>");
        Assert.NotNull(clean);
        Assert.Contains("data:image/png;base64,iVBORw0KGgo=", clean);
        Assert.DoesNotContain("data:image/svg+xml", clean);
    }
}
