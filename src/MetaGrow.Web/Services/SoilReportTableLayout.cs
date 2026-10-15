using MetaGrow.Shared.Models;
using MetaGrow.Web.Models;

namespace MetaGrow.Web.Services;

/// <summary>Appends tissue to each soil block group, keeping portrait continuations and all separate samples.</summary>
public static class SoilReportTableLayout
{
    private const int _rowsPerPage = 24;

    public static List<SoilReportTablePage> Pages(SoilReportDocument document)
    {
        var pages = new List<SoilReportTablePage>();
        var samples = TissueReportLayout.IncludedSamples(document);
        foreach (var blocks in document.ReportedBlocks.Chunk(4))
        {
            SoilReportTablePage? page = null;
            var used = 0;
            // Retain the established soil-only pagination, then use spare space for tissue.
            foreach (var measures in (document.Options.IncludeSoil ? document.Measures : []).Chunk(_rowsPerPage))
            {
                page = new(blocks, measures.GroupBy(measure => measure.Section)
                    .Select(group => new SoilReportTableGroup(group.Key, group.First().SectionColour, group.ToList())).ToList());
                pages.Add(page);
                used = measures.Length;
            }
            var relevant = samples.Where(sample => blocks.Any(block => sample.SoilLabIds.Contains(block.LabSoilId))).ToList();
            foreach (var part in relevant.GroupBy(sample => sample.SamplePart.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                // Align like sample parts. A second sample gets a second row set, never an arbitrary first value or average.
                var byBlock = blocks.Select(block => part.Where(sample => sample.SoilLabIds.Contains(block.LabSoilId))
                    .OrderBy(sample => sample.LabTissueId).ToList()).ToList();
                for (var sampleIndex = 0; sampleIndex < byBlock.Max(list => list.Count); sampleIndex++)
                {
                    var columns = byBlock.Select(list => list.ElementAtOrDefault(sampleIndex)).ToArray();
                    var measures = TissueReportLayout.VisibleMeasures(document, columns.OfType<TissueReportSample>());
                    var offset = 0;
                    while (offset < measures.Count)
                    {
                        // Reserve heading and per-block sample references each time a tissue table continues.
                        if (page is null || used + 4 > _rowsPerPage)
                        {
                            page = new(blocks, []); pages.Add(page); used = 0;
                        }
                        var count = Math.Min(measures.Count - offset, _rowsPerPage - used - 3);
                        page.Groups.Add(new("Tissue Nutrients", "#639f43", measures.Skip(offset).Take(count).ToList(), columns));
                        used += count + 3;
                        offset += count;
                    }
                }
            }
        }
        return pages;
    }
}
