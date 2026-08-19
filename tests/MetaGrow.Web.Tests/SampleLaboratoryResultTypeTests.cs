using ApiModels.MetaGrow;

namespace MetaGrow.Web.Tests;

public sealed class SampleLaboratoryResultTypeTests
{
    public static TheoryData<int, string, string> SupportedTypes => new()
    {
        { SampleLaboratoryResultType.Soil, "Soil", "soil" },
        { SampleLaboratoryResultType.Tissue, "Tissue", "tissue" },
        { SampleLaboratoryResultType.QuickSoil, "Quick Soil", "quick-soil" },
        { SampleLaboratoryResultType.Sap, "Sap", "sap" }
    };

    [Theory]
    [MemberData(nameof(SupportedTypes))]
    public void Supported_result_types_round_trip_through_routes(int id, string name, string slug)
    {
        Assert.True(SampleLaboratoryResultType.IsSupported(id));
        Assert.Equal(name, SampleLaboratoryResultType.Name(id));
        Assert.Equal(slug, SampleLaboratoryResultType.Slug(id));
        Assert.Equal(id, SampleLaboratoryResultType.FromSlug(slug));
    }

    [Fact]
    public void Unknown_result_types_are_rejected()
    {
        Assert.False(SampleLaboratoryResultType.IsSupported(0));
        Assert.Equal(0, SampleLaboratoryResultType.FromSlug("unknown"));
        Assert.Equal(string.Empty, SampleLaboratoryResultType.Slug(0));
    }
}
