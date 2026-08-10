using ApiModels;

namespace MetaGrow.Shared;

public static class MultiCropSurveyFilter
{
    public static IReadOnlyList<MultiCropSurveySummaryDto> Apply(
        IEnumerable<MultiCropSurveySummaryDto> surveys,
        string? propertySearch,
        int? applicationId,
        int? statusId = null)
    {
        ArgumentNullException.ThrowIfNull(surveys);

        var query = surveys;
        if (!string.IsNullOrWhiteSpace(propertySearch))
        {
            var search = propertySearch.Trim();
            query = query.Where(survey =>
                survey.PropertyName.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        if (applicationId.HasValue)
            query = query.Where(survey => survey.ApplicationId == applicationId.Value);

        if (statusId.HasValue)
            query = query.Where(survey => survey.StatusId == statusId.Value);

        // Surveys that still need work float to the top of the list.
        return query
            .OrderByDescending(survey => RequiresAction(survey.StatusName))
            .ThenByDescending(survey => survey.SurveyDate)
            .ThenByDescending(survey => survey.SurveyId)
            .ToList();
    }

    /// <summary>True for statuses where people still need to work on the survey.</summary>
    public static bool RequiresAction(string? statusName) =>
        StatusBadgeClass(statusName) is "status-in-progress" or "status-awaiting-qa";

    /// <summary>Maps a status name to the CSS badge class used by the finder screens.</summary>
    public static string StatusBadgeClass(string? statusName)
    {
        if (string.IsNullOrWhiteSpace(statusName))
            return "status-other";

        var name = statusName.Trim();
        if (name.Contains("qa", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("review", StringComparison.OrdinalIgnoreCase))
            return "status-awaiting-qa";
        if (name.Contains("progress", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("draft", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("open", StringComparison.OrdinalIgnoreCase))
            return "status-in-progress";
        if (name.Contains("complete", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("finalised", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("finalized", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("published", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("sent", StringComparison.OrdinalIgnoreCase))
            return "status-completed";
        if (name.Contains("cancel", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("abandon", StringComparison.OrdinalIgnoreCase))
            return "status-cancelled";

        return "status-other";
    }
}
