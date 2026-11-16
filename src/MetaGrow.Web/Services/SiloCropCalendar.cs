using ApiModels;

namespace MetaGrow.Web.Services;

internal static class SiloCropCalendar
{
    // ABS reports July–June yields by the year the period ends.
    public static int GetReportingYear(DateTime date, int startMonth) =>
        startMonth > 1 && date.Month >= startMonth ? date.Year + 1 : date.Year;

    public static string FormatReportingYear(int year, int startMonth) => startMonth == 1
        ? year.ToString()
        : $"{year - 1}–{year % 100:00}";

    // Label a season by its closing year so a pre-harvest growing period can share the harvest year.
    public static int? GetSeasonYear(SiloCropSeasonDto window, DateTime date)
    {
        if (!window.CrossesYear)
            return date.Month >= window.StartMonth && date.Month <= window.EndMonth ? date.Year : null;

        if (date.Month >= window.StartMonth)
            return date.Year + 1;

        return date.Month <= window.EndMonth ? date.Year : null;
    }

    public static (DateTime From, DateTime To) ClipSeason(SiloCropSeasonDto window, int seasonYear,
        DateTime from, DateTime to)
    {
        // The saved month bounds include the whole first and last month, including leap days.
        var start = new DateTime(seasonYear - (window.CrossesYear ? 1 : 0), window.StartMonth, 1);
        var end = new DateTime(seasonYear, window.EndMonth, DateTime.DaysInMonth(seasonYear, window.EndMonth));
        return (start < from ? from : start, end > to ? to : end);
    }

    public static bool SeasonOverlaps(SiloCropSeasonDto window, int seasonYear, DateTime from, DateTime to)
    {
        // An annual yield belongs to the phase if any date in that harvest-year window is selected.
        var (start, end) = ClipSeason(window, seasonYear, from, to);
        return start <= end;
    }
}
