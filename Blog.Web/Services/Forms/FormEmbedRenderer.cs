using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services.Forms;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using static System.Net.WebUtility;

namespace Blog.Web.Services.Forms;

/// <summary>
/// Turns the Form block's placeholder (<c>&lt;div class="comp-form" data-form="key"&gt;&lt;/div&gt;</c>) into the real form
/// when a page is shown to a reader (ADR-0030): labels, required marks, an anti-forgery token, a form-bound timing
/// token and the honeypot. It works without JavaScript; the last attempt's errors and values come back through
/// TempData and are shown at each field. A form that does not exist or is switched off renders nothing.
/// </summary>
public sealed class FormEmbedRenderer
{
    public const string TokenField = "formToken";
    public const string ReturnField = "returnTo";
    public const string FieldPrefix = "f_";

    // The block's markup as the builder writes it; attribute order is not relied on.
    private static readonly Regex Placeholder = new(
        @"<div\b(?=[^>]*\bclass=""comp-form"")(?=[^>]*\bdata-form=""(?<key>[a-z0-9-]{1,60})"")[^>]*>\s*</div>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly IFormRepository _forms;
    private readonly CommentFormTokenService _tokens;
    private readonly IAntiforgery _antiforgery;
    private readonly ITempDataDictionaryFactory _tempData;
    private readonly ITenantContext _tenant;

    public FormEmbedRenderer(IFormRepository forms, CommentFormTokenService tokens, IAntiforgery antiforgery,
        ITempDataDictionaryFactory tempData, ITenantContext tenant)
    {
        _forms = forms;
        _tokens = tokens;
        _antiforgery = antiforgery;
        _tempData = tempData;
        _tenant = tenant;
    }

    public static bool HasForm(string? html) => !string.IsNullOrEmpty(html) && html.Contains("comp-form", StringComparison.Ordinal);

    public static string ErrorsKey(string key) => $"Form:{key}:Errors";
    public static string ValuesKey(string key) => $"Form:{key}:Values";
    public static string SentKey(string key) => $"Form:{key}:Sent";
    public static string NoticeKey(string key) => $"Form:{key}:Notice";

    public async Task<string> ExpandAsync(string? html, HttpContext context)
    {
        if (!HasForm(html)) return html ?? "";
        var owner = TrafficRecorder.OwnerOf(context);
        var matches = Placeholder.Matches(html!);
        if (matches.Count == 0) return html!;

        var rendered = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match m in matches)
        {
            var key = m.Groups["key"].Value.ToLowerInvariant();
            if (rendered.ContainsKey(key)) continue;
            var form = await _forms.GetByKeyAsync(key, owner);
            rendered[key] = form is { IsActive: true } ? Render(form, context) : "";
        }
        return Placeholder.Replace(html!, m => rendered.GetValueOrDefault(m.Groups["key"].Value.ToLowerInvariant(), ""));
    }

    private string Render(ContactForm form, HttpContext context)
    {
        var fields = FormRules.ParseFields(form.FieldsJson);
        var temp = _tempData.GetTempData(context);
        var errors = Read<Dictionary<string, string>>(temp, ErrorsKey(form.FormKey)) ?? new();
        var values = Read<Dictionary<string, string>>(temp, ValuesKey(form.FormKey)) ?? new();
        var sent = temp[SentKey(form.FormKey)] as string;
        var notice = temp[NoticeKey(form.FormKey)] as string;
        var tokens = _antiforgery.GetAndStoreTokens(context);
        var id = "form-" + form.FormKey;
        var returnTo = context.Request.Path.Value ?? "/";

        var sb = new StringBuilder();
        sb.Append("<form class=\"ink-form\" id=\"").Append(id).Append("\" method=\"post\" action=\"")
          .Append(HtmlEncode(_tenant.TenantPathPrefix + "/forms/" + form.FormKey)).Append("\" data-ink-form aria-labelledby=\"").Append(id).Append("-title\">");
        sb.Append("<h2 class=\"ink-form-title\" id=\"").Append(id).Append("-title\">").Append(HtmlEncode(form.Name)).Append("</h2>");
        sb.Append("<input type=\"hidden\" name=\"").Append(HtmlEncode(tokens.FormFieldName)).Append("\" value=\"").Append(HtmlEncode(tokens.RequestToken)).Append("\" />");
        sb.Append("<input type=\"hidden\" name=\"").Append(TokenField).Append("\" value=\"").Append(HtmlEncode(_tokens.Issue(form.Id))).Append("\" />");
        sb.Append("<input type=\"hidden\" name=\"").Append(ReturnField).Append("\" value=\"").Append(HtmlEncode(returnTo)).Append("\" />");
        // Honeypot: off-screen, unlabeled for people, skipped by the keyboard. Only software fills it.
        sb.Append("<div class=\"ink-form-hp\" aria-hidden=\"true\"><label>Leave this empty <input type=\"text\" name=\"")
          .Append(CommentFormTokenService.HoneypotFieldName).Append("\" tabindex=\"-1\" autocomplete=\"off\" /></label></div>");

        sb.Append("<div class=\"ink-form-status\" role=\"status\" aria-live=\"polite\" data-ink-form-status>");
        if (sent is not null) sb.Append("<p class=\"ink-form-sent\">").Append(HtmlEncode(sent)).Append("</p>");
        else if (notice is not null) sb.Append("<p class=\"ink-form-error\">").Append(HtmlEncode(notice)).Append("</p>");
        else if (errors.Count > 0) sb.Append("<p class=\"ink-form-error\">Check the ").Append(errors.Count == 1 ? "field" : $"{errors.Count} fields").Append(" marked below.</p>");
        sb.Append("</div>");

        foreach (var f in fields)
        {
            var fid = id + "-" + f.Key;
            var name = FieldPrefix + f.Key;
            var value = sent is null ? values.GetValueOrDefault(f.Key) ?? "" : "";
            var error = errors.GetValueOrDefault(f.Key);
            var describedBy = string.Join(' ', new[] { f.Help is null ? null : fid + "-help", error is null ? null : fid + "-error" }.Where(x => x is not null));
            var aria = (describedBy.Length > 0 ? $" aria-describedby=\"{describedBy}\"" : "") + (error is null ? "" : " aria-invalid=\"true\"") + (f.Required ? " required" : "");
            var required = f.Required ? "<span class=\"ink-form-req\" aria-hidden=\"true\"> *</span>" : "";

            sb.Append("<div class=\"ink-form-field").Append(error is null ? "" : " has-error").Append("\" data-field=\"").Append(f.Key).Append("\">");
            if (f.Type is FormFieldTypes.Checkbox or FormFieldTypes.Consent)
            {
                sb.Append("<label class=\"ink-form-check\" for=\"").Append(fid).Append("\"><input type=\"checkbox\" id=\"").Append(fid)
                  .Append("\" name=\"").Append(name).Append("\" value=\"yes\"").Append(value == "yes" ? " checked" : "").Append(aria).Append(" /> <span>")
                  .Append(HtmlEncode(f.Label)).Append(required).Append("</span></label>");
            }
            else
            {
                sb.Append("<label for=\"").Append(fid).Append("\">").Append(HtmlEncode(f.Label)).Append(required).Append("</label>");
                switch (f.Type)
                {
                    case FormFieldTypes.LongText:
                        sb.Append("<textarea id=\"").Append(fid).Append("\" name=\"").Append(name).Append("\" rows=\"5\" maxlength=\"")
                          .Append(FormRules.MaxLongTextLength).Append('"').Append(aria).Append('>').Append(HtmlEncode(value)).Append("</textarea>");
                        break;
                    case FormFieldTypes.Choice:
                        sb.Append("<select id=\"").Append(fid).Append("\" name=\"").Append(name).Append('"').Append(aria).Append("><option value=\"\">Choose…</option>");
                        foreach (var o in f.Options)
                            sb.Append("<option").Append(o == value ? " selected" : "").Append('>').Append(HtmlEncode(o)).Append("</option>");
                        sb.Append("</select>");
                        break;
                    default:
                        var (type, auto) = f.Type switch
                        {
                            FormFieldTypes.Email => ("email", "email"),
                            FormFieldTypes.Name => ("text", "name"),
                            _ => ("text", "off"),
                        };
                        sb.Append("<input type=\"").Append(type).Append("\" id=\"").Append(fid).Append("\" name=\"").Append(name).Append("\" autocomplete=\"")
                          .Append(auto).Append("\" maxlength=\"").Append(FormRules.MaxShortTextLength).Append("\" value=\"").Append(HtmlEncode(value)).Append('"').Append(aria).Append(" />");
                        break;
                }
            }
            if (f.Help is not null) sb.Append("<p class=\"ink-form-help\" id=\"").Append(fid).Append("-help\">").Append(HtmlEncode(f.Help)).Append("</p>");
            sb.Append("<p class=\"ink-form-field-error\" id=\"").Append(fid).Append("-error\"").Append(error is null ? " hidden" : "").Append('>')
              .Append(HtmlEncode(error ?? "")).Append("</p>");
            sb.Append("</div>");
        }
        sb.Append("<button type=\"submit\" class=\"ink-form-submit\">").Append(HtmlEncode(string.IsNullOrWhiteSpace(form.SubmitLabel) ? "Send" : form.SubmitLabel)).Append("</button>");
        sb.Append("</form>");
        return sb.ToString();
    }

    private static T? Read<T>(ITempDataDictionary temp, string key) where T : class
    {
        if (temp[key] is not string json) return null;
        try { return JsonSerializer.Deserialize<T>(json); } catch (JsonException) { return null; }
    }
}
