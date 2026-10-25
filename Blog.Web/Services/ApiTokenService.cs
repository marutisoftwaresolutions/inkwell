using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Blog.Web.Services;

/// <summary>
/// API sign-in (ADR-0033). Issues short-lived bearer tokens after the same checks as the Desk sign-in, and turns a
/// validated token back into a caller: the user is re-read on every request, so a disabled or deleted account, a
/// changed role or a withdrawn permission takes effect at once instead of when the token expires. The caller's
/// claims match the Desk cookie's, so the API is authorised with the Desk's own policies.
/// </summary>
public sealed class ApiTokenService
{
    private readonly IConfiguration _config;
    public ApiTokenService(IConfiguration config) => _config = config;

    /// <summary>Null when tokens can be issued; otherwise why not (see <see cref="ApiSigningKey"/>).</summary>
    public string? Problem => ConfigProblem(_config);

    /// <summary>What stops the API from signing anyone in: an unusable key, or no issuer or audience to check tokens against.</summary>
    public static string? ConfigProblem(IConfiguration config) =>
        ApiSigningKey.Problem(config["Jwt:Key"])
        ?? (string.IsNullOrWhiteSpace(config["Jwt:Issuer"]) || string.IsNullOrWhiteSpace(config["Jwt:Audience"])
            ? "Jwt:Issuer and Jwt:Audience are not both set, so the API cannot check tokens and is switched off. Set both, for example to your site's domain."
            : null);

    public int LifetimeMinutes => Math.Clamp(int.TryParse(_config["Jwt:AccessTokenExpiryMinutes"], out var m) ? m : 15, 1, 60);

    public string Issue(User user)
    {
        if (Problem is { } problem) throw new InvalidOperationException(problem);
        var creds = new SigningCredentials(SigningKey(_config)!, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: [new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()), new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())],
            expires: DateTime.UtcNow.AddMinutes(LifetimeMinutes),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>The signing key, or null when <c>Jwt:Key</c> is unusable: then no token validates (fail closed).</summary>
    public static SymmetricSecurityKey? SigningKey(IConfiguration config) =>
        ApiSigningKey.IsUsable(config["Jwt:Key"]) ? new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!)) : null;

    /// <summary>
    /// The bearer options. Inbound claim mapping is off so <c>sub</c> stays <c>sub</c>; issuer, audience, lifetime
    /// and signature are all checked; the caller is rebuilt from the database in <c>OnTokenValidated</c>.
    /// </summary>
    public static void Configure(JwtBearerOptions o, IConfiguration config)
    {
        var key = SigningKey(config);
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = config["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = config["Jwt:Audience"],
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            ValidateIssuerSigningKey = true,
            RequireSignedTokens = true,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            // No usable key: resolve to nothing, so every token is refused with a 401.
            IssuerSigningKeyResolver = (_, _, _, _) => key is null ? Array.Empty<SecurityKey>() : new SecurityKey[] { key },
        };
        o.Events = new JwtBearerEvents
        {
            OnTokenValidated = async ctx =>
            {
                var principal = await CallerAsync(ctx.HttpContext.RequestServices, ctx.Principal);
                if (principal is null) ctx.Fail("The account behind this token is disabled or no longer exists.");
                else ctx.Principal = principal;
            },
            // An API client gets JSON, not the site's HTML error page (which the status-code pages would show
            // for a 401 or 403 that has no body).
            OnChallenge = async ctx =>
            {
                ctx.HandleResponse();
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                ctx.Response.Headers.WWWAuthenticate = "Bearer";
                await ctx.Response.WriteAsJsonAsync(new
                {
                    statusCode = 401,
                    error = ctx.AuthenticateFailure is SecurityTokenExpiredException
                        ? "The token has expired. Sign in again with POST /api/v1/auth/login."
                        : "Sign in with POST /api/v1/auth/login and send the token as \"Authorization: Bearer <token>\".",
                });
            },
            OnForbidden = ctx =>
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                return ctx.Response.WriteAsJsonAsync(new { statusCode = 403, error = "Your account does not have the permission this needs." });
            },
        };
    }

    /// <summary>The caller as the Desk would see them, or null when the account cannot sign in any more.</summary>
    public static async Task<ClaimsPrincipal?> CallerAsync(IServiceProvider services, ClaimsPrincipal? token)
    {
        if (!Guid.TryParse(token?.FindFirstValue(JwtRegisteredClaimNames.Sub), out var userId)) return null;
        var user = await services.GetRequiredService<IUserRepository>().GetByIdAsync(userId);
        if (user is null || !user.IsActive) return null;

        var claims = UserSignInService.Claims(user, await services.GetRequiredService<IRoleRepository>().GetPermissionNamesForUserAsync(user.Id));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, JwtBearerDefaults.AuthenticationScheme));
    }
}
