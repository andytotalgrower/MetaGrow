using ApiModels.MetaGrow;

namespace MetaGrow.Api.Tests;

public sealed class RoleContractTests
{
    [Fact]
    public void Initial_registration_roles_are_exactly_the_agreed_roles()
    {
        Assert.Equal(["Admin", "Agriculture Manager", "Agronomist"], MetaGrowRoles.All);
    }
}
