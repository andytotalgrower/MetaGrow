using MetaGrow.Web.Components.Pages;
using MetaGrow.Web.Components.Public;
using Microsoft.AspNetCore.Authorization;

namespace MetaGrow.Web.Tests;

public sealed class ReportShareRouteTests
{
    [Fact]
    public void Shared_report_is_anonymous_while_standard_report_requires_staff_role()
    {
        Assert.NotNull(Attribute.GetCustomAttribute(typeof(SharedMultiCropReport), typeof(AllowAnonymousAttribute)));

        var authorization = typeof(MultiCropReport).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .Single(attribute => !string.IsNullOrWhiteSpace(attribute.Roles));
        Assert.Contains("Admin", authorization.Roles);
        Assert.Contains("Agronomist", authorization.Roles);
    }
}
