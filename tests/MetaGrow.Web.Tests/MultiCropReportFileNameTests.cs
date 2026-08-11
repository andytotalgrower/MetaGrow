using MetaGrow.Web.Services;

namespace MetaGrow.Web.Tests;

public sealed class MultiCropReportFileNameTests
{
    [Fact]
    public void Build_uses_farm_name_and_survey_date()
    {
        var result = MultiCropReportFileName.Build("Test Avocado Farm", 42, new DateTime(2026, 4, 30));

        Assert.Equal("mcs_test-avocado-farm_20260430.pdf", result);
    }

    [Fact]
    public void Build_makes_the_farm_name_filename_safe_and_short()
    {
        var result = MultiCropReportFileName.Build(
            "Crème & Sons' Extremely Long Northern Demonstration Property",
            42,
            new DateTime(2026, 8, 11));

        Assert.Equal("mcs_creme-sons-extremely-long-northe_20260811.pdf", result);
    }

    [Fact]
    public void Build_falls_back_to_the_property_id_when_the_name_is_empty()
    {
        var result = MultiCropReportFileName.Build(" / ", 576, new DateTime(2026, 8, 11));

        Assert.Equal("mcs_farm-576_20260811.pdf", result);
    }
}
