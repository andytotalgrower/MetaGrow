using MetaGrow.Shared.Models;
using MetaGrow.Web.Models;

namespace MetaGrow.Web.Services;

/// <summary>Prepares full-period trends from saved farm averages without recalculating or changing report data.</summary>
public static class SoilHistoryTrend
{
    public static List<SoilHistoryTrendPoint> Points(IEnumerable<SoilReportHistoryRound> history, string measureKey)
    {
        // Equal spacing represents sampling rounds, matching the adjacent columns. Retain missing rounds
        // so the sparkline breaks instead of implying a zero result or interpolating across unavailable data.
        var points = history.OrderBy(round => round.Date).ThenBy(round => round.SurveyId)
            .Select((round, index) => new SoilHistoryTrendPoint(index,
                round.Averages.TryGetValue(measureKey, out var value) && double.IsFinite(value) ? value : null)).ToList();
        // A single observation cannot show a trend. Measured zeros and flat series are still meaningful.
        return points.Count(point => point.Value.HasValue) >= 2 ? points : [];
    }
}
