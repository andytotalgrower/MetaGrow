using ApiModels;
using MetaGrow.Web.Services;
using Xunit;

namespace MetaGrow.Web.Tests;

public class SiloCropCalendarTests
{
    [Fact]
    public void BananaYieldUsesTheFinancialYearEndingInJune()
    {
        // A date in July belongs with the next ABS yield year, even in a single-calendar-year selection.
        Assert.Equal(2024, SiloCropCalendar.GetReportingYear(new DateTime(2024, 6, 30), 7));
        Assert.Equal(2025, SiloCropCalendar.GetReportingYear(new DateTime(2024, 7, 1), 7));
        Assert.Equal("2023–24", SiloCropCalendar.FormatReportingYear(2024, 7));
    }

    [Theory]
    [InlineData(2025, 10, 31, null)]
    [InlineData(2025, 11, 1, 2026)]
    [InlineData(2025, 12, 31, 2026)]
    [InlineData(2026, 1, 1, 2026)]
    [InlineData(2026, 4, 30, 2026)]
    [InlineData(2026, 5, 1, null)]
    public void CrossYearGrowingWindowUsesItsClosingYear(int year, int month, int day, int? seasonYear)
    {
        var window = new SiloCropSeasonDto(1, 1, "Sugar", "Growing", 11, 4, true, "", null);

        Assert.Equal(seasonYear, SiloCropCalendar.GetSeasonYear(window, new DateTime(year, month, day)));
    }

    [Fact]
    public void PartialSeasonIncludesLeapDayAndStartsAtTheSelectedDate()
    {
        var window = new SiloCropSeasonDto(1, 1, "Melon", "Harvest", 12, 2, true, "", null);
        var from = new DateTime(2024, 1, 15);

        // January and February belong to the same crop season as the preceding December.
        Assert.Equal((from, new DateTime(2024, 2, 29)),
            SiloCropCalendar.ClipSeason(window, 2024, from, new DateTime(2024, 3, 15)));
    }

    [Fact]
    public void HarvestWindowKeepsItsCalendarYearAndHonoursSelectedEndDate()
    {
        var window = new SiloCropSeasonDto(1, 1, "Sugar", "Harvest", 6, 11, false, "", null);
        var to = new DateTime(2026, 7, 15);

        Assert.Equal(2026, SiloCropCalendar.GetSeasonYear(window, to));
        Assert.Null(SiloCropCalendar.GetSeasonYear(window, new DateTime(2026, 12, 1)));
        Assert.Equal((new DateTime(2026, 6, 1), to),
            SiloCropCalendar.ClipSeason(window, 2026, new DateTime(2026, 1, 1), to));
    }

    [Fact]
    public void YieldYearMatchesItsGrowingWindowEvenWhenSelectedDatesAreInThePreviousYear()
    {
        var window = new SiloCropSeasonDto(1, 1, "Sugar", "Growing", 11, 4, true, "", null);
        var selectedDate = new DateTime(2024, 11, 1);

        // November 2024 is part of the 2025 growing window and lies outside the 2024 window.
        Assert.True(SiloCropCalendar.SeasonOverlaps(window, 2025, selectedDate, selectedDate));
        Assert.False(SiloCropCalendar.SeasonOverlaps(window, 2024, selectedDate, selectedDate));
    }
}
