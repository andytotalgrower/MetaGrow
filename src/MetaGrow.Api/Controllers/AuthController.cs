using System.Security.Claims;
using System.Text;
using ApiModels.MetaGrow;
using MetaGrow.Api.Auth;
using MetaGrow.Api.Data;
using MetaGrow.Api.Services;
using Metagen.Shared.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace MetaGrow.Api.Controllers;

[ApiController]
[Route("auth")]
[EnableRateLimiting("auth")]
public sealed class AuthController(
    UserManager<ApplicationUser> userManager,
    ApplicationDbContext db,
    ITokenService tokenService,
    ITgsApiService tgsApi,
    ISettingsService settings,
    IGraphMailService graphMail,
    MailQueue mailQueue,
    IConfiguration configuration,
    ILogger<AuthController> logger) : ControllerBase
{
    [HttpPost("register")]
    public async Task<ActionResult<MetaGrowRegisterResponse>> Register(MetaGrowRegisterRequest request)
    {
        var registration = await tgsApi.GetMetagenAppRegistration(request.RegistrationCode);
        if (registration is null || !registration.IsActive)
            return AuthBadRequest("Invalid or inactive registration code.");

        var appName = settings.GetSetting("AppName");
        if (!string.Equals(registration.AppName, appName, StringComparison.OrdinalIgnoreCase))
            return AuthBadRequest($"Registration code is not valid for {appName}.");
        if (registration.MaxUsage is not null && registration.Usage.GetValueOrDefault() >= registration.MaxUsage)
            return AuthBadRequest("Registration code has reached its usage limit.");

        var requestedRoles = (registration.Roles ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var roles = requestedRoles
            .Select(role => MetaGrowRoles.All.FirstOrDefault(allowed =>
                allowed.Equals(role, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        if (requestedRoles.Length == 0 || roles.Any(role => role is null))
            return AuthBadRequest("Registration code does not contain a valid MetaGrow role.");
        if (!graphMail.IsConfigured)
        {
            logger.LogError("Registration is unavailable because Graph mail is not configured.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new MetaGrowAuthError { Errors = ["Email service is not configured."] });
        }

        var email = request.Email.Trim();
        var user = new ApplicationUser { UserName = email, Email = email };
        var createResult = await userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded) return IdentityBadRequest(createResult);

        foreach (var role in roles.Cast<string>())
        {
            var roleResult = await userManager.AddToRoleAsync(user, role);
            if (!roleResult.Succeeded)
            {
                await userManager.DeleteAsync(user);
                return IdentityBadRequest(roleResult);
            }
        }

        db.UserEmailAddresses.Add(new UserEmailAddress
        {
            UserId = user.Id,
            Email = email,
            NormalizedEmail = userManager.NormalizeEmail(email)!,
            IsPrimary = true,
            CreatedUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        registration.Usage = registration.Usage.GetValueOrDefault() + 1;
        try
        {
            await tgsApi.UpdateMetagenAppRegistrationUsage(registration);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not record registration-code usage; rolling back user {Email}", email);
            await userManager.DeleteAsync(user);
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new MetaGrowAuthError { Errors = ["Registration could not be completed. Please try again shortly."] });
        }

        var code = Encode(await userManager.GenerateEmailConfirmationTokenAsync(user));
        var link = $"{WebBaseUrl}/Account/ConfirmEmail?userId={Uri.EscapeDataString(user.Id)}&code={code}";
        mailQueue.Enqueue(new OutgoingMail([email], "Confirm your MetaGrow account", $"""
            <p>Welcome to MetaGrow.</p>
            <p><a href="{link}">Confirm your email address</a> to activate your account.</p>
            <p>If you did not register, ignore this email.</p>
            """));
        logger.LogInformation("New user registered: {Email} from {Ip}", email, ClientIp);
        return new MetaGrowRegisterResponse
        {
            RequiresEmailConfirmation = true,
            Message = "Account created. Check your email for a confirmation link before logging in."
        };
    }

    [HttpPost("confirm-email")]
    public async Task<IActionResult> ConfirmEmail(MetaGrowConfirmEmailRequest request)
    {
        var user = await userManager.FindByIdAsync(request.UserId);
        if (user is null || !TryDecode(request.Code, out var code)) return AuthBadRequest("Invalid confirmation link.");
        var result = await userManager.ConfirmEmailAsync(user, code!);
        if (!result.Succeeded) return IdentityBadRequest(result);

        var primary = await db.UserEmailAddresses.SingleAsync(address => address.UserId == user.Id && address.IsPrimary);
        primary.IsConfirmed = true;
        primary.ConfirmedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("login")]
    public async Task<ActionResult<MetaGrowLoginResponse>> Login(MetaGrowLoginRequest request)
    {
        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null || !await userManager.CheckPasswordAsync(user, request.Password))
        {
            if (user is not null) await userManager.AccessFailedAsync(user);
            return Unauthorized(Error("Invalid email or password."));
        }
        if (await userManager.IsLockedOutAsync(user)) return Unauthorized(Error("Account is locked out. Try again later."));
        if (!user.EmailConfirmed) return Unauthorized(Error("Please confirm your email address first."));
        await userManager.ResetAccessFailedCountAsync(user);

        if (configuration.GetValue("Mfa:Required", true))
        {
            if (!user.TwoFactorEnabled)
                return new MetaGrowLoginResponse
                {
                    Status = MetaGrowLoginStatus.MfaSetupRequired,
                    ChallengeToken = tokenService.CreateScopedToken(user, MfaScopes.Setup, TimeSpan.FromMinutes(10))
                };

            var stamp = await userManager.GetSecurityStampAsync(user);
            var trusted = string.IsNullOrWhiteSpace(request.DeviceToken)
                ? null : tokenService.ValidateScopedToken(request.DeviceToken, MfaScopes.Device);
            if (trusted is null || trusted.Value.UserId != user.Id || trusted.Value.SecurityStamp != stamp)
                return new MetaGrowLoginResponse
                {
                    Status = MetaGrowLoginStatus.MfaCodeRequired,
                    ChallengeToken = tokenService.CreateScopedToken(user, MfaScopes.Login, TimeSpan.FromMinutes(10))
                };
        }

        return new MetaGrowLoginResponse { Status = MetaGrowLoginStatus.Ok, Auth = await BuildAuthResponse(user) };
    }

    [HttpPost("mfa/setup-info")]
    public async Task<ActionResult<MetaGrowMfaSetupInfo>> MfaSetupInfo(MetaGrowMfaChallengeRequest request)
    {
        var user = await UserFromChallenge(request.ChallengeToken, MfaScopes.Setup);
        return user is null ? Unauthorized(Error("Your setup session has expired.")) : await BuildSetupInfo(user);
    }

    [HttpPost("mfa/setup")]
    public async Task<ActionResult<MetaGrowMfaSetupResponse>> MfaSetup(MetaGrowMfaSetupRequest request)
    {
        var user = await UserFromChallenge(request.ChallengeToken, MfaScopes.Setup);
        if (user is null) return Unauthorized(Error("Your setup session has expired."));
        if (!await VerifyAuthenticator(user, request.Code)) return AuthBadRequest("Verification code is invalid.");

        await userManager.SetTwoFactorEnabledAsync(user, true);
        var recoveryCodes = await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
        SendMfaMail(user);
        return new MetaGrowMfaSetupResponse
        {
            RecoveryCodes = recoveryCodes!.ToArray(),
            Auth = await BuildAuthResponse(user)
        };
    }

    [HttpPost("mfa/verify")]
    public async Task<ActionResult<MetaGrowAuthResponse>> MfaVerify(MetaGrowMfaVerifyRequest request)
    {
        var user = await UserFromChallenge(request.ChallengeToken, MfaScopes.Login);
        if (user is null) return Unauthorized(Error("Your login session has expired."));
        var valid = request.IsRecoveryCode
            ? (await userManager.RedeemTwoFactorRecoveryCodeAsync(user, request.Code.Trim())).Succeeded
            : await VerifyAuthenticator(user, request.Code);
        if (!valid)
        {
            await userManager.AccessFailedAsync(user);
            return Unauthorized(Error(request.IsRecoveryCode ? "Invalid recovery code." : "Invalid authenticator code."));
        }

        await userManager.ResetAccessFailedCountAsync(user);
        var response = await BuildAuthResponse(user);
        if (request.RememberMachine)
            response.DeviceToken = tokenService.CreateScopedToken(user, MfaScopes.Device,
                TimeSpan.FromDays(30), await userManager.GetSecurityStampAsync(user));
        return response;
    }

    [HttpGet("mfa/status"), Authorize, DisableRateLimiting]
    public async Task<ActionResult<MetaGrowMfaStatusResponse>> MfaStatus()
    {
        var user = await CurrentUser();
        if (user is null) return Unauthorized();
        return new MetaGrowMfaStatusResponse
        {
            TwoFactorEnabled = await userManager.GetTwoFactorEnabledAsync(user),
            HasAuthenticator = await userManager.GetAuthenticatorKeyAsync(user) is not null,
            RecoveryCodesLeft = await userManager.CountRecoveryCodesAsync(user)
        };
    }

    [HttpPost("mfa/recovery-codes"), Authorize]
    public async Task<ActionResult<MetaGrowRecoveryCodesResponse>> NewRecoveryCodes()
    {
        var user = await CurrentUser();
        if (user is null) return Unauthorized();
        if (!await userManager.GetTwoFactorEnabledAsync(user)) return AuthBadRequest("Two-factor authentication is not enabled.");
        return new MetaGrowRecoveryCodesResponse
        {
            RecoveryCodes = (await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10))!.ToArray()
        };
    }

    [HttpPost("mfa/manage/setup-info"), Authorize]
    public async Task<ActionResult<MetaGrowMfaSetupInfo>> ManageMfaSetupInfo()
    {
        var user = await CurrentUser();
        return user is null ? Unauthorized() : await BuildSetupInfo(user);
    }

    [HttpPost("mfa/manage/setup"), Authorize]
    public async Task<ActionResult<MetaGrowRecoveryCodesResponse>> ManageMfaSetup(MetaGrowMfaManageSetupRequest request)
    {
        var user = await CurrentUser();
        if (user is null) return Unauthorized();
        if (!await VerifyAuthenticator(user, request.Code)) return AuthBadRequest("Verification code is invalid.");
        await userManager.SetTwoFactorEnabledAsync(user, true);
        var recoveryCodes = await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
        SendMfaMail(user);
        return new MetaGrowRecoveryCodesResponse { RecoveryCodes = recoveryCodes!.ToArray() };
    }

    [HttpPost("mfa/disable"), Authorize]
    public async Task<IActionResult> DisableMfa()
    {
        var user = await CurrentUser();
        if (user is null) return Unauthorized();
        await userManager.SetTwoFactorEnabledAsync(user, false);
        return NoContent();
    }

    [HttpPost("mfa/reset-authenticator"), Authorize]
    public async Task<IActionResult> ResetOwnAuthenticator()
    {
        var user = await CurrentUser();
        if (user is null) return Unauthorized();
        await userManager.SetTwoFactorEnabledAsync(user, false);
        await userManager.ResetAuthenticatorKeyAsync(user);
        await userManager.UpdateSecurityStampAsync(user);
        return NoContent();
    }

    [HttpPost("mfa/reset/{userId}"), Authorize(Roles = MetaGrowRoles.Admin)]
    public async Task<IActionResult> ResetMfa(string userId)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null) return NotFound();
        await userManager.SetTwoFactorEnabledAsync(user, false);
        await userManager.ResetAuthenticatorKeyAsync(user);
        await userManager.UpdateSecurityStampAsync(user);
        return NoContent();
    }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(MetaGrowForgotPasswordRequest request)
    {
        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null || !user.EmailConfirmed || !graphMail.IsConfigured) return NoContent();
        var code = Encode(await userManager.GeneratePasswordResetTokenAsync(user));
        var link = $"{WebBaseUrl}/Account/ResetPassword?email={Uri.EscapeDataString(user.Email!)}&code={code}";
        mailQueue.Enqueue(new OutgoingMail([user.Email!], "Reset your MetaGrow password",
            $"<p><a href=\"{link}\">Choose a new password</a> for your MetaGrow account.</p>"));
        return NoContent();
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(MetaGrowResetPasswordRequest request)
    {
        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null || !TryDecode(request.Code, out var code)) return AuthBadRequest("The reset link is invalid or expired.");
        var result = await userManager.ResetPasswordAsync(user, code!, request.Password);
        return result.Succeeded ? NoContent() : IdentityBadRequest(result);
    }

    [HttpGet("me"), Authorize, DisableRateLimiting]
    public async Task<ActionResult<MetaGrowUserDto>> Me()
    {
        var user = await CurrentUser();
        return user is null ? Unauthorized() : await BuildUserDto(user);
    }

    [HttpGet("emails"), Authorize]
    public async Task<ActionResult<MetaGrowUserEmailDto[]>> Emails()
    {
        var user = await CurrentUser();
        return user is null ? Unauthorized() : await BuildEmailDtos(user.Id);
    }

    [HttpPost("emails"), Authorize]
    public async Task<ActionResult<MetaGrowUserEmailDto>> AddEmail(MetaGrowAddEmailRequest request)
    {
        var user = await CurrentUser();
        if (user is null) return Unauthorized();
        var email = request.Email.Trim();
        var normalized = userManager.NormalizeEmail(email)!;
        if (await db.UserEmailAddresses.AnyAsync(address => address.NormalizedEmail == normalized))
            return AuthBadRequest("That email address is already registered.");

        var address = new UserEmailAddress
        {
            UserId = user.Id, Email = email, NormalizedEmail = normalized, CreatedUtc = DateTime.UtcNow
        };
        db.UserEmailAddresses.Add(address);
        await db.SaveChangesAsync();

        var token = await userManager.GenerateUserTokenAsync(user, TokenOptions.DefaultEmailProvider,
            $"MetaGrowAdditionalEmail:{address.Id}:{normalized}");
        var code = Encode(token);
        var link = $"{WebBaseUrl}/Account/ConfirmAdditionalEmail?emailAddressId={address.Id}&code={code}";
        mailQueue.Enqueue(new OutgoingMail([email], "Confirm your email for MetaGrow",
            $"<p><a href=\"{link}\">Confirm this additional email address</a>.</p>"));
        return ToDto(address);
    }

    [HttpPost("emails/confirm"), Authorize]
    public async Task<IActionResult> ConfirmAdditionalEmail(MetaGrowConfirmAdditionalEmailRequest request)
    {
        var user = await CurrentUser();
        if (user is null) return Unauthorized();
        var address = await db.UserEmailAddresses.SingleOrDefaultAsync(item =>
            item.Id == request.EmailAddressId && item.UserId == user.Id && !item.IsPrimary);
        if (address is null || !TryDecode(request.Code, out var code)) return AuthBadRequest("Invalid confirmation link.");
        var valid = await userManager.VerifyUserTokenAsync(user, TokenOptions.DefaultEmailProvider,
            $"MetaGrowAdditionalEmail:{address.Id}:{address.NormalizedEmail}", code!);
        if (!valid) return AuthBadRequest("Invalid or expired confirmation link.");
        address.IsConfirmed = true;
        address.ConfirmedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("refresh")]
    public async Task<ActionResult<MetaGrowAuthResponse>> Refresh(MetaGrowRefreshRequest request)
    {
        var rotated = await tokenService.RotateRefreshTokenAsync(request.RefreshToken, ClientIp);
        if (rotated is null) return Unauthorized(Error("Invalid or expired refresh token."));
        var (user, refreshToken) = rotated.Value;
        var (accessToken, expiresUtc) = await tokenService.CreateAccessTokenAsync(user);
        return new MetaGrowAuthResponse
        {
            AccessToken = accessToken, AccessTokenExpiresUtc = expiresUtc,
            RefreshToken = refreshToken, User = await BuildUserDto(user)
        };
    }

    [HttpPost("revoke")]
    public async Task<IActionResult> Revoke(MetaGrowRevokeRequest request)
    {
        await tokenService.RevokeRefreshTokenAsync(request.RefreshToken);
        return NoContent();
    }

    private async Task<MetaGrowMfaSetupInfo> BuildSetupInfo(ApplicationUser user)
    {
        var key = await userManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrWhiteSpace(key))
        {
            await userManager.ResetAuthenticatorKeyAsync(user);
            key = await userManager.GetAuthenticatorKeyAsync(user);
        }
        var issuer = configuration["Mfa:Issuer"] ?? "MetaGrow";
        return new MetaGrowMfaSetupInfo
        {
            SharedKey = string.Join(' ', key!.Chunk(4).Select(chars => new string(chars))).ToLowerInvariant(),
            AuthenticatorUri = $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(user.Email!)}?secret={key}&issuer={Uri.EscapeDataString(issuer)}&digits=6"
        };
    }

    private async Task<bool> VerifyAuthenticator(ApplicationUser user, string code) =>
        await userManager.VerifyTwoFactorTokenAsync(user, userManager.Options.Tokens.AuthenticatorTokenProvider,
            code.Replace(" ", string.Empty).Replace("-", string.Empty));

    private async Task<ApplicationUser?> UserFromChallenge(string token, string scope)
    {
        var validated = tokenService.ValidateScopedToken(token, scope);
        return validated is null ? null : await userManager.FindByIdAsync(validated.Value.UserId);
    }

    private async Task<ApplicationUser?> CurrentUser()
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return id is null ? null : await userManager.FindByIdAsync(id);
    }

    private async Task<MetaGrowAuthResponse> BuildAuthResponse(ApplicationUser user)
    {
        var (accessToken, expiresUtc) = await tokenService.CreateAccessTokenAsync(user);
        return new MetaGrowAuthResponse
        {
            AccessToken = accessToken, AccessTokenExpiresUtc = expiresUtc,
            RefreshToken = await tokenService.IssueRefreshTokenAsync(user, ClientIp),
            User = await BuildUserDto(user)
        };
    }

    private async Task<MetaGrowUserDto> BuildUserDto(ApplicationUser user) => new()
    {
        Id = user.Id,
        Email = user.Email ?? string.Empty,
        Roles = (await userManager.GetRolesAsync(user)).ToArray(),
        EmailAddresses = await BuildEmailDtos(user.Id)
    };

    private async Task<MetaGrowUserEmailDto[]> BuildEmailDtos(string userId) =>
        (await db.UserEmailAddresses.Where(address => address.UserId == userId)
            .OrderByDescending(address => address.IsPrimary).ThenBy(address => address.Email).ToListAsync())
        .Select(ToDto).ToArray();

    private static MetaGrowUserEmailDto ToDto(UserEmailAddress address) => new()
    {
        Id = address.Id, Email = address.Email, IsPrimary = address.IsPrimary, IsConfirmed = address.IsConfirmed
    };

    private void SendMfaMail(ApplicationUser user) => mailQueue.Enqueue(new OutgoingMail([user.Email!],
        "An authenticator was registered on your MetaGrow account",
        $"<p>An authenticator was registered on your MetaGrow account from IP {ClientIp}.</p>"));

    /// <summary>
    /// Base URL for links embedded in emails. Prefers the X-Web-Base-Url header
    /// forwarded by the Web app (reflects the public host the user is browsing),
    /// falling back to the WebBaseUrl config value.
    /// </summary>
    private string WebBaseUrl
    {
        get
        {
            var forwarded = Request.Headers["X-Web-Base-Url"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(forwarded) &&
                Uri.TryCreate(forwarded, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
            {
                return forwarded.TrimEnd('/');
            }
            return (configuration["WebBaseUrl"] ?? "").TrimEnd('/');
        }
    }
    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();
    private BadRequestObjectResult AuthBadRequest(string message) => BadRequest(Error(message));
    private BadRequestObjectResult IdentityBadRequest(IdentityResult result) =>
        BadRequest(new MetaGrowAuthError { Errors = result.Errors.Select(error => error.Description).ToArray() });
    private static MetaGrowAuthError Error(string message) => new() { Errors = [message] };
    private static string Encode(string value) => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(value));
    private static bool TryDecode(string value, out string? decoded)
    {
        try { decoded = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(value)); return true; }
        catch (FormatException) { decoded = null; return false; }
    }
}
