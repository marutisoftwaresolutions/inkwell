using Blog.Core.Domain;
using Blog.Core.Services;
using Microsoft.AspNetCore.DataProtection;

namespace Blog.Web.Services.SearchConsole;

/// <summary>
/// Protects and unprotects the service-account key file stored in a tenant's settings. The clear
/// JSON exists only in memory, for the duration of a token exchange. A key ring that has rotated
/// yields null rather than an exception, and the caller reports "reconnect" instead of failing.
/// </summary>
public sealed class SearchConsoleCredentialStore
{
    private readonly IDataProtector _protector;

    public SearchConsoleCredentialStore(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("Inkwell.SearchConsole.ServiceAccount.v1");
    }

    public string Protect(string serviceAccountJson) => _protector.Protect(serviceAccountJson);

    /// <summary>The tenant's credential, or null when none is stored or it can no longer be read.</summary>
    public ServiceAccountCredential? Read(UserSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.SearchConsoleCredentialProtected)) return null;
        try
        {
            var json = _protector.Unprotect(settings.SearchConsoleCredentialProtected);
            return ServiceAccountCredential.TryParse(json, out _);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Configured means a property and a readable credential are both present.</summary>
    public bool IsConfigured(UserSettings settings) =>
        !string.IsNullOrWhiteSpace(settings.SearchConsoleProperty) && Read(settings) is not null;
}
