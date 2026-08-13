using ApiModels;

namespace MetaGrow.Shared;

public static class IncompleteSurveyFilter
{
    public static IReadOnlyList<IncompleteSurveySummaryDto> Apply(
        IEnumerable<IncompleteSurveySummaryDto> surveys,
        string? propertySearch,
        IncompleteSurveyArea? area,
        int? statusId,
        int? agronomistId)
    {
        ArgumentNullException.ThrowIfNull(surveys);

        // The endpoint already excludes completed rows; keep the UI fail-safe if a future
        // database change accidentally returns one.
        var query = surveys.Where(survey => survey.StatusId != MultiCropSurveyStatus.Complete);
        if (!string.IsNullOrWhiteSpace(propertySearch))
        {
            var search = propertySearch.Trim();
            query = query.Where(survey =>
                survey.PropertyName.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        if (area.HasValue)
            query = query.Where(survey => survey.Area == area.Value);

        if (statusId.HasValue)
            query = query.Where(survey => survey.StatusId == statusId.Value);

        if (agronomistId.HasValue)
            query = query.Where(survey => survey.AgronomistId == agronomistId.Value);

        return query
            .OrderBy(survey => survey.WorkflowPriority)
            .ThenBy(survey => survey.SurveyDate)
            .ThenBy(survey => survey.PropertyName)
            .ThenBy(survey => survey.Area)
            .ThenBy(survey => survey.SurveyId)
            .ToList();
    }

    public static string AreaBadgeClass(IncompleteSurveyArea area) => area switch
    {
        IncompleteSurveyArea.Banana => "area-banana",
        IncompleteSurveyArea.MultiCrop => "area-multicrop",
        _ => "area-other"
    };
}

public static class IncompleteSurveyNavigation
{
    public static string EditUrl(IncompleteSurveySummaryDto survey) =>
        $"/surveys/{AreaSegment(survey.Area)}/{survey.SurveyId}/edit";

    public static string OnlineReportUrl(IncompleteSurveySummaryDto survey) =>
        $"/surveys/{AreaSegment(survey.Area)}/{survey.SurveyId}/report";

    public static string PrintReportUrl(IncompleteSurveySummaryDto survey) =>
        $"{OnlineReportUrl(survey)}?print=true";

    private static string AreaSegment(IncompleteSurveyArea area) => area switch
    {
        IncompleteSurveyArea.Banana => "banana",
        IncompleteSurveyArea.MultiCrop => "multicrop",
        _ => throw new ArgumentOutOfRangeException(nameof(area), area, "Unsupported survey area.")
    };
}
