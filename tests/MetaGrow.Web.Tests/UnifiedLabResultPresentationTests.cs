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
        Assert.Equal("Survey #45", row.SurveyLabel);
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

    [Fact]
    public void SoilKeyResultsUseTheOperationalSummaryFields()
    {
        var row = Assert.Single(UnifiedLabResultPresentation.FromSoil(
        [
            new LabSoilDto
            {
                PhCaCl2 = 5.6,
                CecMeq = 12.3,
                No3nPpm = 8.4,
                CalciumPpm = 1250,
                MagnesiumPpm = 390
            }
        ]));

        Assert.Equal(
            "pH CaCl₂ 5.60 · CEC 12.30 · NO₃-N 8.40 · Ca 1,250.00 · Mg 390.00",
            row.KeyResults);
    }
}
