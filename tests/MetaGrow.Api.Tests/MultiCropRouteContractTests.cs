using ApiModels.MetaGrow;
using MetaGrow.Api.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MetaGrow.Api.Tests;

public sealed class MultiCropRouteContractTests
{
    [Fact]
    public void Controller_UsesAdditiveUnversionedRoutes_AndCurrentRoles()
    {
        var controllerType = typeof(MultiCropSurveysController);
        var route = Assert.Single(controllerType.GetCustomAttributes(typeof(RouteAttribute), true).Cast<RouteAttribute>());
        var authorize = Assert.Single(controllerType.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        var getRoutes = controllerType.GetMethods()
            .SelectMany(method => method.GetCustomAttributes(typeof(HttpGetAttribute), true).Cast<HttpGetAttribute>())
            .Select(attribute => attribute.Template)
            .ToArray();

        Assert.Equal("multicrop", route.Template);
        Assert.Contains("surveys", getRoutes);
        Assert.Contains("survey-types", getRoutes);
        Assert.Equal(
            $"{MetaGrowRoles.Admin},{MetaGrowRoles.AgricultureManager},{MetaGrowRoles.Agronomist}",
            authorize.Roles);
    }
}
