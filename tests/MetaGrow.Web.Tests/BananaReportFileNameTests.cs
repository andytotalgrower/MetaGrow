using MetaGrow.Web.Services;

namespace MetaGrow.Web.Tests;

public sealed class BananaReportFileNameTests
{
    [Fact]
    public void Build_uses_farm_name_and_survey_date()
    {
        var result = BananaReportFileName.Build("Davidson Road Bananas", 42, new DateTime(2026, 1, 20));

        Assert.Equal("banana_davidson-road-bananas_20260120.pdf", result);
    }

    [Fact]
    public void Build_falls_back_to_property_id()
    {
        var result = BananaReportFileName.Build(" / ", 576, new DateTime(2026, 8, 11));

        Assert.Equal("banana_farm-576_20260811.pdf", result);
    }
}
