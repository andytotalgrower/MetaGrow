using MetaGrow.Api.Services;
using Microsoft.AspNetCore.DataProtection;

namespace MetaGrow.Api.Tests;

public sealed class ReportShareTokenServiceTests
{
    [Fact]
    public void Tokens_are_random_hashable_and_recoverable_for_authorised_reuse()
    {
        var service = new ReportShareTokenService(new EphemeralDataProtectionProvider());

        var first = service.CreateToken();
        var second = service.CreateToken();
        var hash = service.HashToken(first);
        var protectedToken = service.ProtectToken(first);

        Assert.NotEqual(first, second);
        Assert.DoesNotContain('=', first);
        Assert.True(service.TokenMatches(first, hash));
        Assert.False(service.TokenMatches(second, hash));
        Assert.NotEqual(first, protectedToken);
        Assert.Equal(first, service.TryUnprotectToken(protectedToken));
    }
}
