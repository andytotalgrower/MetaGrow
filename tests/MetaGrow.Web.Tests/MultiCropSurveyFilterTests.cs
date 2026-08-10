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
}
