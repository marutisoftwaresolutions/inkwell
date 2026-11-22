using Xunit;

namespace Blog.Tests;

/// <summary>
/// Draft recovery keeps a copy of unsaved editor work in the browser. It must never save by itself,
/// never disarm the unsaved-changes guard, and every editor must opt in; structural changes in the
/// block builder must count as unsaved work.
/// </summary>
public class DraftRecoveryTests
{
    private static string Read(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray()));
    }

    [Fact]
    public void The_script_keeps_a_copy_and_never_talks_to_the_server_or_clears_the_guard()
    {
        var js = Read("Blog.Web", "wwwroot", "js", "inkwell-autosave.js");
        Assert.Contains("localStorage", js);
        Assert.DoesNotContain("fetch(", js);
        Assert.DoesNotContain("XMLHttpRequest", js);
        Assert.DoesNotContain(".submit(", js);
        Assert.DoesNotContain("desk-has-unsaved')", js.Replace("contains('desk-has-unsaved')", ""));   // it reads the flag, never sets or removes it
        Assert.Contains("(not saved)", js);
        // A cancelled confirmation must not throw the copy away.
        Assert.Contains("if (e.defaultPrevented) return;", js);
    }

    [Theory]
    [InlineData("Posts", "Edit.cshtml")]
    [InlineData("Posts", "Create.cshtml")]
    [InlineData("Pages", "Edit.cshtml")]
    [InlineData("Pages", "Create.cshtml")]
    public void Every_editor_opts_in_and_has_a_status_line(string folder, string view)
    {
        var html = Read("Blog.Web", "Views", folder, view);
        Assert.Contains("data-local-backup=", html);
        Assert.Contains("data-saved-at=", html);
        Assert.Contains("data-backup-status", html);
        Assert.Contains("~/js/inkwell-autosave.js", html);
        Assert.Contains("data-track-changes", html);
    }

    [Fact]
    public void A_new_item_has_no_saved_version_to_outrank_its_copy()
    {
        Assert.Contains("data-local-backup=\"post:new\" data-saved-at=\"0\"", Read("Blog.Web", "Views", "Posts", "Create.cshtml"));
        Assert.Contains("data-local-backup=\"page:new\" data-saved-at=\"0\"", Read("Blog.Web", "Views", "Pages", "Create.cshtml"));
    }

    [Fact]
    public void Adding_moving_duplicating_and_deleting_a_block_count_as_unsaved_changes()
    {
        var builder = Read("Blog.Web", "wwwroot", "js", "inkwell-builder.js");
        Assert.Contains("function changed() { document.dispatchEvent(new CustomEvent('inkwell:content-changed')); }", builder);
        Assert.True(builder.Split("changed();").Length - 1 >= 5);
        Assert.Contains("window.InkwellBuilderInstance = api;", builder);
        var desk = Read("Blog.Web", "wwwroot", "js", "inkwell-desk.js");
        Assert.Contains(".builder-block > .block-content", desk);
    }
}
