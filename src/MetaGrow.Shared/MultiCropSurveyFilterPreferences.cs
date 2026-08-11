namespace MetaGrow.Shared;

public sealed record MultiCropSurveyFilterPreferences(
    DateTime StartDate,
    DateTime EndDate,
    string? PropertySearch,
    int? ApplicationId,
    int? StatusId)
{
    public static MultiCropSurveyFilterPreferences CreateDefault(DateTime today)
    {
        var date = today.Date;
        return new(date.AddMonths(-2), date, null, null, null);
    }

    public bool HasValidDateRange =>
        StartDate != default &&
        EndDate != default &&
        EndDate.Date >= StartDate.Date;
}
