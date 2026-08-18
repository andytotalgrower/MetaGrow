using ApiModels.MetaGrow;
using MetaGrow.Web.Components.Pages;
using Microsoft.AspNetCore.Authorization;

namespace MetaGrow.Web.Tests;

public sealed class SampleSoilSurveyGeneratorAuthorizationTests
{
    [Fact]
    public void Generator_is_available_to_operational_roles_but_not_accountant()
    {
        var authorization = typeof(SampleSoilSurveyGenerator)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .Single(attribute => !string.IsNullOrWhiteSpace(attribute.Roles));

        var roles = authorization.Roles!.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        Assert.Contains(MetaGrowRoles.Admin, roles);
        Assert.Contains(MetaGrowRoles.AgricultureManager, roles);
        Assert.Contains(MetaGrowRoles.Agronomist, roles);
        Assert.DoesNotContain(MetaGrowRoles.Accountant, roles);
    }
}
