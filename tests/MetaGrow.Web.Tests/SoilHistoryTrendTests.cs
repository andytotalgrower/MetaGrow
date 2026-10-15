using MetaGrow.Shared.Models;
using MetaGrow.Web.Services;

namespace MetaGrow.Web.Tests;

public sealed class SoilHistoryTrendTests
{
    [Fact]
    public void Trends_sort_saved_rounds_and_preserve_zero_values_and_missing_positions()
    {
        var history = new List<SoilReportHistoryRound>
        {
            new() { SurveyId = 3, Date = new(2026, 9, 1), Averages = new() { ["CEC"] = 12 } },
            new() { SurveyId = 1, Date = new(2024, 9, 1), Averages = new() { ["cec"] = 0 } },
            new() { SurveyId = 2, Date = new(2025, 9, 1) }
        };
        var points = SoilHistoryTrend.Points(history, "Cec");
        Assert.Equal([0, 1, 2], points.Select(point => point.Round));
        Assert.Equal(new double?[] { 0, null, 12 }, points.Select(point => point.Value));
        Assert.Equal([3, 1, 2], history.Select(round => round.SurveyId)); // Rendering must not mutate the saved document.
    }

    [Fact]
    public void Fewer_than_two_finite_observations_leave_the_trend_blank()
    {
        var history = new List<SoilReportHistoryRound>
        {
            new() { Averages = new() { ["CEC"] = double.NaN } },
            new() { Averages = new() { ["CEC"] = double.PositiveInfinity } },
            new() { Averages = new() { ["CEC"] = 0 } }
        };
        Assert.Empty(SoilHistoryTrend.Points(history, "CEC"));
        Assert.Empty(SoilHistoryTrend.Points(history, "Unknown"));
        Assert.Empty(SoilHistoryTrend.Points([], "CEC"));
    }

    [Fact]
    public void Flat_series_and_same_day_surveys_keep_their_individual_observations()
    {
        var history = new List<SoilReportHistoryRound>
        {
            new() { SurveyId = 2, Date = new(2026, 9, 1), Averages = new() { ["CEC"] = 5, ["Other"] = 2 } },
            new() { SurveyId = 1, Date = new(2026, 9, 1), Averages = new() { ["CEC"] = 5, ["Other"] = 1 } }
        };
        Assert.Equal(new double?[] { 5, 5 }, SoilHistoryTrend.Points(history, "CEC").Select(point => point.Value));
        Assert.Equal(new double?[] { 1, 2 }, SoilHistoryTrend.Points(history, "Other").Select(point => point.Value));
    }
}
