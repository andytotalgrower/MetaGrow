using MetaGrow.Shared.Models;
using MetaGrow.Web.Models;

namespace MetaGrow.Web.Services;

/// <summary>Builds compact portrait pages without recalculating recommendations or consulting live definitions.</summary>
public static class SoilBlockDetailLayout
{
    // Conservative millimetre estimates reserve space for the shared header, block identity and print footer.
    private const double _pageBudget = 175;

    public static IReadOnlyList<SoilReportMeasure> Measures(SoilReportDocument document, SoilReportBlock block)
    {
        var definition = document.DefinitionSnapshot.FirstOrDefault(item => item.Key.Equals(block.Definition, StringComparison.OrdinalIgnoreCase));
        // Earlier snapshots can fall back to their saved measure list; never load today's maintained definitions.
        var measures = definition is null ? document.Measures : definition.Sections.Where(section => section.SampleSurveyVisible)
            .OrderBy(section => section.Order).ThenBy(section => section.Id)
            .SelectMany(section => section.Items.OrderBy(item => item.Order).ThenBy(item => item.Id).Select(item => new SoilReportMeasure
            {
                Key = item.Field, Label = item.Label, ShortName = item.ShortName, Section = section.Name,
                SectionColour = section.Colour, SectionOrder = section.Order, Order = item.Order,
                NumberFormat = item.NumberFormat, ShowIfZero = item.ShowIfZero
            })).ToList();
        return measures.DistinctBy(measure => measure.Key, StringComparer.OrdinalIgnoreCase)
            .Where(measure => measure.ShowIfZero || block.Values.TryGetValue(measure.Key, out var value) && value.Value > 0).ToList();
    }

    public static Dictionary<string, double> SurveyAverages(SoilReportDocument document)
    {
        // Match Historical Farm Averages: every sampled record, including excluded recommendations, has equal weight.
        return document.Blocks.SelectMany(block => block.Values).Where(pair => pair.Value.Value is { } value && double.IsFinite(value))
            .GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Average(pair => pair.Value.Value!.Value), StringComparer.OrdinalIgnoreCase);
    }

    public static bool HasRange(SoilReportValue? value) => value?.Minimum is { } min && value.Maximum is { } max
        && double.IsFinite(min) && double.IsFinite(max) && max > min && double.IsFinite(max - min);

    public static SoilRangeMarker? Marker(double? number, SoilReportValue? range)
    {
        if (number is not { } value || !double.IsFinite(value) || !HasRange(range)) return null;
        // The middle half represents the range. Clipped extremes retain their numeric reading and an arrow.
        var position = 25 + (value - range!.Minimum!.Value) / (range.Maximum!.Value - range.Minimum.Value) * 50;
        return new(Math.Clamp(position, 2, 98), position < 2 ? "left" : position > 98 ? "right" : "");
    }

    public static List<SoilBlockDetailPage> Pages(SoilReportDocument document, SoilReportBlock block)
    {
        var measures = document.Options.IncludeSoil ? Measures(document, block) : [];
        var summaries = measures.Where(measure => !block.Values.TryGetValue(measure.Key, out var value)
            || value.Target is null && value.Minimum is null && value.Maximum is null).ToList();
        var summaryKeys = summaries.Select(measure => measure.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pages = new List<SoilBlockDetailPage> { new() };
        var page = pages[0];
        var used = 0d;
        void Reserve(double height)
        {
            if (used > 0 && used + height > _pageBudget) { page = new(); pages.Add(page); used = 0; }
            used += height;
        }
        // Match the six-column summary grid on portrait pages, allowing extra lines for narrower labels.
        foreach (var row in summaries.Chunk(6))
        {
            Reserve(17 + row.Max(measure => Math.Max(0, measure.Label.Length - 20) / 20d) * 4);
            page.Summaries.AddRange(row);
        }
        foreach (var row in block.Recommendations.Where(item => document.Options.IncludeSoil && item.Rate > 0).OrderBy(item => item.Order).ThenBy(item => item.Product).Chunk(2))
        {
            Reserve(14 + row.Max(item => Math.Max(0, item.Product.Length - 35) / 35d) * 4);
            page.Recommendations.AddRange(row);
        }
        if (document.Options.IncludeSoil && block.Recommendations.All(item => item.Rate <= 0)) Reserve(10);
        foreach (var measure in measures.Where(measure => !summaryKeys.Contains(measure.Key)))
        {
            // Repeated table/section headings are included in the budget on continuation pages.
            var rowHeight = 6 + Math.Max(0, measure.Label.Length - 30) / 30d * 4;
            var sectionHeight = page.Measures.LastOrDefault()?.Section == measure.Section ? 0 : 6;
            if (used > 0 && used + rowHeight + sectionHeight + (page.Measures.Count == 0 ? 16 : 0) > _pageBudget)
            {
                page = new(); pages.Add(page); used = 0;
                sectionHeight = 6;
            }
            Reserve(rowHeight + sectionHeight + (page.Measures.Count == 0 ? 16 : 0));
            page.Measures.Add(measure);
        }
        // Tissue follows the soil readings within this block's detail pages, using the same portrait height budget.
        foreach (var tissue in TissueReportLayout.IncludedSamples(document).Where(sample => sample.SoilLabIds.Contains(block.LabSoilId)))
            foreach (var measure in TissueReportLayout.VisibleMeasures(document, [tissue]))
            {
                var height = 7 + Math.Max(0, measure.Label.Length - 30) / 30d * 4;
                // Reserve sample identity, the four-column heading and scale explanation.
                var heading = page.Tissue.LastOrDefault()?.Sample.LabTissueId == tissue.LabTissueId ? 0 : 27;
                if (used > 0 && used + height + heading > _pageBudget) { page = new(); pages.Add(page); used = 0; heading = 27; }
                Reserve(height + heading);
                page.Tissue.Add(new(tissue, measure));
            }
        var notes = block.Recommendations.Where(item => item.Overridden).Select(item => new SoilBlockDetailNote(
                item.Product + " — agronomist adjustment", FormattableString.Invariant($"{item.CalculatedRate:#,##0.##} → {item.Rate:#,##0.##} kg/ha. {item.Reason}")))
            .Concat(document.Notes.Where(note => note.LabSoilIds.Contains(block.LabSoilId) && note.RuleId != "disclaimer")
                .Select(note => new SoilBlockDetailNote(note.Title, note.Text)));
        // The shared print footer carries the disclaimer on every page; preserve all other applicable guidance here.
        // These notes concern soil calculations and applications; a tissue-only view retains the shared disclaimer.
        foreach (var note in document.Options.IncludeSoil ? notes : [])
        {
            var lines = WrapLines(note.Text, 100).ToList();
            foreach (var part in lines.Chunk(6).Select((lines, index) => (Lines: lines, Index: index)))
            {
                Reserve(10 + part.Lines.Length * 4);
                page.Notes.Add(new(note.Title + (part.Index > 0 ? " (continued)" : ""), string.Join("\n", part.Lines)));
            }
        }
        return pages;
    }

    private static IEnumerable<string> WrapLines(string text, int width)
    {
        // Split long advice into bounded pieces at whitespace, retaining every character and explicit paragraph break.
        foreach (var paragraph in text.Replace("\r\n", "\n").Split('\n'))
        {
            var remaining = paragraph;
            while (remaining.Length > width)
            {
                var end = remaining.LastIndexOf(' ', width, width);
                if (end <= 0) end = width;
                yield return remaining[..end];
                remaining = remaining[end..];
            }
            yield return remaining;
        }
    }
}
