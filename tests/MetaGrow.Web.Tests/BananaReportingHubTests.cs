using ApiModels;
using MetaGrow.Web.Components.Pages;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using System.Reflection;

namespace MetaGrow.Web.Tests;

public sealed class BananaReportingHubTests
{
    [Fact]
    public void Hub_has_default_and_tab_routes()
    {
        var routes = typeof(BananaReports).GetCustomAttributes(typeof(RouteAttribute), true)
            .Cast<RouteAttribute>()
            .Select(route => route.Template)
            .ToList();

        Assert.Contains("/reports/banana", routes);
        Assert.Contains("/reports/banana/{View}", routes);
    }

    [Fact]
    public void Hub_is_restricted_to_reporting_roles()
    {
        var roles = typeof(BananaReports).GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>()
            .SelectMany(attribute => (attribute.Roles ?? string.Empty).Split(',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("Admin", roles);
        Assert.Contains("Agriculture Manager", roles);
        Assert.Contains("Agronomist", roles);
    }

    [Fact]
    public void Finger_count_request_defaults_to_excel_and_anonymous_secondaries()
    {
        var request = new BananaFingerCountComparisonRequest();

        Assert.Equal(BananaReportExportFormat.Xlsx, request.Format);
        Assert.True(request.AnonymousSecondaryFarms);
        Assert.Empty(request.SecondaryPropertyIds);
    }

    [Fact]
    public void Finger_count_request_accepts_negative_property_ids()
    {
        var component = new BananaReports();
        typeof(BananaReports).GetField("_primaryPropertyId", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(component, -1);
        var secondaryPropertyIds = (List<int?>)typeof(BananaReports)
            .GetField("_secondaryPropertyIds", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(component)!;
        secondaryPropertyIds.Clear();
        secondaryPropertyIds.Add(-2);

        object?[] arguments = [null];
        var isValid = (bool)typeof(BananaReports)
            .GetMethod("TryBuildRequest", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(component, arguments)!;
        var request = Assert.IsType<BananaFingerCountComparisonRequest>(arguments[0]);

        Assert.True(isValid);
        Assert.Equal(-1, request.PrimaryPropertyId);
        Assert.Equal([-2], request.SecondaryPropertyIds);
    }
}
