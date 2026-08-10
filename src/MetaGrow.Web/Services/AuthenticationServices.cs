using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using ApiModels.MetaGrow;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;

namespace MetaGrow.Web.Services;

public static class AuthConstants { public const string SessionClaim = "mg_session"; }
public sealed record TokenEntry(string AccessToken, DateTime AccessTokenExpiresUtc, string RefreshToken);

public sealed class ServerTokenStore(IDistributedCache cache, IDataProtectionProvider dataProtection)
{
    private static readonly DistributedCacheEntryOptions CacheOptions = new() { SlidingExpiration = TimeSpan.FromDays(14) };
    private readonly IDataProtector protector = dataProtection.CreateProtector("MetaGrow.Web.ServerTokenStore");
    private readonly ConcurrentDictionary<string, TokenEntry> entries = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> locks = new();
    private static string Key(string id) => $"mg_token:{id}";

    public async Task<TokenEntry?> GetAsync(string id)
    {
        if (entries.TryGetValue(id, out var entry)) return entry;
        var value = await cache.GetStringAsync(Key(id));
        if (value is null) return null;
        try { entry = JsonSerializer.Deserialize<TokenEntry>(protector.Unprotect(value)); }
        catch { await cache.RemoveAsync(Key(id)); return null; }
        if (entry is not null) entries[id] = entry;
        return entry;
    }
    public async Task SetAsync(string id, TokenEntry entry)
    {
        entries[id] = entry;
        await cache.SetStringAsync(Key(id), protector.Protect(JsonSerializer.Serialize(entry)), CacheOptions);
    }
    public async Task RemoveAsync(string id)
    {
        entries.TryRemove(id, out _);
        if (locks.TryRemove(id, out var gate)) gate.Dispose();
        await cache.RemoveAsync(Key(id));
    }
    public SemaphoreSlim GetLock(string id) => locks.GetOrAdd(id, _ => new(1, 1));
}

public sealed class AuthApiClient(IHttpClientFactory clients, ILogger<AuthApiClient> logger)
{
    public const string HttpClientName = "MetaGrowApi";
    private HttpClient Client => clients.CreateClient(HttpClientName);
    public Task<(MetaGrowLoginResponse?, string[])> LoginAsync(MetaGrowLoginRequest value) => Post<MetaGrowLoginRequest, MetaGrowLoginResponse>("auth/login", value);
    public Task<(MetaGrowRegisterResponse?, string[])> RegisterAsync(MetaGrowRegisterRequest value) => Post<MetaGrowRegisterRequest, MetaGrowRegisterResponse>("auth/register", value);
    public Task<(MetaGrowMfaSetupInfo?, string[])> MfaSetupInfoAsync(string token) => Post<MetaGrowMfaChallengeRequest, MetaGrowMfaSetupInfo>("auth/mfa/setup-info", new() { ChallengeToken = token });
    public Task<(MetaGrowMfaSetupResponse?, string[])> MfaSetupAsync(string token, string code) => Post<MetaGrowMfaSetupRequest, MetaGrowMfaSetupResponse>("auth/mfa/setup", new() { ChallengeToken = token, Code = code });
    public Task<(MetaGrowAuthResponse?, string[])> MfaVerifyAsync(MetaGrowMfaVerifyRequest value) => Post<MetaGrowMfaVerifyRequest, MetaGrowAuthResponse>("auth/mfa/verify", value);
    public Task<(MetaGrowAuthResponse?, string[])> RefreshAsync(string token) => Post<MetaGrowRefreshRequest, MetaGrowAuthResponse>("auth/refresh", new() { RefreshToken = token });
    public Task<string[]> ConfirmEmailAsync(string userId, string code) => PostOnly("auth/confirm-email", new MetaGrowConfirmEmailRequest { UserId = userId, Code = code });
    public Task<string[]> ForgotPasswordAsync(string email) => PostOnly("auth/forgot-password", new MetaGrowForgotPasswordRequest { Email = email });
    public Task<string[]> ResetPasswordAsync(MetaGrowResetPasswordRequest value) => PostOnly("auth/reset-password", value);
    public async Task RevokeAsync(string token)
    {
        try { await Client.PostAsJsonAsync("auth/revoke", new MetaGrowRevokeRequest { RefreshToken = token }); }
        catch (Exception exception) { logger.LogWarning(exception, "Failed to revoke a MetaGrow refresh token."); }
    }
    private async Task<(TResponse?, string[])> Post<TRequest, TResponse>(string path, TRequest value) where TResponse : class
    {
        try
        {
            var response = await Client.PostAsJsonAsync(path, value);
            return response.IsSuccessStatusCode
                ? (await response.Content.ReadFromJsonAsync<TResponse>(), [])
                : (null, await Errors(response));
        }
        catch (HttpRequestException exception)
        {
            logger.LogError(exception, "Could not reach MetaGrow.Api at {Address}", Client.BaseAddress);
            return (null, ["MetaGrow.Api could not be reached. Please try again shortly."]);
        }
    }
    private async Task<string[]> PostOnly<T>(string path, T value)
    {
        try
        {
            var response = await Client.PostAsJsonAsync(path, value);
            return response.IsSuccessStatusCode ? [] : await Errors(response);
        }
        catch (HttpRequestException exception)
        {
            logger.LogError(exception, "Could not reach MetaGrow.Api at {Address}", Client.BaseAddress);
            return ["MetaGrow.Api could not be reached. Please try again shortly."];
        }
    }
    private static async Task<string[]> Errors(HttpResponseMessage response)
    {
        try
        {
            var error = await response.Content.ReadFromJsonAsync<MetaGrowAuthError>();
            if (error is { Errors.Length: > 0 }) return error.Errors;
        }
        catch { }
        return [$"Request failed ({(int)response.StatusCode})."];
    }
}

public sealed class ApiTokenService(ServerTokenStore tokens, AuthApiClient auth)
{
    public async Task<string?> GetAccessTokenAsync(ClaimsPrincipal principal)
    {
        var id = principal.FindFirst(AuthConstants.SessionClaim)?.Value;
        if (id is null) return null;
        var entry = await tokens.GetAsync(id);
        if (entry is null) return null;
        if (entry.AccessTokenExpiresUtc - DateTime.UtcNow > TimeSpan.FromSeconds(60)) return entry.AccessToken;
        var gate = tokens.GetLock(id);
        await gate.WaitAsync();
        try
        {
            entry = await tokens.GetAsync(id);
            if (entry is null) return null;
            if (entry.AccessTokenExpiresUtc - DateTime.UtcNow > TimeSpan.FromSeconds(60)) return entry.AccessToken;
            var (fresh, _) = await auth.RefreshAsync(entry.RefreshToken);
            if (fresh is null) { await tokens.RemoveAsync(id); return null; }
            await tokens.SetAsync(id, new(fresh.AccessToken, fresh.AccessTokenExpiresUtc, fresh.RefreshToken));
            return fresh.AccessToken;
        }
        finally { gate.Release(); }
    }
}

public sealed class AccountApiClient(
    IHttpClientFactory clients,
    IHttpContextAccessor contextAccessor,
    ApiTokenService tokens)
{
    private HttpClient Client => clients.CreateClient(AuthApiClient.HttpClientName);

    public Task<(MetaGrowUserEmailDto[]?, string?)> GetEmailsAsync() =>
        Send<MetaGrowUserEmailDto[]>(HttpMethod.Get, "auth/emails");
    public Task<(MetaGrowUserEmailDto?, string?)> AddEmailAsync(string email) =>
        Send<MetaGrowUserEmailDto>(HttpMethod.Post, "auth/emails", new MetaGrowAddEmailRequest { Email = email });
    public Task<string?> ConfirmEmailAsync(long id, string code) =>
        SendWithoutResult(HttpMethod.Post, "auth/emails/confirm", new MetaGrowConfirmAdditionalEmailRequest { EmailAddressId = id, Code = code });
    public Task<(MetaGrowMfaStatusResponse?, string?)> GetMfaStatusAsync() =>
        Send<MetaGrowMfaStatusResponse>(HttpMethod.Get, "auth/mfa/status");
    public Task<(MetaGrowRecoveryCodesResponse?, string?)> NewRecoveryCodesAsync() =>
        Send<MetaGrowRecoveryCodesResponse>(HttpMethod.Post, "auth/mfa/recovery-codes", new { });
    public Task<string?> DisableMfaAsync() => SendWithoutResult(HttpMethod.Post, "auth/mfa/disable", new { });
    public Task<string?> ResetAuthenticatorAsync() => SendWithoutResult(HttpMethod.Post, "auth/mfa/reset-authenticator", new { });

    private async Task<(T?, string?)> Send<T>(HttpMethod method, string path, object? body = null)
    {
        var request = await CreateRequest(method, path, body);
        if (request is null) return (default, "Your session has expired. Please log in again.");
        var response = await Client.SendAsync(request);
        if (!response.IsSuccessStatusCode) return (default, await ReadError(response));
        return (await response.Content.ReadFromJsonAsync<T>(), null);
    }

    private async Task<string?> SendWithoutResult(HttpMethod method, string path, object body)
    {
        var request = await CreateRequest(method, path, body);
        if (request is null) return "Your session has expired. Please log in again.";
        var response = await Client.SendAsync(request);
        return response.IsSuccessStatusCode ? null : await ReadError(response);
    }

    private async Task<HttpRequestMessage?> CreateRequest(HttpMethod method, string path, object? body)
    {
        var principal = contextAccessor.HttpContext?.User;
        if (principal is null) return null;
        var token = await tokens.GetAccessTokenAsync(principal);
        if (token is null) return null;
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return request;
    }

    private static async Task<string> ReadError(HttpResponseMessage response)
    {
        try
        {
            var error = await response.Content.ReadFromJsonAsync<MetaGrowAuthError>();
            if (error is { Errors.Length: > 0 }) return string.Join(" ", error.Errors);
        }
        catch { }
        return $"Request failed ({(int)response.StatusCode}).";
    }
}

public static class AuthCookieHelper
{
    public static async Task SignInAsync(HttpContext context, ServerTokenStore tokens, MetaGrowAuthResponse auth, bool persistent)
    {
        var id = Guid.NewGuid().ToString("N");
        await tokens.SetAsync(id, new(auth.AccessToken, auth.AccessTokenExpiresUtc, auth.RefreshToken));
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, auth.User.Id), new(ClaimTypes.Name, auth.User.Email), new(ClaimTypes.Email, auth.User.Email), new(AuthConstants.SessionClaim, id) };
        claims.AddRange(auth.User.Roles.Select(role => new Claim(ClaimTypes.Role, role)));
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme, ClaimTypes.Name, ClaimTypes.Role);
        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity), new AuthenticationProperties { IsPersistent = persistent });
    }
}

public sealed record MfaPending(string ChallengeToken, bool RememberMe, DateTime ExpiresUtc);
public sealed class MfaFlowState(IDataProtectionProvider dataProtection)
{
    private const string PendingCookie = "mg_mfa";
    private const string DeviceCookie = "mg_device";
    private readonly IDataProtector protector = dataProtection.CreateProtector("MetaGrow.Web.MfaPending");
    public void WritePending(HttpContext context, string token, bool remember) => context.Response.Cookies.Append(PendingCookie,
        protector.Protect(JsonSerializer.Serialize(new MfaPending(token, remember, DateTime.UtcNow.AddMinutes(10)))), Options(TimeSpan.FromMinutes(10)));
    public MfaPending? ReadPending(HttpContext context)
    {
        if (!context.Request.Cookies.TryGetValue(PendingCookie, out var value)) return null;
        try { var result = JsonSerializer.Deserialize<MfaPending>(protector.Unprotect(value)); return result?.ExpiresUtc > DateTime.UtcNow ? result : null; }
        catch { return null; }
    }
    public void ClearPending(HttpContext context) => context.Response.Cookies.Delete(PendingCookie);
    public string? ReadDeviceToken(HttpContext context) => context.Request.Cookies.TryGetValue(DeviceCookie, out var value) ? value : null;
    public void WriteDeviceToken(HttpContext context, string value) => context.Response.Cookies.Append(DeviceCookie, value, Options(TimeSpan.FromDays(30)));
    public void ClearDeviceToken(HttpContext context) => context.Response.Cookies.Delete(DeviceCookie);
    private static CookieOptions Options(TimeSpan age) => new() { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, MaxAge = age, IsEssential = true };
}

public sealed class ClientIpForwardingHandler(IHttpContextAccessor accessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var httpContext = accessor.HttpContext;
        var ip = httpContext?.Connection.RemoteIpAddress?.ToString();
        if (!string.IsNullOrWhiteSpace(ip)) request.Headers.TryAddWithoutValidation("X-Forwarded-For", ip);

        if (httpContext is not null && httpContext.Request.Host.HasValue)
        {
            var webBaseUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
            request.Headers.TryAddWithoutValidation("X-Web-Base-Url", webBaseUrl);
        }

        return base.SendAsync(request, cancellationToken);
    }
}

public static class AccountEndpoints
{
    public static IEndpointConventionBuilder MapAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/Account");
        group.MapGet("/Logout", Logout);
        group.MapPost("/Logout", Logout);
        return group;
    }
    private static async Task<IResult> Logout(HttpContext context, ServerTokenStore tokens, AuthApiClient auth, [FromForm] string? returnUrl = null)
    {
        var id = context.User.FindFirst(AuthConstants.SessionClaim)?.Value;
        if (id is not null)
        {
            var entry = await tokens.GetAsync(id);
            if (entry is not null) await auth.RevokeAsync(entry.RefreshToken);
            await tokens.RemoveAsync(id);
        }
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return TypedResults.LocalRedirect($"~/{returnUrl}");
    }
}
