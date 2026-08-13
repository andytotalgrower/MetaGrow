using ApiModels;
using MetaGrow.Shared;

namespace MetaGrow.Web.Tests;

public sealed class SampleSurveyFilterTests
{
    private static readonly SampleSurveySummaryDto[] Surveys =
    [
        new()
        {
            SurveyId = 1,
            SurveyDate = new DateTime(2026, 8, 10),
            PropertyName = "River Farm",
            AgronomistId = 10,
            MetagenSampleCount = 2,
            MetagenWorkflowName = "At lab",
            Laboratories = [new() { Id = 6, Name = "Metagen" }],
            TestTypes = [new() { Id = 101, Name = "DNA" }]
        },
        new()
        {
            SurveyId = 2,
            SurveyDate = new DateTime(2026, 8, 11),
            PropertyName = "Hill Farm",
            AgronomistId = 20,
            MetagenSampleCount = 1,
            NutritionSampleCount = 3,
            MetagenWorkflowName = "Complete, Sent to Client",
            NutritionWorkflowName = "Ready for agronomist",
            Laboratories = [new() { Id = 2, Name = "Nutrient Advantage" }, new() { Id = 6, Name = "Metagen" }],
            TestTypes = [new() { Id = 202, Name = "Soil" }]
        }
    ];

    [Fact]
    public void Apply_FiltersOneSurveyRowByCategoryWorkflowLabAndTest()
    {
        Assert.Equal([2], SampleSurveyFilter.Apply(Surveys, null, null, "Nutrition", null, null, null).Select(item => item.SurveyId));
        Assert.Equal([1], SampleSurveyFilter.Apply(Surveys, "river", 10, null, "At lab", 6, 101).Select(item => item.SurveyId));
        Assert.Empty(SampleSurveyFilter.Apply(Surveys, null, null, null, null, 2, 101));
    }

    [Fact]
    public void Preferences_DefaultToPreviousThreeMonths()
    {
        var preferences = SampleSurveyFilterPreferences.CreateDefault(new DateTime(2026, 8, 13, 9, 0, 0));
        Assert.Equal(new DateTime(2026, 5, 13), preferences.StartDate);
        Assert.Equal(new DateTime(2026, 8, 13), preferences.EndDate);
        Assert.True(preferences.HasValidDateRange);
    }

    [Theory]
    [InlineData("In transit", "status-in-transit")]
    [InlineData("At lab", "status-at-lab")]
    [InlineData("Ready for agronomist", "status-ready")]
    [InlineData("Complete, Sent to Client", "status-completed")]
    public void StatusBadgeClass_MapsOperationalStates(string status, string expected) =>
        Assert.Equal(expected, SampleSurveyFilter.StatusBadgeClass(status));
}
