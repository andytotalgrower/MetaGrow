using ApiModels;

namespace MetaGrow.Shared;

public static class BananaSurveyFilter
{
    public static IReadOnlyList<BananaSurveySummaryDto> Apply(
        IEnumerable<BananaSurveySummaryDto> surveys,
        string? propertySearch,
        int? statusId)
    {
        ArgumentNullException.ThrowIfNull(surveys);

        var query = surveys;
        if (!string.IsNullOrWhiteSpace(propertySearch))
        {
            var search = propertySearch.Trim();
            query = query.Where(survey =>
                survey.PropertyName.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        if (statusId.HasValue)
            query = query.Where(survey => survey.StatusId == statusId.Value);

        return query
            .OrderByDescending(survey => MultiCropSurveyFilter.RequiresAction(survey.StatusName))
            .ThenByDescending(survey => survey.SurveyDate)
            .ThenByDescending(survey => survey.SurveyId)
            .ToList();
    }
}
