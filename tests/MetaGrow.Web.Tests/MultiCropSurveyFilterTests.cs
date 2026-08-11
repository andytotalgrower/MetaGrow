using ApiModels;
using MetaGrow.Shared;

namespace MetaGrow.Web.Tests;

public sealed class MultiCropSurveyFilterTests
{
    private static readonly MultiCropSurveySummaryDto[] Surveys =
    [
        new() { SurveyId = 1, SurveyDate = new DateTime(2026, 6, 10), PropertyName = "River Farm", ApplicationId = 10 },
        new() { SurveyId = 3, SurveyDate = new DateTime(2026, 8, 1), PropertyName = "Hill View", ApplicationId = 20 },
        new() { SurveyId = 2, SurveyDate = new DateTime(2026, 7, 1), PropertyName = "River Bend", ApplicationId = 20 }
    ];

    [Fact]
    public void Apply_FiltersPropertyCaseInsensitively_AndOrdersNewestFirst()
    {
        var result = MultiCropSurveyFilter.Apply(Surveys, "  RIVER ", null);

        Assert.Equal([2, 1], result.Select(survey => survey.SurveyId));
    }

    [Fact]
    public void Apply_FiltersBySurveyType()
    {
        var result = MultiCropSurveyFilter.Apply(Surveys, null, 20);

        Assert.Equal([3, 2], result.Select(survey => survey.SurveyId));
    }

    [Fact]
    public void Summary_BuildsExistingSurveyReferenceFormat_AndPhotoFlag()
    {
        var survey = new MultiCropSurveySummaryDto
        {
            SurveyDate = new DateTime(2026, 8, 10),
            SurveyNumber = 4,
            ClientReference = "PO-123",
            PhotoCount = 2
        };

        Assert.Equal("Survey #2026-4, P.O.# PO-123", survey.SurveyReference);
        Assert.True(survey.HasPhotos);
    }

    [Fact]
    public void Apply_FiltersByStatus()
    {
        MultiCropSurveySummaryDto[] surveys =
        [
            new() { SurveyId = 1, SurveyDate = new DateTime(2026, 6, 10), StatusId = 1, StatusName = "Completed" },
            new() { SurveyId = 2, SurveyDate = new DateTime(2026, 7, 1), StatusId = 2, StatusName = "Awaiting QA" }
        ];

        var result = MultiCropSurveyFilter.Apply(surveys, null, null, 2);

        Assert.Equal([2], result.Select(survey => survey.SurveyId));
    }

    [Fact]
    public void Apply_PutsSurveysRequiringActionFirst()
    {
        MultiCropSurveySummaryDto[] surveys =
        [
            new() { SurveyId = 1, SurveyDate = new DateTime(2026, 8, 20), StatusName = "Completed" },
            new() { SurveyId = 2, SurveyDate = new DateTime(2026, 6, 1), StatusName = "In Progress" },
            new() { SurveyId = 3, SurveyDate = new DateTime(2026, 7, 1), StatusName = "Awaiting QA" }
        ];

        var result = MultiCropSurveyFilter.Apply(surveys, null, null);

        Assert.Equal([3, 2, 1], result.Select(survey => survey.SurveyId));
    }

    [Theory]
    [InlineData("Awaiting QA", "status-awaiting-qa")]
    [InlineData("In Progress", "status-in-progress")]
    [InlineData("Completed", "status-completed")]
    [InlineData("Cancelled", "status-cancelled")]
    [InlineData("Something Else", "status-other")]
    [InlineData(null, "status-other")]
    public void StatusBadgeClass_MapsStatusNames(string? statusName, string expected)
    {
        Assert.Equal(expected, MultiCropSurveyFilter.StatusBadgeClass(statusName));
    }

    [Fact]
    public void FilterPreferences_DefaultToPreviousTwoMonths_WithOtherFiltersCleared()
    {
        var filters = MultiCropSurveyFilterPreferences.CreateDefault(new DateTime(2026, 8, 11, 14, 30, 0));

        Assert.Equal(new DateTime(2026, 6, 11), filters.StartDate);
        Assert.Equal(new DateTime(2026, 8, 11), filters.EndDate);
        Assert.Null(filters.PropertySearch);
        Assert.Null(filters.ApplicationId);
        Assert.Null(filters.StatusId);
        Assert.True(filters.HasValidDateRange);
    }

    [Fact]
    public void FilterPreferences_RejectAnEndDateBeforeTheStartDate()
    {
        var filters = new MultiCropSurveyFilterPreferences(
            new DateTime(2026, 8, 11),
            new DateTime(2026, 8, 10),
            null,
            null,
            null);

        Assert.False(filters.HasValidDateRange);
    }
}
