using ApiModels;
using MetaGrow.Web.Services;

namespace MetaGrow.Web.Tests;

public class UnifiedLabResultPresentationTests
{
    [Theory]
    [InlineData(null, "soil")]
    [InlineData("TISSUE", "tissue")]
    [InlineData("quicksoil", "quick-soil")]
    [InlineData("quick-soil", "quick-soil")]
    [InlineData("sap", "sap")]
    public void ResultTypeIsNormalisedForDeepLinks(string? input, string expected) =>
        Assert.Equal(expected, UnifiedLabResultPresentation.NormalizeType(input));

    [Fact]
    public void TissueResultLinksToItsSampleSurvey()
    {
        var row = Assert.Single(UnifiedLabResultPresentation.FromTissue(
        [
            new LabTissueDto
            {
                LabTissueId = 12,
                SampleSurveyId = 45,
                PropertyName = "Example Farm",
                BlockName = "North",
                NitrogenPc = 2.5
            }
        ]));

        Assert.Equal("Tissue", row.ResultType);
        Assert.Equal("/surveys/samples/45/edit", row.SurveyUrl);
        Assert.Contains("N 2.50", row.KeyResults);
    }

    [Fact]
    public void LegacySoilResultLinksToItsBananaSurvey()
    {
        var row = Assert.Single(UnifiedLabResultPresentation.FromSoil(
        [
            new LabSoilDto { LabSoilId = 8, VisitId = 72, PropertyName = "Example Farm" }
        ]));

        Assert.Equal("/surveys/banana/72/edit", row.SurveyUrl);
        Assert.Equal("Banana #72", row.SurveyLabel);
    }
}
