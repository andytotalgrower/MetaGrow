using ApiModels;
using MetaGrow.Web.Components.Pages;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;

namespace MetaGrow.Web.Tests;

public sealed class MultiCropReportingHubTests
{
    [Fact]
    public void Hub_has_default_and_tab_routes()
    {
        var routes = typeof(MultiCropReports).GetCustomAttributes(typeof(RouteAttribute), true)
            .Cast<RouteAttribute>()
            .Select(route => route.Template)
            .ToList();

        Assert.Contains("/reports/multicrop", routes);
        Assert.Contains("/reports/multicrop/{View}", routes);
    }

    [Fact]
    public void Hub_is_restricted_to_multicrop_staff_roles()
    {
        var roles = typeof(MultiCropReports).GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>()
            .SelectMany(attribute => (attribute.Roles ?? string.Empty).Split(',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("Admin", roles);
        Assert.Contains("Agriculture Manager", roles);
        Assert.Contains("Agronomist", roles);
    }

    [Fact]
    public void Matrix_request_uses_optional_property_without_billing_entity()
    {
        var request = new MultiCropPbdMatrixExportRequest();

        Assert.Null(request.PropertyId);
        Assert.Equal(MultiCropPbdGrouping.MonthParentCropAndParameter, request.Grouping);
        Assert.DoesNotContain(typeof(MultiCropPbdMatrixExportRequest).GetProperties(),
            property => property.Name.Contains("Billing", StringComparison.OrdinalIgnoreCase));
    }
}
