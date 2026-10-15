using MetaGrow.Shared.Models;

namespace MetaGrow.Web.Services;

/// <summary>Shared presentation rules for tissue readings in the saved soil report.</summary>
public static class TissueReportLayout
{
    public static List<TissueReportSample> IncludedSamples(SoilReportDocument document)
    {
        if (!document.Options.IncludeTissue) return [];
        var soilIds = document.Blocks.Where(block => !block.Excluded).Select(block => block.LabSoilId).ToHashSet();
        return document.Tissue.Samples.Where(sample => sample.SoilLabIds.Any(soilIds.Contains)).ToList();
    }

    public static List<SoilReportMeasure> VisibleMeasures(SoilReportDocument document, IEnumerable<TissueReportSample> samples)
    {
        // Whole rows of zeroes represent unreported tissue tests. Preserve individual zeroes when
        // another sample has a measured value, without changing the frozen source or audit history.
        var reportedKeys = samples.SelectMany(sample => sample.Values)
            .Where(pair => pair.Value.Value is { } value && double.IsFinite(value) && value != 0)
            .Select(pair => pair.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return document.Tissue.Measures.Where(measure => reportedKeys.Contains(measure.Key)).ToList();
    }
}
