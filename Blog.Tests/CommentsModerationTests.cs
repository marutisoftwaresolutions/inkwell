using System.Reflection;
using System.Text.RegularExpressions;
using Blog.Core.Interfaces;
using Blog.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Blog.Tests;

/// <summary>
/// The comments queue is a Desk list like any other: bulk moderation wired the same way as the
/// Posts list, search and paging through the shared partials, tabs that announce the current one,
/// a tracked reply form, and a tenant-scoped duplicate check behind the spam filter. Source-scanned
/// and reflected so none of it can quietly regress.
/// </summary>
public class CommentsModerationTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string File_(string rel) => File.ReadAllText(Path.Combine(Root(), rel.Replace('/', Path.DirectorySeparatorChar)));
    private static string View() => File_("Blog.Web/Views/Comments/Index.cshtml");
    private static string Controller() => File_("Blog.Web/Controllers/CommentsController.cs");

    [Fact]
    public void Queue_has_bulk_selection_wired_to_the_bulk_action_with_a_typed_guard_at_five()
    {
        var view = View();
        Assert.Contains("id=\"bulkForm\"", view);
        Assert.Contains("action=\"/admin/comments/bulk\"", view);
        Assert.Contains("form=\"bulkForm\"", view);
        Assert.Contains("id=\"bulkAll\"", view);
        foreach (var option in new[] { "value=\"approve\"", "value=\"pending\"", "value=\"delete\"", "value=\"spam\"" })
            Assert.Contains(option, view);

        // Delete and Spam open the Desk dialog with consequence copy; five or more rows need the typed word.
        Assert.Contains("form.dataset.confirmTitle", view);
        Assert.Contains("form.dataset.confirmBody", view);
        Assert.Contains("form.dataset.confirmAction", view);
        Assert.Equal(2, Regex.Matches(view, @"if \(n >= 5\) form\.dataset\.confirmTyped = 'DELETE'").Count);
    }

    [Fact]
    public void Queue_uses_the_shared_search_and_pager_and_keeps_the_tab_across_a_search()
    {
        var view = View();
        Assert.Contains("_ListSearch", view);
        Assert.Contains("_ListPager", view);
        Assert.DoesNotContain("@for (int i = 1; i <= Model.TotalPages; i++)", view); // the hand-rolled pager is gone

        var controller = Controller();
        Assert.Contains("ViewData[\"ListSearchKeep\"]", controller);
        Assert.Contains("[\"status\"] = tab", controller);
        Assert.Contains("ListPaging.Stash(", controller);
        Assert.Contains("SearchAsync(filter, query)", controller);
        Assert.Contains("CountSearchAsync(filter, query)", controller);
    }

    [Fact]
    public void Tabs_are_links_that_announce_the_current_one_and_cover_pending_approved_and_all()
    {
        var view = View();
        foreach (var status in new[] { "pending", "approved", "all" })
        {
            Assert.Contains($"TabHref(\"{status}\")", view);
            Assert.Contains($"aria-current=\"@(tab == \"{status}\" ? \"page\" : null)\"", view);
        }
        Assert.DoesNotContain("status=\"spam\"", view); // no spam status exists in the domain; the tab must not pretend one does
    }

    [Fact]
    public void Empty_states_are_specific_to_the_tab_and_to_a_search()
    {
        var view = View();
        Assert.Contains("No comments waiting for review.", view);
        Assert.Contains("No approved comments yet.", view);
        Assert.Contains("No comments yet.", view);
        Assert.Contains("Nothing matches “{q}”.", view);
        Assert.DoesNotContain("This list is empty.", view);
        Assert.DoesNotContain("No comments found", view);
    }

    [Fact]
    public void Reply_form_has_a_labelled_textarea_and_is_tracked_for_unsaved_changes()
    {
        var view = View();
        var reply = Regex.Match(view, "<form[^>]*action=\"/admin/comments/reply/[^>]*>", RegexOptions.Singleline);
        Assert.True(reply.Success, "reply form not found");
        Assert.Contains("data-track-changes", reply.Value);

        Assert.Contains("<label for=\"reply-@comment.Id\"", view);
        Assert.Contains("<textarea id=\"reply-@comment.Id\" name=\"content\"", view);
        Assert.Contains("data-dirty-indicator", view);
    }

    [Fact]
    public void Every_checkbox_in_the_queue_has_an_accessible_name()
    {
        var view = View();
        foreach (Match m in Regex.Matches(view, "<input[^>]*type=\"checkbox\"[^>]*>"))
            Assert.True(m.Value.Contains("aria-label="), "checkbox without aria-label: " + m.Value);
    }

    [Fact]
    public void Bulk_action_is_a_protected_post_that_audits_every_outcome_and_reports_spam_once_per_address()
    {
        var bulk = typeof(CommentsController).GetMethod("Bulk", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(bulk);
        Assert.NotNull(bulk!.GetCustomAttribute<HttpPostAttribute>());
        Assert.NotNull(bulk.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
        Assert.Equal("bulk", bulk.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Contains(bulk.GetParameters(), p => p.Name == "action" && p.ParameterType == typeof(string));
        Assert.Contains(bulk.GetParameters(), p => p.Name == "ids" && p.ParameterType == typeof(List<Guid>));

        var controller = Controller();
        var body = controller[controller.IndexOf("[HttpPost(\"bulk\")]", StringComparison.Ordinal)..];
        var end = Regex.Match(body, @"\r?\n    \}\r?\n"); // the method's own closing brace, whatever the line endings
        Assert.True(end.Success, "could not find the end of Bulk");
        body = body[..end.Index];
        foreach (var audit in new[] { "AuditActions.CommentApproved", "AuditActions.CommentRejected", "AuditActions.CommentDeleted", "AuditActions.CommentMarkedSpam" })
            Assert.Contains(audit, body);
        Assert.Contains("RegisterCommentSpamAsync(", body);
        Assert.Contains("reportedAddresses.Add(", body); // one firewall report per address, not per comment
        Assert.DoesNotContain("catch", body); // audit calls are on the success path, never inside a catch
    }

    [Fact]
    public void Duplicate_check_is_scoped_to_the_post_owner()
    {
        var method = typeof(ICommentRepository).GetMethod(nameof(ICommentRepository.HasRecentDuplicateAsync));
        Assert.NotNull(method);
        var parameters = method!.GetParameters();
        Assert.Equal(3, parameters.Length);
        Assert.Equal(typeof(Guid), parameters[0].ParameterType);
        Assert.Equal(typeof(string), parameters[1].ParameterType);
        Assert.Equal(typeof(TimeSpan), parameters[2].ParameterType);

        var repo = File_("Blog.Infrastructure/Data/Repositories/CommentRepository.cs");
        Assert.Contains("p.AuthorId = @PostOwnerId", repo);

        var blog = File_("Blog.Web/Controllers/BlogController.cs");
        Assert.Matches(new Regex(@"HasRecentDuplicateAsync\(\s*post\.AuthorId,", RegexOptions.Singleline), blog);
    }

    [Fact]
    public void Search_treats_the_term_as_data_not_a_pattern()
    {
        var repo = File_("Blog.Infrastructure/Data/Repositories/CommentRepository.cs");
        Assert.Contains("ESCAPE '\\'", repo);
        Assert.Contains("EscapeLike(", repo);
        foreach (var column in new[] { "c.AuthorName LIKE @Like", "c.AuthorEmail LIKE @Like", "c.Content LIKE @Like", "p.Title LIKE @Like" })
            Assert.Contains(column, repo);
    }
}
