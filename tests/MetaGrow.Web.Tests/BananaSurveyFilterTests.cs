using ApiModels;
using MetaGrow.Shared;

namespace MetaGrow.Web.Tests;

public sealed class BananaSurveyFilterTests
{
    [Fact]
    public void Apply_FiltersByPropertyAndStatus_AndPrioritisesWorkInProgress()
    {
        BananaSurveySummaryDto[] surveys =
        [
            new() { SurveyId = 1, SurveyDate = new DateTime(2026, 8, 10), PropertyName = "River Farm", StatusId = 100, StatusName = "Complete, sent to client" },
            new() { SurveyId = 2, SurveyDate = new DateTime(2026, 6, 10), PropertyName = "River Bend", StatusId = 10, StatusName = "In Progress" },
            new() { SurveyId = 3, SurveyDate = new DateTime(2026, 7, 10), PropertyName = "Hill Farm", StatusId = 20, StatusName = "Awaiting QA" }
        ];

        Assert.Equal([2, 1], BananaSurveyFilter.Apply(surveys, "river", null).Select(item => item.SurveyId));
        Assert.Equal([3], BananaSurveyFilter.Apply(surveys, null, 20).Select(item => item.SurveyId));
    }

    [Fact]
    public void FilterPreferences_DefaultToPreviousTwoMonths_WithOtherFiltersCleared()
    {
        var filters = BananaSurveyFilterPreferences.CreateDefault(new DateTime(2026, 8, 12, 9, 30, 0));

        Assert.Equal(new DateTime(2026, 6, 12), filters.StartDate);
        Assert.Equal(new DateTime(2026, 8, 12), filters.EndDate);
        Assert.Null(filters.PropertySearch);
        Assert.Null(filters.StatusId);
        Assert.True(filters.HasValidDateRange);
    }

    [Fact]
    public void Summary_ReportsPhotoPresence()
    {
        Assert.True(new BananaSurveySummaryDto { PhotoCount = 8 }.HasPhotos);
        Assert.False(new BananaSurveySummaryDto().HasPhotos);
    }
}
