using ApiModels;
using MetaGrow.Shared;

namespace MetaGrow.Web.Tests;

public sealed class IncompleteSurveyFilterTests
{
    private static readonly IncompleteSurveySummaryDto[] Surveys =
    [
        Survey(1, IncompleteSurveyArea.MultiCrop, MultiCropSurveyStatus.InProgress, new DateTime(2026, 8, 10), "River Farm", 4),
        Survey(2, IncompleteSurveyArea.Banana, MultiCropSurveyStatus.AwaitingQa, new DateTime(2026, 8, 12), "Hill View", 5),
        Survey(3, IncompleteSurveyArea.MultiCrop, MultiCropSurveyStatus.AwaitingQa, new DateTime(2026, 7, 1), "River Bend", 4),
        Survey(4, IncompleteSurveyArea.Banana, MultiCropSurveyStatus.ReadyToSend, new DateTime(2026, 6, 20), "Valley", 6),
        Survey(5, IncompleteSurveyArea.Banana, MultiCropSurveyStatus.NotStarted, new DateTime(2026, 5, 15), "Orchard", 5),
        Survey(6, IncompleteSurveyArea.MultiCrop, MultiCropSurveyStatus.Complete, new DateTime(2026, 4, 1), "Completed Farm", 7)
    ];

    [Fact]
    public void Apply_UsesWorkflowPriorityThenOldestSurveyDate()
    {
        var result = IncompleteSurveyFilter.Apply(Surveys, null, null, null, null);

        Assert.Equal([3, 2, 4, 1, 5], result.Select(survey => survey.SurveyId));
    }

    [Fact]
    public void Apply_DefensivelyExcludesCompletedSurveys()
    {
        var result = IncompleteSurveyFilter.Apply(Surveys, null, null, null, null);

        Assert.DoesNotContain(result, survey => survey.StatusId == MultiCropSurveyStatus.Complete);
    }

    [Fact]
    public void Apply_FiltersSharedFields()
    {
        Assert.Equal(
            [3, 1],
            IncompleteSurveyFilter.Apply(Surveys, " river ", IncompleteSurveyArea.MultiCrop, null, 4)
                .Select(survey => survey.SurveyId));

        Assert.Equal(
            [2],
            IncompleteSurveyFilter.Apply(Surveys, null, IncompleteSurveyArea.Banana, MultiCropSurveyStatus.AwaitingQa, null)
                .Select(survey => survey.SurveyId));
    }

    [Theory]
    [InlineData(IncompleteSurveyArea.Banana, "area-banana")]
    [InlineData(IncompleteSurveyArea.MultiCrop, "area-multicrop")]
    public void AreaBadgeClass_UsesDistinctAreaClasses(IncompleteSurveyArea area, string expected) =>
        Assert.Equal(expected, IncompleteSurveyFilter.AreaBadgeClass(area));

    [Theory]
    [InlineData(IncompleteSurveyArea.Banana, 2144364933, "/surveys/banana/2144364933/edit", "/surveys/banana/2144364933/report", "/surveys/banana/2144364933/report?print=true")]
    [InlineData(IncompleteSurveyArea.MultiCrop, 57646, "/surveys/multicrop/57646/edit", "/surveys/multicrop/57646/report", "/surveys/multicrop/57646/report?print=true")]
    public void Navigation_BuildsAreaSpecificLinks(
        IncompleteSurveyArea area,
        int surveyId,
        string edit,
        string online,
        string print)
    {
        var survey = new IncompleteSurveySummaryDto { Area = area, SurveyId = surveyId };

        Assert.Equal(edit, IncompleteSurveyNavigation.EditUrl(survey));
        Assert.Equal(online, IncompleteSurveyNavigation.OnlineReportUrl(survey));
        Assert.Equal(print, IncompleteSurveyNavigation.PrintReportUrl(survey));
    }

    private static IncompleteSurveySummaryDto Survey(
        int id,
        IncompleteSurveyArea area,
        int statusId,
        DateTime date,
        string property,
        int agronomistId) => new()
        {
            SurveyId = id,
            Area = area,
            StatusId = statusId,
            StatusName = MultiCropSurveyStatus.Name(statusId),
            SurveyDate = date,
            PropertyName = property,
            AgronomistId = agronomistId
        };
}
