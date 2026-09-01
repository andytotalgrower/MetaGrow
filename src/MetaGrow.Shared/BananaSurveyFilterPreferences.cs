namespace MetaGrow.Shared;

public sealed record BananaSurveyFilterPreferences(
    DateTime StartDate,
    DateTime EndDate,
    string? PropertySearch,
    int? StatusId)
{
    public static BananaSurveyFilterPreferences CreateDefault(DateTime today)
    {
        var date = today.Date;
        return new(date.AddMonths(-3), date, null, null);
    }

    public bool HasValidDateRange =>
        StartDate != default &&
        EndDate != default &&
        EndDate.Date >= StartDate.Date;
}
