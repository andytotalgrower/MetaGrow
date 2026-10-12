using ApiModels;
using System.Globalization;

namespace MetaGrow.Web.Services;

public static class ReportChartLayout
{
    // Use the same distinct, high-contrast colours online and on paper.
    public static readonly string[] Palette = [
        "#1764a0", "#c25100", "#00815f", "#9b4584", "#6550ad", "#1684a0",
        "#875237", "#a27700", "#427a28", "#c42b37", "#665d16", "#a35d9c",
        "#424b55", "#087d86", "#b24c68", "#77791d"
    ];

    public static bool IsAreaAverage(string name) =>
        string.Equals(name.Trim(), "Area average", StringComparison.OrdinalIgnoreCase);

    public static string SurveyTitle(string? title) => title?.StartsWith("TGS ", StringComparison.OrdinalIgnoreCase) == true
        ? title[4..].TrimStart() : title ?? string.Empty;

    public static IEnumerable<string> ObservationLines(string text) => text
        .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(line => line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal)
            ? line[2..].TrimStart() : line.TrimStart('•').TrimStart())
        .Where(line => line.Length > 0);

    public static IReadOnlyList<(string Name, int Count)> SummaryGroups(IReadOnlyList<MultiCropReportMetricValueDto> metrics)
    {
        var groups = new List<(string Name, int Count)>();
        // Span only neighbouring columns: overflow stays after all legacy columns.
        foreach (var metric in metrics)
        {
            var name = string.IsNullOrWhiteSpace(metric.SummaryGroup) ? "Measurements" : metric.SummaryGroup;
            if (groups.Count > 0 && groups[^1].Name == name)
                groups[^1] = (name, groups[^1].Count + 1);
            else
                groups.Add((name, 1));
        }
        return groups;
    }

    // The API groups current values by block. The legacy report instead puts
    // blocks on the axis and measurements in the legend. Transpose after block
    // groups are selected so the chart and summary use the same block identities.
    public static MultiCropReportSectionDto WithBlocksOnAxis(MultiCropReportSectionDto section)
    {
        if (section.IsHistorical) return section;
        return new MultiCropReportSectionDto
        {
            Code = section.Code,
            Title = section.Title,
            Metrics = section.Series.Select((block, index) => new MultiCropReportMetricDefinitionDto
            {
                MetricCode = index.ToString(CultureInfo.InvariantCulture),
                MetricLabel = block.Name,
                SortOrder = index
            }).ToList(),
            Series = section.Metrics.OrderBy(metric => metric.SortOrder).Select(metric => new MultiCropReportSeriesDto
            {
                Name = metric.MetricLabel,
                Points = section.Series.Select((block, index) => new MultiCropReportPointDto
                {
                    MetricCode = index.ToString(CultureInfo.InvariantCulture),
                    MetricLabel = block.Name,
                    Value = block.Points.FirstOrDefault(point => string.Equals(point.MetricCode, metric.MetricCode,
                        StringComparison.OrdinalIgnoreCase))?.Value
                }).ToList()
            }).ToList()
        };
    }

    public static string BananaValue(double? value, string column) => value?.ToString(column switch
    {
        "SE" or "Rating" => "#,##0",
        "Prod" or "StemDecay" or "BunchFingerCount" or "PinkMealyBug1" or "PinkMealyBug2" => "#,##0.0",
        _ => "#,##0.00"
    }, CultureInfo.GetCultureInfo("en-AU")) ?? "—";
}
