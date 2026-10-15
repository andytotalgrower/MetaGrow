using ApiModels.MetaGrow;
using MetaGrow.Web.Components.Pages;
using Microsoft.AspNetCore.Authorization;

namespace MetaGrow.Web.Tests;

/// <summary>Direct page navigation must enforce the same restriction as API calls.</summary>
public sealed class SoilTestTemplateAccessTests
{
    [Fact]
    public void PageAllowsOnlyAdministratorsAndAgricultureManagers()
    {
        var attribute = typeof(SoilTestTemplates).GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>().Single(value => !string.IsNullOrWhiteSpace(value.Roles));
        var roles = attribute.Roles!.Split(',', StringSplitOptions.TrimEntries);
        Assert.Equal(new[] { MetaGrowRoles.Admin, MetaGrowRoles.AgricultureManager }, roles);
    }

    [Theory]
    [InlineData("x archived", true)]
    [InlineData("X archived", true)]
    [InlineData("Existing soil", false)]
    [InlineData("Soil x", false)]
    public void LegacyFilterMatchesOnlyTheBeginningOfTheName(string name, bool legacy)
    {
        Assert.Equal(legacy, SoilTemplateRules.IsLegacy(name));
    }
}
