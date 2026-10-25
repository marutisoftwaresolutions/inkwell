using Blog.Core.Domain;
using Blog.Core.Interfaces;
using Blog.Core.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Blog.Web.Controllers.Api;

[ApiController]
[Route("api/v1")]
[Produces("application/json")]
public class AuthApiController : ControllerBase
{
    private readonly AuthService _auth;
    private readonly IUserRepository _users;
    private readonly Blog.Web.Services.ApiTokenService _tokens;
    private readonly Blog.Web.Services.TwoFactorService _twoFactor;
    private readonly Blog.Web.Services.Security.IpFirewallService _firewall;

    public AuthApiController(AuthService auth, IUserRepository users, Blog.Web.Services.ApiTokenService tokens,
        Blog.Web.Services.TwoFactorService twoFactor, Blog.Web.Services.Security.IpFirewallService firewall)
    {
        _auth = auth;
        _users = users;
        _tokens = tokens;
        _twoFactor = twoFactor;
        _firewall = firewall;
    }

    [HttpPost("auth/login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        // No usable signing key: the API is off (ADR-0033). Said before the password is checked, so it reveals nothing.
        if (_tokens.Problem is not null)
            return StatusCode(503, ApiError(503, "The API is not configured on this site. The site administrator needs to set Jwt:Key."));

        var (user, error) = await _auth.ValidateLoginAsync(req.Email, req.Password);
        if (user == null)
        {
            await _firewall.RegisterFailedLoginAsync(HttpContext);
            return Unauthorized(ApiError(401, error ?? "Invalid credentials."));
        }

        // Two-factor (ADR-0021): a token is a sign-in too, so it needs the same second step as the Desk.
        // App codes only; recovery codes are for getting back into the Desk after losing a phone.
        if ((await _users.GetTwoFactorAsync(user.Id))?.IsEnabled == true)
        {
            if (string.IsNullOrWhiteSpace(req.Code))
                return Unauthorized(ApiError(401, "Two-factor code required. Send the 6-digit code from your authenticator app as \"code\"."));
            if (await _twoFactor.VerifyAsync(user.Id, req.Code, allowRecoveryCode: false) != Blog.Web.Services.TwoFactorCheck.Ok)
            {
                await _firewall.RegisterFailedLoginAsync(HttpContext);
                return Unauthorized(ApiError(401, "Invalid two-factor code."));
            }
        }
        else if (await _twoFactor.MustEnrolAsync(user.Id, user.Role))
        {
            return StatusCode(403, ApiError(403, "Two-factor sign-in is required for your role. Set it up in the Desk first."));
        }

        var accessToken = _tokens.Issue(user);

        return Ok(ApiOk(new { accessToken, expiresIn = _tokens.LifetimeMinutes * 60, tokenType = "Bearer" }));
    }

    [HttpPost("auth/logout")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public IActionResult Logout()
    {
        // Tokens are not stored, so one cannot be withdrawn early: the client discards it and it lapses at its expiry.
        return Ok(ApiOk("Discard the token: it stops working when it expires. Disabling the account ends it at once."));
    }


    private static object ApiOk(object data) => new { data };
    private static object ApiError(int code, string error) =>
        new { statusCode = code, error, traceId = System.Diagnostics.Activity.Current?.Id };
}

public record LoginRequest(string Email, string Password, string? Code = null);
