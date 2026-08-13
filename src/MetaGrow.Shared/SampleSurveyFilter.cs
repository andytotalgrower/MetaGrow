using ApiModels;

namespace MetaGrow.Shared;

public static class SampleSurveyFilter
{
    public const string MetagenCategory = "Metagen";
    public const string NutritionCategory = "Nutrition";

    public static IReadOnlyList<SampleSurveySummaryDto> Apply(
        IEnumerable<SampleSurveySummaryDto> surveys,
        string? propertySearch,
        int? agronomistId,
        string? category,
        string? workflow,
        int? labId,
        int? testTypeId)
    {
        ArgumentNullException.ThrowIfNull(surveys);
        var query = surveys;

        if (!string.IsNullOrWhiteSpace(propertySearch))
        {
            var search = propertySearch.Trim();
            query = query.Where(survey =>
                survey.PropertyName.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        if (agronomistId.HasValue)
            query = query.Where(survey => survey.AgronomistId == agronomistId.Value);

        if (string.Equals(category, MetagenCategory, StringComparison.OrdinalIgnoreCase))
            query = query.Where(survey => survey.HasMetagenSamples);
        else if (string.Equals(category, NutritionCategory, StringComparison.OrdinalIgnoreCase))
            query = query.Where(survey => survey.HasNutritionSamples);

        if (!string.IsNullOrWhiteSpace(workflow))
        {
            query = query.Where(survey =>
                string.Equals(survey.MetagenWorkflowName, workflow, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(survey.NutritionWorkflowName, workflow, StringComparison.OrdinalIgnoreCase));
        }

        if (labId.HasValue)
            query = query.Where(survey => survey.Laboratories.Any(item => item.Id == labId.Value));

        if (testTypeId.HasValue)
            query = query.Where(survey => survey.TestTypes.Any(item => item.Id == testTypeId.Value));

        return query
            .OrderByDescending(RequiresAction)
            .ThenByDescending(survey => survey.SurveyDate)
            .ThenByDescending(survey => survey.SurveyId)
            .ToList();
    }

    public static bool RequiresAction(SampleSurveySummaryDto survey) =>
        IsActionable(survey.MetagenWorkflowName) || IsActionable(survey.NutritionWorkflowName);

    public static string StatusBadgeClass(string? status) =>
        status?.Trim().ToLowerInvariant() switch
        {
            "in transit" => "status-in-transit",
            "at lab" => "status-at-lab",
            "ready for agronomist" => "status-ready",
            "awaiting qa" => "status-awaiting-qa",
            "qa complete, ready to send" => "status-ready-to-send",
            "complete, sent to client" => "status-completed",
            _ => "status-in-progress"
        };

    private static bool IsActionable(string? status) =>
        !string.IsNullOrWhiteSpace(status) &&
        !status.Contains("complete, sent", StringComparison.OrdinalIgnoreCase);
}
