using Microsoft.AspNetCore.Identity;

namespace MetaGrow.Api.Data;

public sealed class ApplicationUser : IdentityUser
{
    public ICollection<RefreshToken> RefreshTokens { get; set; } = [];
    public ICollection<UserEmailAddress> EmailAddresses { get; set; } = [];
}
