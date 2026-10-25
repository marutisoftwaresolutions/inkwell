using Blog.Core.Services;
using Blog.Web.Services;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Blog.Web.TagHelpers;

/// <summary>
/// Every <c>&lt;img&gt;</c> in a view whose <c>src</c> is an upload gets <c>srcset</c> and <c>sizes</c> when narrower copies
/// exist, and <c>width</c>/<c>height</c> when the view did not set them (ADR-0032). A view that knows how wide the image is
/// drawn sets <c>sizes</c> itself; one that must not change sets <c>srcset</c> or <c>data-fixed-image</c>.
/// </summary>
[HtmlTargetElement("img", Attributes = "src")]
public sealed class ResponsiveImageTagHelper : TagHelper
{
    private readonly ResponsiveImageStore _store;
    public ResponsiveImageTagHelper(ResponsiveImageStore store) => _store = store;

    public override int Order => 1000;   // after the framework's own img helper (asp-append-version)

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        if (output.Attributes.ContainsName("srcset") || output.Attributes.ContainsName("data-fixed-image")) return;
        var src = output.Attributes["src"]?.Value?.ToString();
        var path = ResponsiveImages.UploadPath(src);
        if (path is null) return;
        var info = _store.Describe(path);
        if (info is null) return;
        var set = ResponsiveImages.Srcset(path, info);
        if (set is not null)
        {
            output.Attributes.SetAttribute("srcset", set);
            if (!output.Attributes.ContainsName("sizes")) output.Attributes.SetAttribute("sizes", ResponsiveImages.DefaultSizes);
        }
        if (!output.Attributes.ContainsName("width") && !output.Attributes.ContainsName("height"))
        {
            output.Attributes.SetAttribute("width", info.Width);
            output.Attributes.SetAttribute("height", info.Height);
        }
    }
}
