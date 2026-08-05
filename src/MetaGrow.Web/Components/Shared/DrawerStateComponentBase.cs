using Microsoft.AspNetCore.Components;

namespace MetaGrow.Web.Components.Shared;

public abstract class DrawerStateComponentBase : ComponentBase
{
    [SupplyParameterFromQuery(Name = DrawerStateUrlBuilder.QueryParameterName)]
    public bool ToggledDrawer { get; set; }

    [Inject]
    private NavigationManager NavigationManager { get; set; } = null!;

    protected string AddDrawerStateToUrl(string baseUrl) =>
        DrawerStateUrlBuilder.AddStateToUrl(baseUrl, ToggledDrawer, NavigationManager);

    protected string AddDrawerStateToUrlToggled(string baseUrl) =>
        DrawerStateUrlBuilder.AddStateToUrl(baseUrl, !ToggledDrawer, NavigationManager);
}

public abstract class DrawerStateLayoutComponentBase : LayoutComponentBase
{
    [SupplyParameterFromQuery(Name = DrawerStateUrlBuilder.QueryParameterName)]
    public bool ToggledDrawer { get; set; }

    [Inject]
    private NavigationManager NavigationManager { get; set; } = null!;

    protected string AddDrawerStateToUrl(string baseUrl) =>
        DrawerStateUrlBuilder.AddStateToUrl(baseUrl, ToggledDrawer, NavigationManager);

    protected string AddDrawerStateToUrlToggled(string baseUrl) =>
        DrawerStateUrlBuilder.AddStateToUrl(baseUrl, !ToggledDrawer, NavigationManager);
}

internal static class DrawerStateUrlBuilder
{
    public const string QueryParameterName = "toggledSidebar";

    public static string AddStateToUrl(string baseUrl, bool toggledDrawer, NavigationManager navigationManager) =>
        navigationManager.GetUriWithQueryParameters(
            baseUrl,
            new Dictionary<string, object?>
            {
                [QueryParameterName] = toggledDrawer ? true : null
            });
}
