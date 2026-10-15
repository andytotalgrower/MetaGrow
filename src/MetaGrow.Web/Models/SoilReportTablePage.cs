using MetaGrow.Shared.Models;

namespace MetaGrow.Web.Models;

/// <summary>Soil and tissue share block columns, while their distinct sources and ranges remain separate.</summary>
public sealed record SoilReportTablePage(SoilReportBlock[] Blocks, List<SoilReportTableGroup> Groups);
public sealed record SoilReportTableGroup(string Title, string Colour, List<SoilReportMeasure> Measures,
    TissueReportSample?[]? Samples = null)
{
    public bool IsTissue => Samples is not null;
    public SoilReportValue? Value(SoilReportBlock block, int index, string key) =>
        IsTissue ? Samples![index]?.Values.GetValueOrDefault(key) : block.Values.GetValueOrDefault(key);
}

/// <summary>A matrix row always links back to a soil block; repeated tissue samples retain their own rows.</summary>
public sealed record SoilStatusRow(SoilReportBlock Block, TissueReportSample? Tissue = null, bool IsTissue = false)
{
    public SoilReportValue? Value(string key) => IsTissue ? Tissue?.Values.GetValueOrDefault(key) : Block.Values.GetValueOrDefault(key);
}
public sealed record SoilStatusMatrix(bool IsTissue, List<SoilStatusRow> Rows, List<SoilReportMeasure> Measures,
    Dictionary<string, double> Widths, int RowStart, int TotalRows, int MeasureStart, int TotalMeasures)
{
    // Allow for rotated labels, title, notes and totals. Both matrices show compact block names only.
    public double EstimatedHeight => 65 + Rows.Sum(row => 5.5 + Math.Max(0, row.Block.Name.Length - 20) / 20d * 3.5)
        + Math.Max(0, Measures.Select(measure => (string.IsNullOrWhiteSpace(measure.ShortName) ? measure.Label : measure.ShortName).Length)
            .DefaultIfEmpty(0).Max() - 24) * 1.4;
}
