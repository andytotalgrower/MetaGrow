using ApiModels.MetaGrow;
using MetaGrow.Shared.Models;

namespace MetaGrow.Api.Services.SoilReports;

public sealed partial class SoilReportCalculator
{
    private static void BuildRecommendations(SoilReportSource source, int regionId, SoilReportLabSample sample, string crop,
        List<TargetResult> targets, SoilReportBlock block, SoilReportDocument document)
    {
        foreach (var group in targets.Where(target => !string.IsNullOrWhiteSpace(target.Product)).GroupBy(target => target.Product, StringComparer.OrdinalIgnoreCase))
        {
            var product = source.Products.FirstOrDefault(item => item.ProductName.Equals(group.Key, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"Product settings are missing for {group.Key}.");
            var useMaximum = group.Key.Equals("Lime", StringComparison.OrdinalIgnoreCase) || group.Key.Equals("Dolomite", StringComparison.OrdinalIgnoreCase);
            var amount = useMaximum ? group.Max(target => target.Balance) : group.Sum(target => target.Balance);
            Trace(document.Trace, sample.LabSoilId, useMaximum ? "product-maximum" : "product-sum", group.Key,
                useMaximum ? "Use maximum for lime/dolomite, avoiding duplicated elemental representations." : "Sum contributions for this product.", amount,
                group.Select((target, index) => ($"{target.Rule.ObjectName}[{index}]", target.Balance)).ToArray());
            var nitricRule = crop.StartsWith("sugar", StringComparison.OrdinalIgnoreCase) && group.Key.StartsWith("potassium", StringComparison.OrdinalIgnoreCase);
            if (nitricRule)
            {
                var nitric = targets.FirstOrDefault(target => target.Rule.ObjectName == "NitricPotassiumMeq");
                var rate = nitric is null || nitric.Actual >= 1 ? 0 : nitric.Actual >= .8 ? 40 : nitric.Actual >= .6 ? 60 : 80;
                // Preserve the legacy area multiplication here pending agronomic review. Area is never used for history averages.
                var matchingBlocks = source.Survey.Blocks.Where(item => item.BlockId == block.BlockId
                    && string.Equals(item.BlockName, block.Name, StringComparison.OrdinalIgnoreCase)).ToList();
                var area = matchingBlocks.Count == 1 ? matchingBlocks[0].Size : 0;
                if (rate > 0 && area <= 0) throw new InvalidOperationException($"{block.Name}: the legacy sugar potassium rule requires a valid block area. Confirm the area before calculating this recommendation.");
                amount = rate * area;
                Trace(document.Trace, sample.LabSoilId, "sugar-nitric-potassium", group.Key,
                    "Legacy report multiplies the nitric-potassium band rate by hectares before displaying kg/ha; retained for parity and flagged for review.", amount,
                    ("nitricPotassiumMeq", nitric?.Actual ?? 0), ("bandRate", rate), ("hectares", area));
                document.Warnings.Add($"{block.Name}: legacy sugar potassium calculation uses block hectares. Verify the rate before QA.");
                if (rate > 0) AddNote(document, "nitric-potassium", "Potassium", "Where Nitric Potassium is under target, if this block is near a mill use mill ash/filter press if available to restore levels. Standard potassium programs are generally a minimum of 100 kg/ha but lower can be used in soils with very large reserves. Potassium recommendations are in addition to standard programs.", sample.LabSoilId);
            }
            else
            {
                var original = amount;
                amount = Math.Round(amount, 0);
                if (product.RoundTo is > 0)
                {
                    var units = amount / product.RoundTo.Value;
                    amount = (product.RoundType.ToUpperInvariant() switch { "D" => Math.Floor(units), "U" => Math.Ceiling(units), _ => Math.Round(units, 0) }) * product.RoundTo.Value;
                }
                Trace(document.Trace, sample.LabSoilId, "product-rounding", group.Key, $"Round to whole units, then product increment ({product.RoundType}).", amount,
                    ("unrounded", original), ("increment", product.RoundTo ?? 0));
            }
            if (amount > 0)
            {
                var limits = source.ProductLimits.Where(limit => limit.TemplateId == source.Template.Settings.TemplateId && limit.RegionId == regionId
                    && string.Equals(limit.ProductName, group.Key, StringComparison.OrdinalIgnoreCase)).ToList();
                if (limits.Count > 1) throw new InvalidOperationException($"Multiple product limits match {group.Key} in region {regionId}.");
                if (limits.SingleOrDefault() is { } limit)
                {
                    var original = amount;
                    amount = Math.Clamp(amount, (double)limit.MinAmount, (double)limit.MaxAmount);
                    Trace(document.Trace, sample.LabSoilId, "regional-product-limit", group.Key, "Apply the selected template and region's product limit after rounding.", amount,
                        ("before", original), ("minimum", (double)limit.MinAmount), ("maximum", (double)limit.MaxAmount), ("region", regionId));
                }
                block.Recommendations.Add(new() { Product = group.Key, Order = product.DisplayOrder, CalculatedRate = amount, Rate = amount });
            }
        }
        block.Recommendations = block.Recommendations.OrderBy(item => item.Order).ThenBy(item => item.Product).ToList();
        // Nutrient contributions shown beneath recommendations are derived from final, rounded rates.
        foreach (var (element, limeFactor, dolomiteFactor) in new[] { ("Calcium added", .38, .2), ("Magnesium added", 0d, .1) })
        {
            var lime = block.Recommendations.FirstOrDefault(item => item.Product.Equals("Lime", StringComparison.OrdinalIgnoreCase))?.Rate ?? 0;
            var dolomite = block.Recommendations.FirstOrDefault(item => item.Product.Equals("Dolomite", StringComparison.OrdinalIgnoreCase))?.Rate ?? 0;
            Trace(document.Trace, sample.LabSoilId, "product-element-contribution", element, "Elemental contribution from final lime and dolomite rates.", lime * limeFactor + dolomite * dolomiteFactor,
                ("limeRate", lime), ("limeFraction", limeFactor), ("dolomiteRate", dolomite), ("dolomiteFraction", dolomiteFactor));
        }
    }

    private static void AddNotes(SoilReportSource source, SoilReportLabSample sample, string crop, List<TargetResult> targets, SoilReportDocument document)
    {
        if (!string.IsNullOrWhiteSpace(sample.ImportWarning))
            AddNote(document, $"lab-import-{sample.LabSoilId}", "Laboratory import warning", sample.ImportWarning.Trim(), sample.LabSoilId);
        AddNote(document, "disclaimer", "Disclaimer", "TGS Pty Ltd does not make any representations or warranties in respect hereof and expressly disclaims any liability for loss, damage or expenditure of whatsoever nature which may result from reliance on the contents of any advice given.", sample.LabSoilId);
        // Crop applicability is the intentional change agreed with the agronomist: do not attach potato notes to every horticulture template.
        if (crop.Contains("banana", StringComparison.OrdinalIgnoreCase))
            AddNote(document, "crop-banana", "Banana", "Recommendations adhere to the Queensland Reef Water Quality Prescribed Methodology for Banana Cultivation. Erosion & Sediment Control – Ratoon block inter-rows must have at least 60% covered ground, except when undertaking works (such as wheel ruts). By the start of the wet season all plant blocks must have at least 60% covered ground in the inter-row, except when undertaking renovation works to remove wheel ruts. At fallow, all blocks must have a grassy fallow or cover crop established that maintains adequate covered ground.", sample.LabSoilId);
        if (crop.StartsWith("potato", StringComparison.OrdinalIgnoreCase))
            AddNote(document, "crop-potato", "Potato", "Minimum requirement of 50kg/ha of Phosphorous in potato crops.", sample.LabSoilId);
        if (crop.Contains("sugar", StringComparison.OrdinalIgnoreCase))
            AddNote(document, "crop-sugar", "Sugarcane", "Recommendations adhere to Sugarcane Best Management Practice Guidelines.", sample.LabSoilId);
        if (targets.Any(target => target.Rule.ObjectName == "ManganesePpm" && target.Actual < target.Target))
            AddNote(document, "manganese-deficiency", "Manganese", "Some of these soils have low Mn availability. Crops vary with response to Mn but many crops grown on this soil may respond to regular small manganese applications as foliar or fertigated in a chelated form. In our experience Mn should be applied every 14-21 days through leaf growth. In orchards this is only effective through leaf flush. Mn deficiency symptoms may appear the same dull green as nitrogen limitations. Test strips with foliar may be a good indicator.", sample.LabSoilId);
        AddNote(document, "boron-application-area", "Boron", "Any boron recommendations are based on applications covering the entire area. Where boron applications occur through irrigation systems the quantities need to be decreased by a factor of (treated area ÷ total area). Failure to do this will result in over applications and may cause severe damage to the crop.", sample.LabSoilId);
        AddNote(document, "theoretical-required-amounts", "Theoretical elemental requirements", "Kg/ha to soil target, may not be achievable in the medium term or in some cases ever. Consult with your agronomist before acting here. A number of applications will usually be required to achieve this result.", sample.LabSoilId);
    }

    private static void AddNote(SoilReportDocument document, string rule, string title, string text, int sampleId)
    {
        var note = document.Notes.FirstOrDefault(note => note.RuleId == rule);
        if (note is null) { note = new() { RuleId = rule, Title = title, Text = text }; document.Notes.Add(note); }
        if (!note.LabSoilIds.Contains(sampleId)) note.LabSoilIds.Add(sampleId);
        Trace(document.Trace, sampleId, "note:" + rule, title, "This rule adds the report note recorded in the revision.", null);
    }

    private static void AddSugarInputs(SoilReportSource source, SoilReportBlock block, Dictionary<string, double?> values, List<SoilCalculationStep> trace)
    {
        if (!block.Crop.Contains("sugar", StringComparison.OrdinalIgnoreCase)) return;
        var carbon = Math.Round(Value(values, "OrganicCarbon"), 1);
        var bses = Value(values, "PhosphorousBsesPpm"); var pbi = Value(values, "PhosphorousBufferingIndex");
        foreach (var group in source.SugarInputs.GroupBy(rule => (rule.Nutrient, rule.PlantRatoon, rule.DistrictYield)))
        {
            var matched = group.FirstOrDefault(rule => rule.Nutrient == "Nitrogen"
                ? carbon > rule.OrganicCarbonFrom && carbon <= rule.OrganicCarbonTo
                : bses > rule.BsesFrom && bses <= rule.BsesTo && pbi > rule.PbiFrom && pbi <= rule.PbiTo);
            var amount = matched?.AllowableInput ?? 0;
            block.SugarInputs.Add(new() { Nutrient = group.Key.Nutrient, Stage = group.Key.PlantRatoon == "P" ? "Plant" : "Ratoon", DistrictYield = group.Key.DistrictYield, Amount = amount });
            Trace(trace, block.LabSoilId, "sugar-allowable-input", group.Key.Nutrient, $"Lookup {group.Key.PlantRatoon}, district yield {group.Key.DistrictYield}; lower bound exclusive, upper inclusive.", amount,
                ("organicCarbon", carbon), ("BSES", bses), ("PBI", pbi));
        }
    }

    private static void BuildHistory(SoilReportSource source, SoilReportDocument document)
    {
        // Anchor the three-year window to the report's sample date, so opening an old approved report cannot move its history window.
        var start = source.Survey.SurveyDate.Date.AddYears(-3);
        // Current samples have already been derived and traced. Reuse them for their historical average.
        var currentValues = document.Blocks.ToDictionary(block => block.LabSoilId,
            block => block.Values.ToDictionary(pair => pair.Key, pair => pair.Value.Value, StringComparer.OrdinalIgnoreCase));
        var samples = source.History.Concat(source.Samples).DistinctBy(sample => sample.LabSoilId)
            .Where(sample => sample.SampleSurveyId > 0 && sample.SurveyDate.Date >= start && sample.SurveyDate.Date <= source.Survey.SurveyDate.Date);
        foreach (var group in samples.GroupBy(sample => new { sample.SampleSurveyId, Date = sample.SurveyDate.Date }).OrderBy(group => group.Key.Date))
        {
            var values = group.Select(sample => currentValues.TryGetValue(sample.LabSoilId, out var current)
                ? current : Derive(sample, document.Trace)).ToList();
            var round = new SoilReportHistoryRound { SurveyId = group.Key.SampleSurveyId, Date = group.Key.Date, BlockCount = values.Count };
            foreach (var measure in document.Measures)
            {
                var present = values.Where(value => value.TryGetValue(measure.Key, out var number) && number.HasValue).Select(value => value[measure.Key]!.Value).ToList();
                if (present.Count > 0) round.Averages[measure.Key] = present.Average();
            }
            document.History.Add(round);
        }
    }
}
