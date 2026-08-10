using ApiModels;

namespace MetaGrow.Shared;

public static class MultiCropSurveyFilter
{
    public static IReadOnlyList<MultiCropSurveySummaryDto> Apply(
        IEnumerable<MultiCropSurveySummaryDto> surveys,
        string? propertySearch,
        int? applicationId)
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

        return query
            .OrderByDescending(survey => survey.SurveyDate)
            .ThenByDescending(survey => survey.SurveyId)
            .ToList();
    }
}
