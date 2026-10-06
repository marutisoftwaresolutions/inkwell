using System.Security.Cryptography;
using System.Text;
using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;
using QRCoder;

namespace Blog.Web.Services;

public enum TwoFactorCheck { Ok, OkWithRecoveryCode, Invalid, SecretUnreadable }

/// <summary>A started (not yet confirmed) enrolment: shown on the setup page, kept in a signed hidden field.</summary>
public sealed record TwoFactorEnrolment(string Token, string SecretForDisplay, string QrSvg);

/// <summary>
/// Two-factor sign-in (ADR-0021). The secret is stored Data-Protection-encrypted; a secret being set up
/// travels only in a time-limited protected token until the user proves it with a code; recovery codes
/// are shown once and stored as SHA-256 hashes. Between password and code the user holds a five-minute
/// protected ticket, not a sign-in. Which roles must use two-factor is a site setting
/// (<see cref="UserSettings.TwoFactorRequiredRoles"/>), off for every role by default.
/// </summary>
public sealed class TwoFactorService
{
    public const string TicketCookie = "inkwell.2fa";
    public static readonly TimeSpan TicketLifetime = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan EnrolmentLifetime = TimeSpan.FromMinutes(15);
    public const int MaxAttemptsPerTicket = 5;
    private static readonly TimeSpan PolicyCacheTtl = TimeSpan.FromSeconds(60);

    private readonly IUserRepository _users;
    private readonly ISettingRepository _settings;
    private readonly IMemoryCache _cache;
    private readonly IDataProtector _secrets;
    private readonly ITimeLimitedDataProtector _enrolments;
    private readonly ITimeLimitedDataProtector _tickets;

    public TwoFactorService(IUserRepository users, ISettingRepository settings, IMemoryCache cache, IDataProtectionProvider dp)
    {
        _users = users;
        _settings = settings;
        _cache = cache;
        _secrets = dp.CreateProtector("Inkwell.TwoFactor.Secret.v1");
        _enrolments = dp.CreateProtector("Inkwell.TwoFactor.Enrolment.v1").ToTimeLimitedDataProtector();
        _tickets = dp.CreateProtector("Inkwell.TwoFactor.Ticket.v1").ToTimeLimitedDataProtector();
    }

    // ── Policy ──────────────────────────────────────────────────────────────────

    private async Task<UserSettings> SiteSettingsAsync()
    {
        var owner = (await _users.GetFirstAdminAsync())?.Id ?? Guid.Empty;
        return await _settings.GetSettingsAsync(owner);
    }

    public Task<IReadOnlyList<string>> RequiredRolesAsync() =>
        _cache.GetOrCreateAsync<IReadOnlyList<string>>("2fa:roles", async e =>
        {
            e.AbsoluteExpirationRelativeToNow = PolicyCacheTtl;
            return (await SiteSettingsAsync()).TwoFactorRequiredRoles?.ToList() ?? new List<string>();
        })!;

    public async Task<string> IssuerAsync()
    {
        var name = (await SiteSettingsAsync()).SiteName;
        return string.IsNullOrWhiteSpace(name) ? "Inkwell" : name.Trim();
    }

    /// <summary>True when this user's role requires two-factor and they have not turned it on. Cached for a minute.</summary>
    public async Task<bool> MustEnrolAsync(Guid userId, string? role)
    {
        if (!TwoFactor.IsRequiredFor(role, await RequiredRolesAsync())) return false;
        return !await _cache.GetOrCreateAsync($"2fa:on:{userId}", async e =>
        {
            e.AbsoluteExpirationRelativeToNow = PolicyCacheTtl;
            return (await _users.GetTwoFactorAsync(userId))?.IsEnabled == true;
        });
    }

    public void Forget(Guid userId)
    {
        _cache.Remove($"2fa:on:{userId}");
        _cache.Remove("2fa:roles");
    }

    // ── Enrolment ───────────────────────────────────────────────────────────────

    public Task<TwoFactorEnrolment> StartEnrolmentAsync(string account)
    {
        var base32 = TwoFactor.ToBase32(TwoFactor.NewSecret());
        return BuildAsync(account, base32, _enrolments.Protect(base32, EnrolmentLifetime));
    }

    /// <summary>
    /// The same setup again (same secret, same QR code) after a wrong code, so the entry the user already
    /// added to their app keeps working. Null once the setup token has expired.
    /// </summary>
    public async Task<TwoFactorEnrolment?> ResumeEnrolmentAsync(string account, string token)
    {
        try { return await BuildAsync(account, _enrolments.Unprotect(token), token); }
        catch (CryptographicException) { return null; }
    }

    private async Task<TwoFactorEnrolment> BuildAsync(string account, string base32, string token)
    {
        var uri = TwoFactor.ProvisioningUri(await IssuerAsync(), account, base32);
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(uri, QRCodeGenerator.ECCLevel.M);
        // Black on white whatever the colour scheme: phone cameras read dark-on-light most reliably.
        var svg = new SvgQRCode(data).GetGraphic(4, "#000000", "#ffffff", drawQuietZones: true);
        return new TwoFactorEnrolment(token, TwoFactor.ForDisplay(base32), svg);
    }

    /// <summary>Confirms the app works (the code matches) and turns two-factor on. Returns the recovery codes, or null.</summary>
    public async Task<IReadOnlyList<string>?> CompleteEnrolmentAsync(Guid userId, string token, string? code)
    {
        string base32;
        try { base32 = _enrolments.Unprotect(token); }
        catch (CryptographicException) { return null; }
        var step = TwoFactor.Verify(TwoFactor.FromBase32(base32), code, DateTime.UtcNow, null);
        if (step is null) return null;

        var codes = TwoFactor.NewRecoveryCodes();
        await _users.EnableTwoFactorAsync(userId, _secrets.Protect(base32), codes.Select(TwoFactor.HashRecoveryCode).ToList(),
            step.Value, DateTime.UtcNow);
        Forget(userId);
        return codes;
    }

    public async Task DisableAsync(Guid userId)
    {
        await _users.DisableTwoFactorAsync(userId);
        Forget(userId);
    }

    /// <summary>New recovery codes, replacing the old ones, after a valid app code. Null when the code is wrong.</summary>
    public async Task<IReadOnlyList<string>?> RegenerateRecoveryCodesAsync(Guid userId, string? code)
    {
        if (await VerifyAsync(userId, code, allowRecoveryCode: false) != TwoFactorCheck.Ok) return null;
        var codes = TwoFactor.NewRecoveryCodes();
        await _users.SaveRecoveryHashesAsync(userId, codes.Select(TwoFactor.HashRecoveryCode).ToList());
        return codes;
    }

    // ── Verification ────────────────────────────────────────────────────────────

    /// <summary>
    /// Checks an app code (each 30-second code works once) or, when allowed, a single-use recovery code.
    /// <see cref="TwoFactorCheck.SecretUnreadable"/> means the encryption keys changed (a restore onto a
    /// new server): app codes cannot be checked, recovery codes still can.
    /// </summary>
    public async Task<TwoFactorCheck> VerifyAsync(Guid userId, string? code, bool allowRecoveryCode = true)
    {
        var tf = await _users.GetTwoFactorAsync(userId);
        if (tf is not { IsEnabled: true } || string.IsNullOrWhiteSpace(code)) return TwoFactorCheck.Invalid;

        if (allowRecoveryCode && TwoFactor.LooksLikeRecoveryCode(code))
        {
            var remaining = TwoFactor.UseRecoveryCode(tf.RecoveryHashes, code);
            if (remaining is null) return TwoFactorCheck.Invalid;
            await _users.SaveRecoveryHashesAsync(userId, remaining);
            return TwoFactorCheck.OkWithRecoveryCode;
        }

        string base32;
        try { base32 = _secrets.Unprotect(tf.SecretProtected!); }
        catch (CryptographicException) { return TwoFactorCheck.SecretUnreadable; }

        var step = TwoFactor.Verify(TwoFactor.FromBase32(base32), code, DateTime.UtcNow, tf.LastStep);
        if (step is null) return TwoFactorCheck.Invalid;
        return await _users.TryAdvanceTwoFactorStepAsync(userId, step.Value) ? TwoFactorCheck.Ok : TwoFactorCheck.Invalid;
    }

    public async Task<int> RecoveryCodesLeftAsync(Guid userId) =>
        (await _users.GetTwoFactorAsync(userId))?.RecoveryHashes.Count ?? 0;

    // ── The ticket between password and code ────────────────────────────────────

    public sealed record Ticket(Guid UserId, string? ReturnUrl, string Nonce);

    public string IssueTicket(Guid userId, string? returnUrl)
    {
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(12));
        return _tickets.Protect($"{userId:N}|{nonce}|{returnUrl}", TicketLifetime);
    }

    public Ticket? ReadTicket(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        try
        {
            var parts = _tickets.Unprotect(value).Split('|', 3);
            return parts.Length == 3 && Guid.TryParse(parts[0], out var id)
                ? new Ticket(id, string.IsNullOrEmpty(parts[2]) ? null : parts[2], parts[1]) : null;
        }
        catch (CryptographicException) { return null; }
    }

    /// <summary>Counts a wrong code against the ticket. False once the ticket has used up its attempts.</summary>
    public bool RecordFailedAttempt(Ticket ticket)
    {
        var key = "2fa:fail:" + ticket.Nonce;
        var n = _cache.GetOrCreate(key, e => { e.AbsoluteExpirationRelativeToNow = TicketLifetime; return 0; }) + 1;
        _cache.Set(key, n, TicketLifetime);
        return n < MaxAttemptsPerTicket;
    }

    public bool TicketExhausted(Ticket ticket) =>
        _cache.TryGetValue("2fa:fail:" + ticket.Nonce, out int n) && n >= MaxAttemptsPerTicket;
}
