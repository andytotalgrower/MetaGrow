namespace MetaGrow.Web.Tests;

public class SolutionSmokeTests
{
    [Fact]
    public void Shared_project_has_expected_assembly_name()
    {
        Assert.Equal("MetaGrow.Shared", typeof(MetaGrow.Shared.MetaGrowModule).Assembly.GetName().Name);
    }
}
