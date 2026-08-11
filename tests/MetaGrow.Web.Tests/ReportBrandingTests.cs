using MetaGrow.Web.Services;

namespace MetaGrow.Web.Tests;

public sealed class ReportBrandingTests
{
    [Fact]
    public void Both_report_brands_are_available()
    {
        Assert.Collection(
            ReportBranding.Options,
            option =>
            {
                Assert.Equal(ReportBranding.MetagenKey, option.Key);
                Assert.Equal("Metagen", option.Label);
            },
            option =>
            {
                Assert.Equal(ReportBranding.TotalGrowerServicesKey, option.Key);
                Assert.Equal("Total Grower Services", option.Label);
            });
    }

    [Fact]
    public void Unknown_brand_falls_back_to_metagen()
    {
        var result = ReportBranding.Get("unknown");

        Assert.Equal(ReportBranding.MetagenKey, result.Key);
    }
}
