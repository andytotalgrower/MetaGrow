using ApiModels.MetaGrow;

namespace MetaGrow.Web.Tests;

public class AuthenticationContractTests
{
    [Fact]
    public void Initial_roles_are_distinct_and_stable()
    {
        Assert.Equal(3, MetaGrowRoles.All.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains(MetaGrowRoles.Admin, MetaGrowRoles.All);
        Assert.Contains(MetaGrowRoles.AgricultureManager, MetaGrowRoles.All);
        Assert.Contains(MetaGrowRoles.Agronomist, MetaGrowRoles.All);
    }
}
