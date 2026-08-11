using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;

namespace MetaGrow.Api.Services;

public interface IReportShareTokenService
{
    string CreateToken();
    string HashToken(string token);
    bool TokenMatches(string token, string expectedHash);
    string ProtectToken(string token);
    string? TryUnprotectToken(string protectedToken);
}

public sealed class ReportShareTokenService(IDataProtectionProvider dataProtection) : IReportShareTokenService
{
    private readonly IDataProtector protector = dataProtection.CreateProtector("MetaGrow.Api.ReportShareToken.v1");

    public string CreateToken() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    public string HashToken(string token) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public bool TokenMatches(string token, string expectedHash)
    {
        try
        {
            var actual = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            var expected = Convert.FromBase64String(expectedHash);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public string ProtectToken(string token) => protector.Protect(token);

    public string? TryUnprotectToken(string protectedToken)
    {
        try { return protector.Unprotect(protectedToken); }
        catch (CryptographicException) { return null; }
        catch (FormatException) { return null; }
    }
}
