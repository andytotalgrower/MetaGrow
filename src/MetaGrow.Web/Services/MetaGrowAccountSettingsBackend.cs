using System.Security.Claims;
using ApiModels.MetaGrow;
using ApiModels.Passkeys;
using Metagen.AccountSettings.Razor.Backend;
using Metagen.AccountSettings.Razor.Configuration;
using Metagen.AccountSettings.Razor.Models;
using Microsoft.AspNetCore.Components.Authorization;

namespace MetaGrow.Web.Services;

public sealed class MetaGrowAccountSettingsBackend(
    AccountApiClient accountApi,
    AuthenticationStateProvider authenticationStateProvider,
    MfaFlowState mfaFlow,
    IHttpContextAccessor contextAccessor,
    ILogger<MetaGrowAccountSettingsBackend> logger) : IAccountSettingsBackend
{
    public const AccountSettingsFeatures SupportedFeatureSet =
        AccountSettingsFeatures.Dashboard |
        AccountSettingsFeatures.TwoFactorAuthentication |
        AccountSettingsFeatures.RecoveryCodes |
        AccountSettingsFeatures.Passkeys;

    public AccountSettingsFeatures SupportedFeatures => SupportedFeatureSet;

    public async Task<AccountSettingsOverview> GetOverviewAsync(
        CancellationToken cancellationToken = default)
    {
        var principal = (await authenticationStateProvider.GetAuthenticationStateAsync()).User;
        var mfaTask = LoadMfaAsync(cancellationToken);
        var passkeysTask = LoadPasskeysAsync(cancellationToken);

        await Task.WhenAll(mfaTask, passkeysTask);

        return MetaGrowAccountSettingsOverviewMapper.Map(
            principal,
            await mfaTask,
            await passkeysTask,
            IsBrowserRemembered());
    }

    public async Task<AccountOperationResult<AccountMfaSummary>> GetMfaStatusAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var (status, error) = await accountApi.GetMfaStatusAsync(cancellationToken);
            return status is null
                ? AccountOperationResult<AccountMfaSummary>.Failure(error ?? "Two-factor authentication status could not be loaded.")
                : AccountOperationResult<AccountMfaSummary>.Success(
                    new(status.TwoFactorEnabled, status.RecoveryCodesLeft, IsBrowserRemembered()));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not load the account-settings MFA status.");
            return AccountOperationResult<AccountMfaSummary>.Failure("Two-factor authentication status could not be loaded.");
        }
    }

    public async Task<AccountOperationResult<AuthenticatorSetupDetails>> BeginAuthenticatorSetupAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var (setup, error) = await accountApi.ManageMfaSetupInfoAsync(cancellationToken);
            return setup is null
                ? AccountOperationResult<AuthenticatorSetupDetails>.Failure(error ?? "Authenticator setup could not be started.")
                : AccountOperationResult<AuthenticatorSetupDetails>.Success(
                    new(setup.SharedKey, setup.AuthenticatorUri));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not begin account-settings authenticator setup.");
            return AccountOperationResult<AuthenticatorSetupDetails>.Failure("Authenticator setup could not be started.");
        }
    }

    public async Task<AccountOperationResult<RecoveryCodeResult>> VerifyAuthenticatorSetupAsync(
        string verificationCode,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var (response, error) = await accountApi.ManageMfaSetupAsync(verificationCode, cancellationToken);
            return response is null
                ? AccountOperationResult<RecoveryCodeResult>.Failure(error ?? "The verification code is invalid.")
                : AccountOperationResult<RecoveryCodeResult>.Success(new(response.RecoveryCodes));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not verify account-settings authenticator setup.");
            return AccountOperationResult<RecoveryCodeResult>.Failure("Authenticator setup could not be completed.");
        }
    }

    public async Task<AccountOperationResult> DisableMfaAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunOperationAsync(
            token => accountApi.DisableMfaAsync(token),
            "Two-factor authentication has been disabled.",
            "Two-factor authentication could not be disabled.",
            cancellationToken);
        if (result.Succeeded) ClearRememberedBrowser();
        return result;
    }

    public async Task<AccountOperationResult> ResetAuthenticatorAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunOperationAsync(
            token => accountApi.ResetAuthenticatorAsync(token),
            "Authenticator reset.",
            "The authenticator could not be reset.",
            cancellationToken);
        if (result.Succeeded) ClearRememberedBrowser();
        return result;
    }

    public Task<AccountOperationResult> ForgetRememberedBrowserAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ClearRememberedBrowser();
        return Task.FromResult(AccountOperationResult.Success("This browser is no longer remembered."));
    }

    public async Task<AccountOperationResult<RecoveryCodeResult>> GenerateRecoveryCodesAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var (response, error) = await accountApi.NewRecoveryCodesAsync(cancellationToken);
            return response is null
                ? AccountOperationResult<RecoveryCodeResult>.Failure(error ?? "Recovery codes could not be generated.")
                : AccountOperationResult<RecoveryCodeResult>.Success(new(response.RecoveryCodes));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not generate account-settings recovery codes.");
            return AccountOperationResult<RecoveryCodeResult>.Failure("Recovery codes could not be generated.");
        }
    }

    public async Task<AccountOperationResult<AccountPasskeyCollection>> GetPasskeysAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var (passkeys, error) = await accountApi.GetPasskeysAsync(cancellationToken);
            return passkeys is null
                ? AccountOperationResult<AccountPasskeyCollection>.Failure(error ?? "Passkeys could not be loaded.")
                : AccountOperationResult<AccountPasskeyCollection>.Success(
                    new(passkeys.Select(MapPasskey).ToArray(), 10));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not load account-settings passkeys.");
            return AccountOperationResult<AccountPasskeyCollection>.Failure("Passkeys could not be loaded.");
        }
    }

    public async Task<AccountOperationResult<AccountPasskeyCreationOptions>> BeginPasskeyCreationAsync(
        string displayName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Trim().Length > 64)
        {
            return AccountOperationResult<AccountPasskeyCreationOptions>.Failure(
                "Enter a passkey name of 1 to 64 characters.");
        }

        try
        {
            var (options, error) = await accountApi.PasskeyCreationOptionsAsync(displayName.Trim(), cancellationToken);
            return options is null
                ? AccountOperationResult<AccountPasskeyCreationOptions>.Failure(error ?? "Passkey setup could not be started.")
                : AccountOperationResult<AccountPasskeyCreationOptions>.Success(
                    new(options.CeremonyId, options.OptionsJson));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not begin account-settings passkey creation.");
            return AccountOperationResult<AccountPasskeyCreationOptions>.Failure("Passkey setup could not be started.");
        }
    }

    public Task<AccountOperationResult> CompletePasskeyCreationAsync(
        AccountPasskeyAttestation attestation,
        CancellationToken cancellationToken = default) =>
        RunOperationAsync(
            token => accountApi.RegisterPasskeyAsync(
                new PasskeyAttestationRequest
                {
                    CeremonyId = attestation.CeremonyId,
                    CredentialJson = attestation.CredentialJson,
                    DisplayName = attestation.DisplayName
                },
                token),
            "Passkey added.",
            "The passkey could not be saved.",
            cancellationToken);

    public Task<AccountOperationResult> RenamePasskeyAsync(
        string credentialId,
        string displayName,
        CancellationToken cancellationToken = default) =>
        RunOperationAsync(
            token => accountApi.RenamePasskeyAsync(credentialId, displayName, token),
            "Passkey renamed.",
            "The passkey could not be renamed.",
            cancellationToken);

    public Task<AccountOperationResult> DeletePasskeyAsync(
        string credentialId,
        CancellationToken cancellationToken = default) =>
        RunOperationAsync(
            token => accountApi.DeletePasskeyAsync(credentialId, token),
            "Passkey deleted.",
            "The passkey could not be deleted.",
            cancellationToken);

    private async Task<MetaGrowAccountLoadResult<MetaGrowMfaStatusResponse>> LoadMfaAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var (status, error) = await accountApi.GetMfaStatusAsync(cancellationToken);
            return new(status, error);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not load the account-settings MFA summary.");
            return new(null, "Two-factor authentication status could not be loaded.");
        }
    }

    private async Task<MetaGrowAccountLoadResult<PasskeySummary[]>> LoadPasskeysAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var (passkeys, error) = await accountApi.GetPasskeysAsync(cancellationToken);
            return new(passkeys, error);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not load the account-settings passkey summary.");
            return new(null, "Passkey information could not be loaded.");
        }
    }

    private bool IsBrowserRemembered()
    {
        var context = contextAccessor.HttpContext;
        return context is not null && mfaFlow.ReadDeviceToken(context) is not null;
    }

    private void ClearRememberedBrowser()
    {
        var context = contextAccessor.HttpContext;
        if (context is not null) mfaFlow.ClearDeviceToken(context);
    }

    private async Task<AccountOperationResult> RunOperationAsync(
        Func<CancellationToken, Task<string?>> operation,
        string successMessage,
        string failureMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            var error = await operation(cancellationToken);
            return error is null
                ? AccountOperationResult.Success(successMessage)
                : AccountOperationResult.Failure(error);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "An account-settings operation failed.");
            return AccountOperationResult.Failure(failureMessage);
        }
    }

    private static AccountPasskey MapPasskey(PasskeySummary passkey) =>
        new(passkey.CredentialId, passkey.DisplayName, passkey.CreatedAt, passkey.IsBackedUp);
}

internal sealed record MetaGrowAccountLoadResult<T>(T? Value, string? Error)
    where T : class;

internal static class MetaGrowAccountSettingsOverviewMapper
{
    public static AccountSettingsOverview Map(
        ClaimsPrincipal principal,
        MetaGrowAccountLoadResult<MetaGrowMfaStatusResponse> mfa,
        MetaGrowAccountLoadResult<PasskeySummary[]> passkeys,
        bool isBrowserRemembered = false) =>
        new()
        {
            Identity = MapIdentity(principal),
            Mfa = MapMfa(mfa, isBrowserRemembered),
            Passkeys = MapPasskeys(passkeys)
        };

    private static AccountSettingsOverviewSection<AccountIdentitySummary> MapIdentity(
        ClaimsPrincipal principal)
    {
        var userName = principal.FindFirstValue(ClaimTypes.Email) ?? principal.Identity?.Name;
        return string.IsNullOrWhiteSpace(userName)
            ? AccountSettingsOverviewSection<AccountIdentitySummary>.Unavailable(
                "Your account identity is temporarily unavailable.")
            : AccountSettingsOverviewSection<AccountIdentitySummary>.Available(new(userName));
    }

    private static AccountSettingsOverviewSection<AccountMfaSummary> MapMfa(
        MetaGrowAccountLoadResult<MetaGrowMfaStatusResponse> result,
        bool isBrowserRemembered)
    {
        if (result.Value is not null)
        {
            return AccountSettingsOverviewSection<AccountMfaSummary>.Available(
                new(result.Value.TwoFactorEnabled, result.Value.RecoveryCodesLeft, isBrowserRemembered));
        }

        return AccountSettingsOverviewSection<AccountMfaSummary>.Failed(
            result.Error ?? "Two-factor authentication status could not be loaded.");
    }

    private static AccountSettingsOverviewSection<AccountPasskeySummary> MapPasskeys(
        MetaGrowAccountLoadResult<PasskeySummary[]> result)
    {
        if (result.Value is not null)
        {
            return AccountSettingsOverviewSection<AccountPasskeySummary>.Available(
                new(result.Value.Length));
        }

        return AccountSettingsOverviewSection<AccountPasskeySummary>.Failed(
            result.Error ?? "Passkey information could not be loaded.");
    }
}
