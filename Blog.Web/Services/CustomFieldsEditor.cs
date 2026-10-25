using System.Security.Claims;
using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace Blog.Web.Services;

/// <summary>
/// What the post and page editors need for custom fields (ADR-0025): the site's definitions for the
/// content type, and the ViewData the <c>_CustomFieldsPanel</c> partial reads. Validation is
/// <see cref="CustomFields.Clean"/>; this class only fetches and hands over.
/// </summary>
public sealed class CustomFieldsEditor
{
    private readonly ISettingRepository _settings;
    private readonly IUserRepository _users;
    private readonly ITenantContext _tenant;

    public CustomFieldsEditor(ISettingRepository settings, IUserRepository users, ITenantContext tenant)
    {
        _settings = settings;
        _users = users;
        _tenant = tenant;
    }

    /// <summary>The fields that apply to posts (or pages). Settings that cannot be read mean no fields: the editor still works.</summary>
    public async Task<IReadOnlyList<CustomFieldDefinition>> DefinitionsAsync(ClaimsPrincipal user, bool pages)
    {
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId);
        return await DefinitionsForOwnerAsync(userId, pages);
    }

    /// <summary>
    /// The same, for a known site owner: in cloud mode each owner has their own fields; on a single site the
    /// first admin's settings hold them. The API uses this for anonymous reads, where there is no signed-in user.
    /// </summary>
    public async Task<IReadOnlyList<CustomFieldDefinition>> DefinitionsForOwnerAsync(Guid cloudOwnerId, bool pages)
    {
        try
        {
            var ownerId = _tenant.IsCloudMode ? cloudOwnerId : (await _users.GetFirstAdminAsync())?.Id ?? Guid.Empty;
            var s = await _settings.GetSettingsAsync(ownerId);
            return CustomFields.For(s.CustomFields, pages);
        }
        catch { return []; }
    }

    public static void Show(ViewDataDictionary viewData, IReadOnlyList<CustomFieldDefinition> definitions,
        IReadOnlyDictionary<string, string> values, IReadOnlyDictionary<string, string>? errors = null)
    {
        viewData["CustomFieldDefinitions"] = definitions;
        viewData["CustomFieldValues"] = values;
        viewData["CustomFieldErrors"] = errors ?? new Dictionary<string, string>();
    }
}
