using System.Buffers.Text;
using System.Collections.Concurrent;
using ApiModels.Passkeys;
using MetaGrow.Api.Data;
using Microsoft.AspNetCore.Identity;

namespace MetaGrow.Api.Auth;

public sealed class PasskeyService(UserManager<ApplicationUser> users, IPasskeyHandler<ApplicationUser> handler)
{
    private const int MaxPasskeys = 10;
    private static readonly ConcurrentDictionary<string, Ceremony> Ceremonies = new();

    public async Task<PasskeyOptionsResponse> MakeRequestOptionsAsync(string? username, HttpContext context)
    {
        var user = string.IsNullOrWhiteSpace(username) ? null : await users.FindByNameAsync(username);
        var result = await handler.MakeRequestOptionsAsync(user, context);
        return Store(Kind.Assertion, result.AssertionState!, null, result.RequestOptionsJson);
    }

    public async Task<(ApplicationUser? User, string? Error)> AssertAsync(PasskeyAssertionRequest request, HttpContext context)
    {
        if (!Take(request.CeremonyId, Kind.Assertion, out var ceremony)) return (null, "The passkey request expired or was already used.");
        var result = await handler.PerformAssertionAsync(new() { HttpContext = context, CredentialJson = request.CredentialJson, AssertionState = ceremony.State });
        if (!result.Succeeded) return (null, result.Failure.Message);
        var update = await users.AddOrUpdatePasskeyAsync(result.User, result.Passkey);
        return update.Succeeded ? (result.User, null) : (null, "The passkey could not be updated.");
    }

    public async Task<(PasskeyOptionsResponse? Response, string? Error)> MakeCreationOptionsAsync(ApplicationUser user, string displayName, HttpContext context)
    {
        if ((await users.GetPasskeysAsync(user)).Count >= MaxPasskeys) return (null, $"A maximum of {MaxPasskeys} passkeys is allowed.");
        var userId = await users.GetUserIdAsync(user);
        var userName = await users.GetUserNameAsync(user) ?? displayName;
        var result = await handler.MakeCreationOptionsAsync(new() { Id = userId, Name = userName, DisplayName = displayName }, context);
        return (Store(Kind.Attestation, result.AttestationState!, userId, result.CreationOptionsJson), null);
    }

    public async Task<string?> AttestAsync(ApplicationUser user, PasskeyAttestationRequest request, HttpContext context)
    {
        if (!Take(request.CeremonyId, Kind.Attestation, out var ceremony)) return "The passkey registration expired or was already used.";
        var userId = await users.GetUserIdAsync(user);
        if (ceremony.UserId != userId) return "The passkey registration does not belong to this account.";
        if ((await users.GetPasskeysAsync(user)).Count >= MaxPasskeys) return $"A maximum of {MaxPasskeys} passkeys is allowed.";
        var result = await handler.PerformAttestationAsync(new() { HttpContext = context, CredentialJson = request.CredentialJson, AttestationState = ceremony.State });
        if (!result.Succeeded) return result.Failure.Message;
        if (result.UserEntity.Id != userId) return "The passkey was created for a different account.";
        result.Passkey.Name = request.DisplayName.Trim();
        return (await users.AddOrUpdatePasskeyAsync(user, result.Passkey)).Succeeded ? null : "The passkey could not be saved.";
    }

    public async Task<PasskeySummary[]> ListAsync(ApplicationUser user) =>
        (await users.GetPasskeysAsync(user)).Select(p => new PasskeySummary {
            CredentialId = Base64Url.EncodeToString(p.CredentialId), DisplayName = p.Name ?? "Unnamed passkey",
            CreatedAt = p.CreatedAt, IsBackedUp = p.IsBackedUp }).ToArray();

    public async Task<string?> RenameAsync(ApplicationUser user, string encodedId, string name)
    {
        if (!Decode(encodedId, out var id)) return "The passkey ID is invalid.";
        var passkey = await users.GetPasskeyAsync(user, id);
        if (passkey is null) return "The passkey was not found.";
        passkey.Name = name.Trim();
        return (await users.AddOrUpdatePasskeyAsync(user, passkey)).Succeeded ? null : "The passkey could not be renamed.";
    }

    public async Task<string?> DeleteAsync(ApplicationUser user, string encodedId)
    {
        if (!Decode(encodedId, out var id)) return "The passkey ID is invalid.";
        return (await users.RemovePasskeyAsync(user, id)).Succeeded ? null : "The passkey could not be deleted.";
    }

    private static PasskeyOptionsResponse Store(Kind kind, string state, string? userId, string json)
    {
        foreach (var expired in Ceremonies.Where(item => item.Value.ExpiresAt <= DateTimeOffset.UtcNow).Take(100))
            Ceremonies.TryRemove(expired.Key, out _);
        var id = Guid.NewGuid().ToString("N");
        Ceremonies[id] = new(kind, state, userId, DateTimeOffset.UtcNow.AddMinutes(5));
        return new() { CeremonyId = id, OptionsJson = json };
    }
    private static bool Take(string id, Kind kind, out Ceremony ceremony)
    {
        if (Ceremonies.TryRemove(id, out ceremony!) && ceremony.Kind == kind && ceremony.ExpiresAt > DateTimeOffset.UtcNow) return true;
        ceremony = default!; return false;
    }
    private static bool Decode(string value, out byte[] id) { try { id = Base64Url.DecodeFromChars(value); return true; } catch (FormatException) { id = []; return false; } }
    private enum Kind { Assertion, Attestation }
    private sealed record Ceremony(Kind Kind, string State, string? UserId, DateTimeOffset ExpiresAt);
}
