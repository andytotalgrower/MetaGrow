namespace MetaGrow.Shared.Models;

/// <summary>A report-only choice; the original survey and laboratory records are never rewritten.</summary>
public sealed class SoilReportManualMatch
{
    public int LabSoilId { get; set; }
    public int SurveyItemId { get; set; }
    public string Reason { get; set; } = "";
    // These fields are assigned by the API, not trusted from the editor.
    public string SourceHash { get; set; } = "";
    public string ConfirmedById { get; set; } = "";
    public string ConfirmedBy { get; set; } = "";
}

/// <summary>Frozen matching evidence belongs to the same immutable revision as the calculation.</summary>
public sealed record SoilReportSampleMatch(int LabSoilId, int SurveyItemId, string BlockName, string Method,
    string Explanation, string ConfirmedById = "", string ConfirmedBy = "");

public sealed record SoilReportMatchChoice(int SurveyItemId, string Label);

public sealed class SoilReportMatchRow
{
    public int LabSoilId { get; set; }
    public string BlockName { get; set; } = "";
    public string Laboratory { get; set; } = "";
    public string Reference { get; set; } = "";
    public int SurveyItemId { get; set; }
    public int AutomaticSurveyItemId { get; set; }
    public bool IsManual { get; set; }
    public bool RequiresReview { get; set; }
    public string Method { get; set; } = "";
    public string Message { get; set; } = "";
    // Keep the original evidence so undoing a manual choice restores an accurate explanation.
    public string AutomaticMessage { get; set; } = "";
    public string Reason { get; set; } = "";
    public List<SoilReportMatchChoice> Choices { get; set; } = [];
}

public sealed class SoilReportMatchingReview
{
    public long Version { get; set; }
    public string InputHash { get; set; } = "";
    public bool CanEdit { get; set; }
    public List<SoilReportMatchRow> Rows { get; set; } = [];
    public List<string> Issues { get; set; } = [];
    public bool RequiresReview => Issues.Count > 0 || Rows.Any(row => row.RequiresReview);
}

public sealed class SoilReportMatchingChange
{
    public long ExpectedVersion { get; set; }
    public string ExpectedInputHash { get; set; } = "";
    public List<SoilReportManualMatch> Items { get; set; } = [];
}
