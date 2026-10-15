using System.Reflection;
using ApiModels.MetaGrow;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using MetaGrow.Web.Components.Pages;

namespace MetaGrow.Web.Tests;

public class ActionListsTests
{
    [Fact]
    public void ActionMaintenanceRequiresAnAdministrativeRole()
    {
        Assert.Equal("/admin/action-lists", typeof(ActionLists).GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Equal(new[] { MetaGrowRoles.Admin, MetaGrowRoles.AgricultureManager },
            typeof(ActionLists).GetCustomAttributes<AuthorizeAttribute>().Single(a => a.Roles is not null).Roles!.Split(','));
    }
}
