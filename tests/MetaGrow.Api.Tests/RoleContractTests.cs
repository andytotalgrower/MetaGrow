using ApiModels.MetaGrow;
using MetaGrow.Api.Controllers;
using Microsoft.AspNetCore.Authorization;

namespace MetaGrow.Api.Tests;

public sealed class RoleContractTests
{
    [Fact]
    public void Initial_registration_roles_are_exactly_the_agreed_roles()
    {
        Assert.Equal(["Admin", "Agriculture Manager", "Agronomist"], MetaGrowRoles.All);
    }

    [Theory]
    [InlineData(nameof(ReportSharesController.GetForSurvey))]
    [InlineData(nameof(ReportSharesController.Create))]
    [InlineData(nameof(ReportSharesController.Revoke))]
    public void Every_staff_role_can_manage_report_shares(string methodName)
    {
        var method = typeof(ReportSharesController).GetMethod(methodName)!;
        var authorization = Assert.Single(method.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>());

        Assert.Equal(MetaGrowRoles.All,
            authorization.Roles!.Split(',', StringSplitOptions.TrimEntries));
    }
}
