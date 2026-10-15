using MetaGrow.Api.Data;
using MetaGrow.Shared.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MetaGrow.Api.Services;

/// <summary>Reuses Identity's per-user key/value storage without requiring a schema migration.</summary>
public sealed class ReportBrandPreferenceStore(ApplicationDbContext _database)
{
    private const string _provider = "MetaGrow.Preferences";
    private const string _name = "ReportBrand";

    public async Task<string> GetAsync(string userId)
    {
        var value = await _database.UserTokens.AsNoTracking()
            .Where(item => item.UserId == userId && item.LoginProvider == _provider && item.Name == _name)
            .Select(item => item.Value).SingleOrDefaultAsync();
        return ReportBrandPreference.Normalize(value);
    }

    public async Task SaveAsync(string userId, string brandKey)
    {
        if (!ReportBrandPreference.IsSupported(brandKey)) throw new ArgumentException("Unsupported report brand.", nameof(brandKey));
        var preference = await _database.UserTokens.FindAsync(userId, _provider, _name);
        if (preference is null)
        {
            preference = new IdentityUserToken<string> { UserId = userId, LoginProvider = _provider, Name = _name };
            _database.UserTokens.Add(preference);
        }
        preference.Value = brandKey;
        await _database.SaveChangesAsync();
    }
}
