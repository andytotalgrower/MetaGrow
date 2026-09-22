using System.Reflection;
using System.Security.Claims;
using ApiModels.MetaGrow;
using ApiModels.Passkeys;
using MetaGrow.Web.Services;
using Metagen.AccountSettings.Razor.Configuration;
using Metagen.AccountSettings.Razor.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;

namespace MetaGrow.Web.Tests;

public class AccountSettingsIntegrationTests
{
    [Fact]
    public void MetaGrow_supports_the_complete_phase_four_feature_set()
    {
        var features = MetaGrowAccountSettingsBackend.SupportedFeatureSet;

        Assert.True(features.HasFlag(AccountSettingsFeatures.Dashboard));
        Assert.True(features.HasFlag(AccountSettingsFeatures.TwoFactorAuthentication));
        Assert.True(features.HasFlag(AccountSettingsFeatures.RecoveryCodes));
        Assert.True(features.HasFlag(AccountSettingsFeatures.Passkeys));
        Assert.False(features.HasFlag(AccountSettingsFeatures.Profile));
        Assert.False(features.HasFlag(AccountSettingsFeatures.PrimaryEmail));
        Assert.False(features.HasFlag(AccountSettingsFeatures.Password));
    }

    [Fact]
    public void Overview_mapper_uses_the_authenticated_identity_and_real_API_counts()
    {
        var principal = AuthenticatedPrincipal("andy@example.test");
        var mfa = new MetaGrowAccountLoadResult<MetaGrowMfaStatusResponse>(
            new() { TwoFactorEnabled = true, RecoveryCodesLeft = 6 },
            null);
        var passkeys = new MetaGrowAccountLoadResult<PasskeySummary[]>(
            [new(), new(), new()],
            null);

        var overview = MetaGrowAccountSettingsOverviewMapper.Map(principal, mfa, passkeys, isBrowserRemembered: true);

        Assert.Equal(AccountSettingsOverviewState.Available, overview.Identity.State);
        Assert.Equal("andy@example.test", overview.Identity.Value!.UserName);
        Assert.True(overview.Mfa.Value!.IsEnabled);
        Assert.Equal(6, overview.Mfa.Value.RecoveryCodesRemaining);
        Assert.True(overview.Mfa.Value.IsBrowserRemembered);
        Assert.Equal(3, overview.Passkeys.Value!.Count);
    }

    [Fact]
    public void Overview_mapper_preserves_independent_API_failures()
    {
        var principal = AuthenticatedPrincipal("andy@example.test");
        var mfa = new MetaGrowAccountLoadResult<MetaGrowMfaStatusResponse>(
            null,
            "MFA status failed.");
        var passkeys = new MetaGrowAccountLoadResult<PasskeySummary[]>(
            [new()],
            null);

        var overview = MetaGrowAccountSettingsOverviewMapper.Map(principal, mfa, passkeys);

        Assert.Equal(AccountSettingsOverviewState.Error, overview.Mfa.State);
        Assert.Equal("MFA status failed.", overview.Mfa.Message);
        Assert.Equal(AccountSettingsOverviewState.Available, overview.Passkeys.State);
        Assert.Equal(1, overview.Passkeys.Value!.Count);
    }

    [Fact]
    public void Production_route_is_authorized_and_does_not_expose_additional_email_addresses()
    {
        var repositoryRoot = FindRepositoryRoot();
        var route = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "MetaGrow.Web",
            "Components",
            "Pages",
            "AccountSettings.razor"));

        Assert.Contains("@page \"/Account/Manage\"", route);
        Assert.Contains("@page \"/Account/Manage/{*Section}\"", route);
        Assert.Contains("@attribute [Authorize]", route);
        Assert.Contains("@attribute [ExcludeFromInteractiveRouting]", route);
        Assert.Contains("@layout AccountLayout", route);
        Assert.DoesNotContain("[AllowAnonymous]", route);
        Assert.Contains("<AccountSettingsHost", route);
        Assert.DoesNotContain("EmailAddresses", route);

        var componentType = typeof(MetaGrow.Web.Components.Pages.AccountSettings);
        Assert.NotEmpty(componentType.GetCustomAttributes<AuthorizeAttribute>());
        Assert.Null(componentType.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.NotNull(componentType.GetCustomAttribute<ExcludeFromInteractiveRoutingAttribute>());
    }

    [Fact]
    public void Preview_route_redirects_to_production_account_settings()
    {
        var repositoryRoot = FindRepositoryRoot();
        var route = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "MetaGrow.Web",
            "Components",
            "Pages",
            "AccountSettingsPreview.razor"));

        Assert.Contains("@page \"/Account/SettingsPreview\"", route);
        Assert.Contains("@page \"/Account/SettingsPreview/{*Section}\"", route);
        Assert.Contains("Navigation.NavigateTo(target, replace: true)", route);
        Assert.Contains("/Account/Manage", route);
        Assert.DoesNotContain("<AccountSettingsHost", route);
    }

    [Fact]
    public void Web_app_registers_the_Fluent_account_settings_host_and_shared_assets()
    {
        var repositoryRoot = FindRepositoryRoot();
        var program = File.ReadAllText(Path.Combine(repositoryRoot, "src", "MetaGrow.Web", "Program.cs"));
        var app = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "MetaGrow.Web",
            "Components",
            "App.razor"));

        Assert.Contains("AddMetagenAccountSettings", program);
        Assert.Contains("options.BasePath = \"/Account/Manage\"", program);
        Assert.Contains("AccountSettingsVisualStyle.Fluent", program);
        Assert.Contains("IAccountSettingsBackend, MetaGrowAccountSettingsBackend", program);
        Assert.Contains("_content/Metagen.AccountSettings.Razor/metagen-account-settings.css", app);
        Assert.Contains("_content/Metagen.AccountSettings.Razor/account-settings-authenticator-code.js", app);
        Assert.Contains("_content/Metagen.AccountSettings.Razor/account-settings.js", app);
    }

    [Fact]
    public void Shared_feature_pages_replace_the_duplicated_MetaGrow_manage_pages()
    {
        var repositoryRoot = FindRepositoryRoot();
        var managePages = Path.Combine(
            repositoryRoot,
            "src",
            "MetaGrow.Web",
            "Components",
            "Account",
            "Pages",
            "Manage");

        Assert.True(File.Exists(Path.Combine(managePages, "EmailAddresses.razor")));
        Assert.False(File.Exists(Path.Combine(managePages, "TwoFactorAuthentication.razor")));
        Assert.False(File.Exists(Path.Combine(managePages, "GenerateRecoveryCodes.razor")));
        Assert.False(File.Exists(Path.Combine(managePages, "ResetAuthenticator.razor")));
        Assert.False(File.Exists(Path.Combine(managePages, "Passkeys.razor")));
    }

    [Fact]
    public void Additional_email_page_does_not_duplicate_shared_settings_navigation()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sharedComponents = Path.Combine(
            repositoryRoot,
            "src",
            "MetaGrow.Web",
            "Components",
            "Account",
            "Shared");
        var layout = File.ReadAllText(Path.Combine(sharedComponents, "ManageLayout.razor"));

        Assert.Contains("account-settings-shell--standalone", layout);
        Assert.DoesNotContain("<aside", layout);
        Assert.DoesNotContain("<ManageNavMenu", layout);
        Assert.False(File.Exists(Path.Combine(sharedComponents, "ManageNavMenu.razor")));
    }

    [Fact]
    public void Passkey_endpoints_delegate_management_to_the_shared_backend_contract()
    {
        var repositoryRoot = FindRepositoryRoot();
        var services = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "MetaGrow.Web",
            "Services",
            "AuthenticationServices.cs"));

        Assert.Contains("IAccountSettingsBackend account", services);
        Assert.Contains("account.BeginPasskeyCreationAsync", services);
        Assert.Contains("account.CompletePasskeyCreationAsync", services);
        Assert.Contains("account.RenamePasskeyAsync", services);
        Assert.Contains("account.DeletePasskeyAsync", services);
    }

    [Fact]
    public void Authenticator_management_rejects_setup_when_two_factor_is_already_enabled()
    {
        var repositoryRoot = FindRepositoryRoot();
        var controller = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "MetaGrow.Api",
            "Controllers",
            "AuthController.cs"));

        Assert.Equal(2, CountOccurrences(
            controller,
            "Two-factor authentication is already enabled. Reset the authenticator before setting it up again."));
    }

    private static ClaimsPrincipal AuthenticatedPrincipal(string email) =>
        new(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "user-1"),
                new Claim(ClaimTypes.Name, email),
                new Claim(ClaimTypes.Email, email)
            ],
            "Test"));

    private static int CountOccurrences(string value, string search)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(search, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += search.Length;
        }

        return count;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MetaGrow.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the MetaGrow repository root.");
    }
}
