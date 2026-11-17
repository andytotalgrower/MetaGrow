namespace MetaGrow.Web.Services;

/// <summary>Shared print choices and page geometry for banana and multi-crop photos.</summary>
public static class ReportPhotoPrinting
{
    public const string _none = "none";
    public const string _bestFit = "best-fit";
    public static IReadOnlyList<KeyValuePair<string, string>> Options { get; } =
    [
        new(_none, "No photos in print"),
        new(_bestFit, "Best fit"),
        new("1", "1 column"),
        new("2", "2 columns"),
        new("3", "3 columns")
    ];

    public static string Normalize(string? choice) =>
        Options.Any(option => option.Key == choice) ? choice! : _none;

    public static string PrintStyle(string choice, int photoCount)
    {
        // Best fit enlarges small sets instead of leaving most of a three-column grid empty.
        var columns = Normalize(choice) switch
        {
            "1" => 1,
            "2" => 2,
            "3" => 3,
            _ => photoCount <= 2 ? Math.Max(1, photoCount) : photoCount == 4 ? 2 : 3
        };
        var rows = photoCount <= columns || columns == 1 ? 1 : 2;
        // Reserve space for the section heading, captions, and page footer on landscape A4.
        return $"--print-photo-columns:{columns};--print-photo-rows:{rows};--print-photo-height:{(rows == 1 ? 126 : 58)}mm";
    }
}
