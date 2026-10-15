using System.Globalization;
using MetaGrow.Shared.Models;
using MetaGrow.Web.Models;

namespace MetaGrow.Web.Services;

/// <summary>Packs compact soil and tissue matrices together while preserving portrait width and height.</summary>
public static class SoilStatusOverviewLayout
{
    private const int _rowsPerMatrix = 25;
    private const int _measuresPerMatrix = 20;
    // 32 mm for labels plus 20.5 × 7.5 mm fits the 186 mm print area.
    private const double _widthBudget = 20.5;
    private const double _pageHeightBudget = 225;

    public static List<List<SoilStatusMatrix>> Pages(SoilReportDocument document)
    {
        var blocks = document.ReportedBlocks;
        var matrices = Matrices(blocks.Select(block => new SoilStatusRow(block)).ToList(),
            document.Options.IncludeSoil ? document.Measures : [], false);
        var samples = TissueReportLayout.IncludedSamples(document);
        var tissueRows = new List<SoilStatusRow>();
        // Multiple soil depths share a tissue sample. Show it once against the first included soil block.
        foreach (var group in blocks.GroupBy(block => (block.BlockId, block.Name.Trim().ToUpperInvariant())))
        {
            var matches = samples.Where(sample => group.Any(block => sample.SoilLabIds.Contains(block.LabSoilId)))
                .OrderBy(sample => sample.LabTissueId).ToList();
            // Uncollected tissue must not create blank matrix rows; soil coverage stays unchanged.
            tissueRows.AddRange(matches.Select(sample => new SoilStatusRow(group.First(), sample, true)));
        }
        matrices.AddRange(Matrices(tissueRows, TissueReportLayout.VisibleMeasures(document, samples), true));
        var pages = new List<List<SoilStatusMatrix>>();
        foreach (var matrix in matrices)
        {
            // Only join the transition from soil to tissue; soil continuation ordering remains unchanged.
            if (matrix.IsTissue && pages.LastOrDefault() is { Count: 1 } previous && !previous[0].IsTissue
                && previous[0].EstimatedHeight + matrix.EstimatedHeight <= _pageHeightBudget)
                previous.Add(matrix);
            else pages.Add([matrix]);
        }
        return pages;
    }

    private static List<SoilStatusMatrix> Matrices(List<SoilStatusRow> rows, List<SoilReportMeasure> definitions, bool tissue)
    {
        var measures = definitions.Where(measure => rows.Any(row => row.Value(measure.Key)?.Target is not null))
            .GroupBy(measure => measure.Section).SelectMany(group => group).ToList();
        var widths = measures.ToDictionary(measure => measure.Key, measure =>
        {
            // Size against all rows so every continuation uses the same widths, including long numeric readings.
            var length = rows.Select(row => row.Value(measure.Key)?.Value?.ToString(measure.NumberFormat, CultureInfo.InvariantCulture).Length ?? 0)
                .DefaultIfEmpty(0).Max();
            return Math.Min(_widthBudget, 1 + Math.Max(0, length - 4) * .3);
        });
        var pages = new List<SoilStatusMatrix>();
        var rowsPerMatrix = _rowsPerMatrix;
        for (var rowStart = 0; rowStart < rows.Count; rowStart += rowsPerMatrix)
        {
            var rowPage = rows.Skip(rowStart).Take(rowsPerMatrix).ToList();
            for (var start = 0; start < measures.Count;)
            {
                var pageMeasures = new List<SoilReportMeasure>();
                var used = 0d;
                foreach (var measure in measures.Skip(start))
                {
                    if (pageMeasures.Count > 0 && (pageMeasures.Count == _measuresPerMatrix || used + widths[measure.Key] > _widthBudget)) break;
                    pageMeasures.Add(measure); used += widths[measure.Key];
                }
                pages.Add(new(tissue, rowPage, pageMeasures, widths, rowStart, rows.Count, start, measures.Count));
                start += pageMeasures.Count;
            }
        }
        return pages;
    }
}
