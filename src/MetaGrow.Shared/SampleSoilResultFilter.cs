using ApiModels;

namespace MetaGrow.Shared;

public static class SampleSoilResultFilter
{
    public static IReadOnlyList<SampleSoilResultGroupDto> Apply(
        IEnumerable<SampleSoilResultGroupDto> groups,
        string? propertySearch,
        string? rawCustomerSearch,
        int? agronomistId,
        int? labId,
        string? referenceSearch,
        bool? isFullyMapped)
    {
        ArgumentNullException.ThrowIfNull(groups);
        var query = groups;

        if (!string.IsNullOrWhiteSpace(propertySearch))
        {
            var search = propertySearch.Trim();
            query = query.Where(group => group.PropertyName.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(rawCustomerSearch))
        {
            var search = rawCustomerSearch.Trim();
            query = query.Where(group => group.RawCustomerName.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        if (agronomistId.HasValue)
            query = query.Where(group => group.AgronomistId == agronomistId.Value);

        if (labId.HasValue)
            query = query.Where(group => group.LabId == labId.Value);

        if (!string.IsNullOrWhiteSpace(referenceSearch))
        {
            var search = referenceSearch.Trim();
            query = query.Where(group => group.Results.Any(result =>
                (result.OrderNo?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (result.RawFieldReference?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)));
        }

        if (isFullyMapped.HasValue)
            query = query.Where(group => group.IsFullyMapped == isFullyMapped.Value);

        return query
            .OrderByDescending(group => group.DateReceived)
            .ThenByDescending(group => group.SurveyId)
            .ToList();
    }
}
