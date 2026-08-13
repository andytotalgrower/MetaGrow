namespace MetaGrow.Shared;

public sealed record SampleSurveyFilterPreferences(
    DateTime StartDate,
    DateTime EndDate,
    string? PropertySearch,
    int? AgronomistId,
    string? Category,
    string? Workflow,
    int? LabId,
    int? TestTypeId)
{
    public static SampleSurveyFilterPreferences CreateDefault(DateTime today)
    {
        var date = today.Date;
        return new(date.AddMonths(-3), date, null, null, null, null, null, null);
    }

    public bool HasValidDateRange =>
        StartDate != default &&
        EndDate != default &&
        EndDate.Date >= StartDate.Date;
}
