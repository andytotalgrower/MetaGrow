using MetaGrow.Shared.Models;

namespace MetaGrow.Web.Models;

/// <summary>Presentation-only page groups; their values always come from the saved report revision.</summary>
public sealed class SoilBlockDetailPage
{
    public List<SoilReportMeasure> Summaries { get; } = [];
    public List<SoilReportRecommendation> Recommendations { get; } = [];
    public List<SoilReportMeasure> Measures { get; } = [];
    public List<SoilBlockDetailNote> Notes { get; } = [];
    public List<TissueBlockDetailRow> Tissue { get; } = [];
}

public sealed record SoilBlockDetailNote(string Title, string Text);
public sealed record SoilRangeMarker(double Position, string Overflow);
public sealed record TissueBlockDetailRow(TissueReportSample Sample, SoilReportMeasure Measure);
