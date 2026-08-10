using Microsoft.AspNetCore.Components;

namespace MetaGrow.Web.Components.Account;

internal sealed class IdentityRedirectManager(NavigationManager navigation)
{
    // During static SSR, NavigateTo issues the redirect and returns normally (.NET 10+),
    // so callers must return immediately after calling RedirectTo.
    public void RedirectTo(string? uri)
    {
        uri ??= string.Empty;
        if (!Uri.IsWellFormedUriString(uri, UriKind.Relative)) uri = navigation.ToBaseRelativePath(uri);
        navigation.NavigateTo(uri);
    }
    public void RedirectTo(string uri, Dictionary<string, object?> query) =>
        RedirectTo(navigation.GetUriWithQueryParameters(uri, query));
}
