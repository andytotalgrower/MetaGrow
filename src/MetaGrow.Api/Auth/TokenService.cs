using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using MetaGrow.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace MetaGrow.Api.Auth;

public static class MfaScopes
{
    public const string Setup = "mfa_setup";
    public const string Login = "mfa_login";
    public const string Device = "mfa_device";
}

public interface ITokenService
{
    Task<(string AccessToken, DateTime ExpiresUtc)> CreateAccessTokenAsync(ApplicationUser user);
    Task<string> IssueRefreshTokenAsync(ApplicationUser user, string? ipAddress);
    Task<(ApplicationUser User, string NewRefreshToken)?> RotateRefreshTokenAsync(string refreshToken, string? ipAddress);
    Task RevokeRefreshTokenAsync(string refreshToken);
    string CreateScopedToken(ApplicationUser user, string scope, TimeSpan lifetime, string? securityStamp = null);
    (string UserId, string? SecurityStamp)? ValidateScopedToken(string token, string expectedScope);
}

public sealed class TokenService(
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    IOptions<JwtOptions> jwtOptions) : ITokenService
{
    private const string ScopeClaim = "metagrow_scope";
    private const string StampClaim = "metagrow_stamp";
    private readonly JwtOptions jwt = jwtOptions.Value;
    private string MfaAudience => $"{jwt.Audience}.mfa";

    public async Task<(string AccessToken, DateTime ExpiresUtc)> CreateAccessTokenAsync(ApplicationUser user)
    {
        var roles = await userManager.GetRolesAsync(user);
        var expiresUtc = DateTime.UtcNow.AddMinutes(jwt.AccessTokenMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var token = new JwtSecurityToken(
            issuer: jwt.Issuer,
            audience: jwt.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiresUtc,
            signingCredentials: new SigningCredentials(SigningKey(), SecurityAlgorithms.HmacSha256));

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresUtc);
    }

    public async Task<string> IssueRefreshTokenAsync(ApplicationUser user, string? ipAddress)
    {
        var token = GenerateTokenString();
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = Hash(token),
            CreatedUtc = DateTime.UtcNow,
            ExpiresUtc = DateTime.UtcNow.AddDays(jwt.RefreshTokenDays),
            CreatedByIp = ipAddress
        });
        await db.SaveChangesAsync();
        return token;
    }

    public async Task<(ApplicationUser User, string NewRefreshToken)?> RotateRefreshTokenAsync(
        string refreshToken,
        string? ipAddress)
    {
        var stored = await db.RefreshTokens
            .Include(token => token.User)
            .SingleOrDefaultAsync(token => token.TokenHash == Hash(refreshToken));

        if (stored?.User is null) return null;

        if (stored.ReplacedByTokenHash is not null)
        {
            var activeTokens = await db.RefreshTokens
                .Where(token => token.UserId == stored.UserId && token.RevokedUtc == null)
                .ToListAsync();
            foreach (var token in activeTokens) token.RevokedUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return null;
        }

        if (!stored.IsActive) return null;

        var replacement = GenerateTokenString();
        stored.RevokedUtc = DateTime.UtcNow;
        stored.ReplacedByTokenHash = Hash(replacement);
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = stored.UserId,
            TokenHash = stored.ReplacedByTokenHash,
            CreatedUtc = DateTime.UtcNow,
            ExpiresUtc = DateTime.UtcNow.AddDays(jwt.RefreshTokenDays),
            CreatedByIp = ipAddress
        });
        await db.SaveChangesAsync();
        return (stored.User, replacement);
    }

    public async Task RevokeRefreshTokenAsync(string refreshToken)
    {
        var stored = await db.RefreshTokens.SingleOrDefaultAsync(token => token.TokenHash == Hash(refreshToken));
        if (stored is not { RevokedUtc: null }) return;

        stored.RevokedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public string CreateScopedToken(ApplicationUser user, string scope, TimeSpan lifetime, string? securityStamp = null)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(ScopeClaim, scope)
        };
        if (securityStamp is not null) claims.Add(new Claim(StampClaim, securityStamp));

        var token = new JwtSecurityToken(
            issuer: jwt.Issuer,
            audience: MfaAudience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.Add(lifetime),
            signingCredentials: new SigningCredentials(SigningKey(), SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public (string UserId, string? SecurityStamp)? ValidateScopedToken(string token, string expectedScope)
    {
        try
        {
            var principal = new JwtSecurityTokenHandler { MapInboundClaims = false }.ValidateToken(
                token,
                new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = MfaAudience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = SigningKey(),
                    ClockSkew = TimeSpan.FromSeconds(30)
                },
                out _);

            if (principal.FindFirst(ScopeClaim)?.Value != expectedScope) return null;
            var userId = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
            return userId is null ? null : (userId, principal.FindFirst(StampClaim)?.Value);
        }
        catch
        {
            return null;
        }
    }

    private SymmetricSecurityKey SigningKey() => new(Encoding.UTF8.GetBytes(jwt.SigningKey));
    private static string GenerateTokenString() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
    private static string Hash(string token) => Convert.ToBase64String(SHA512.HashData(Encoding.UTF8.GetBytes(token)));
}
