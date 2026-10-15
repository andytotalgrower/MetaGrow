using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ApiModels.MetaGrow;
using MetaGrow.Shared.Models;

namespace MetaGrow.Api.Services.SoilReports;

/// <summary>Pure calculation pipeline. Legacy rule order is intentional; rendering and database access cannot change recommendations.</summary>
public sealed partial class SoilReportCalculator(SoilReportDefinitions _definitions)
{
    public const string _engineVersion = "soil-tissue-4";

    public SoilReportDocument Calculate(SoilReportSource source, int regionId, IReadOnlyCollection<SoilReportNotSampled>? notSampled = null,
        IReadOnlyCollection<SoilReportManualMatch>? manualMatches = null, IReadOnlyCollection<SoilReportManualMatch>? tissueMatches = null)
    {
        if (source.Survey.SurveyId <= 0 || source.Samples.Count == 0)
            throw new InvalidOperationException("A Sample survey with a complete soil result set is required.");
        if (source.Template.Settings.TestTypeId != 10 || SoilTemplateRules.IsLegacy(source.Template.Settings.TemplateName))
            throw new InvalidOperationException("Choose an active Soil template on the Sample survey.");
        notSampled ??= [];
        manualMatches ??= [];
        var matchedSamples = SoilReportSampleMatcher.Match(source, notSampled, manualMatches);
        // A repeated binding would silently overwrite ranges while doubling recommendations. Legacy duplicates must be resolved first.
        if (source.Targets.GroupBy(target => target.ObjectName, StringComparer.OrdinalIgnoreCase).Any(group => group.Select(target => target.TargetId).Distinct().Count() > 1))
            throw new InvalidOperationException("The calculation template contains duplicate test items or product mappings. Resolve these in the template manager before calculating a report.");
        var document = new SoilReportDocument
        {
            SurveyId = source.Survey.SurveyId, PropertyName = source.Survey.PropertyName,
            SurveyDate = source.Survey.SurveyDate, Agronomist = source.Survey.AgronomistName,
            TemplateName = source.Template.Settings.TemplateName, EngineVersion = _engineVersion,
            // New reports include individual pages; recalculation retains the previous revision's options.
            Options = new() { ShowIndividualBlockDetails = true },
            DefinitionHash = _definitions.VersionHash, DefinitionSnapshot = _definitions.Snapshot.ToList(), CalculatedUtc = DateTime.UtcNow,
            // The full source remains unchanged; exceptions are frozen separately with the report and its fingerprint.
            NotSampled = notSampled.OrderBy(item => item.SurveyItemId).Select(item =>
            {
                var sample = source.Survey.Samples.Single(sample => sample.ItemId == item.SurveyItemId);
                return new SoilReportOmittedSample(item.SurveyItemId, sample.BlockName ?? "", sample.BarcodeId, item.Reason);
            }).ToList()
        };
        // Freeze the selected pair as well as its evidence: swapping two same-block samples must invalidate QA.
        document.SampleMatches = SoilReportSampleMatcher.Review(source, manualMatches, notSampled).Rows.Select(row =>
        {
            var manual = manualMatches.SingleOrDefault(item => item.LabSoilId == row.LabSoilId);
            return new SoilReportSampleMatch(row.LabSoilId, row.SurveyItemId, matchedSamples[row.LabSoilId].BlockName ?? "",
                row.Method, row.Message, manual?.ConfirmedById ?? "", manual?.ConfirmedBy ?? "");
        }).ToList();
        foreach (var match in document.SampleMatches)
            Trace(document.Trace, match.LabSoilId, "sample-match-" + match.Method, match.BlockName,
                $"Survey item {match.SurveyItemId}. {match.Explanation}", match.SurveyItemId, ("surveyItemId", match.SurveyItemId));
        document.InputHash = InputFingerprint(source, regionId, _definitions.VersionHash,
            matchedSamples.Values.Concat(source.Survey.Samples.Where(sample => notSampled.Any(item => item.SurveyItemId == sample.ItemId))), document.NotSampled, document.SampleMatches);
        foreach (var sample in source.Samples.OrderBy(sample => sample.BlockName, StringComparer.OrdinalIgnoreCase).ThenBy(sample => sample.LabSoilId))
        {
            // The survey sample's crop is authoritative, never the lab text or template's nominal crop.
            var surveySample = matchedSamples[sample.LabSoilId];
            var crop = surveySample.CropTypeName!;
            var values = Derive(sample, document.Trace);
            var block = new SoilReportBlock
            {
                LabSoilId = sample.LabSoilId, BlockId = surveySample.BlockId, Name = surveySample.BlockName ?? sample.BlockName, Crop = crop,
                SampleDate = sample.SurveyDate, SampleReference = sample.SampleReference, SampleType = sample.SampleType,
                Laboratory = sample.LabName, Definition = _definitions.Select(crop, values)
            };
            foreach (var pair in values) block.Values[pair.Key] = new() { Value = pair.Value };
            var targets = source.Targets.Select(rule => CalculateTarget(rule, source.Template.Settings, values, sample.LabSoilId, document.Trace)).ToList();
            foreach (var target in targets.Where(target => target.Warning is not null))
                document.Warnings.Add($"{block.Name}: {target.Rule.ItemName}: {target.Warning}");
            foreach (var rule in source.Targets.Where(rule => !values.TryGetValue(rule.ObjectName, out var number) || !number.HasValue))
                document.Warnings.Add($"{block.Name}: {rule.ItemName} was not reported. Its required-amount calculation uses the legacy zero default; review this recommendation during QA.");
            AdjustBalances(targets, source.Template, values, crop, sample.LabSoilId, document.Trace);
            foreach (var (exchange, ppm) in new[] { ("ExchangeMagnesiumPc", "MagnesiumPpm"), ("ExchangeCalciumPc", "CalciumPpm"), ("ExchangePotassiumPc", "PotassiumPpm") })
            {
                var ex = targets.LastOrDefault(target => target.Rule.ObjectName == exchange);
                var pp = targets.LastOrDefault(target => target.Rule.ObjectName == ppm);
                if (ex is null || pp is null) continue;
                var removed = ex.Balance >= pp.Balance ? pp : ex;
                Trace(document.Trace, sample.LabSoilId, "exchange-versus-ppm", removed.Rule.ObjectName,
                    "Use the larger balancing requirement; zero the other representation to avoid double application.", 0,
                    ("exchange", ex.Balance), ("ppm", pp.Balance));
                removed.Balance = 0;
            }
            foreach (var target in targets)
            {
                if (block.Values.TryGetValue(target.Rule.ObjectName, out var value))
                {
                    value.Minimum = target.Minimum; value.Maximum = target.Maximum; value.Target = target.Target;
                    value.BelowTargetIsGood = target.Rule.BelowTargetIsGood;
                }
                block.Requirements.Add(new() { Key = target.Rule.ObjectName, Name = target.Rule.ItemName, Amount = target.Required });
            }
            BuildRecommendations(source, regionId, sample, crop, targets, block, document);
            AddNotes(source, sample, crop, targets, document);
            AddSugarInputs(source, block, values, document.Trace);
            document.Blocks.Add(block);
        }
        // A union of each block's database-defined rows avoids dropping measures that are absent from the first block.
        document.Measures = document.Blocks.SelectMany(block => _definitions.Get(block.Definition))
            .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase).Select(group => group.First())
            .Where(item => (item.ShowIfZero || document.Blocks.Any(block =>
                block.Values.TryGetValue(item.Key, out var value) && value.Value > 0)))
            .OrderBy(item => item.SectionOrder).ThenBy(item => item.Order).ToList();
        BuildHistory(source, document);
        var tissueScope = TissueReportAssembler.Prepare(source, manualMatches, notSampled);
        var tissueReview = TissueReportAssembler.Review(tissueScope, tissueMatches ?? []);
        document.Tissue = TissueReportAssembler.Build(source, tissueScope, tissueReview, _definitions, tissueMatches ?? []);
        if (document.Tissue.MissingSamples.Count > 0)
            document.Warnings.Add("Tissue results are pending for " + string.Join(", ", document.Tissue.MissingSamples) + ". Complete these results before peer QA.");
        // Only included tissue and selected targets affect this report; unrelated tissue-only blocks do not invalidate it.
        var tissueIds = document.Tissue.Samples.Select(sample => sample.LabTissueId).ToHashSet();
        var templateIds = document.Tissue.Samples.Select(sample => sample.TemplateId).ToHashSet();
        document.InputHash = Hash(document.InputHash + JsonSerializer.Serialize(new
        {
            Samples = source.Tissue.Samples.Where(sample => tissueIds.Contains(sample.LabTissueId)).OrderBy(sample => sample.LabTissueId),
            Templates = source.Tissue.Templates.Where(template => templateIds.Contains(template.Settings.TemplateId)).OrderBy(template => template.Settings.TemplateId),
            document.Tissue, Expected = tissueScope.MatchingSource.Survey.Samples.OrderBy(sample => sample.ItemId)
        }));
        var agronomist = source.Survey.Agronomists.FirstOrDefault(person => person.StaffId == source.Survey.AgronomistId);
        document.AgronomistEmail = agronomist?.Email ?? "";
        document.AgronomistPhone = agronomist?.MobilePhone ?? "";
        return document;
    }

    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    /// <summary>Exclude unrelated editor pick lists and billing state from report invalidation; the full source is still retained for audit.</summary>
    private static string InputFingerprint(SoilReportSource source, int regionId, string definitions, IEnumerable<ApiModels.SampleSurveyItem> samples,
        IReadOnlyCollection<SoilReportOmittedSample> notSampled, IReadOnlyCollection<SoilReportSampleMatch> matches)
    {
        var agronomist = source.Survey.Agronomists.FirstOrDefault(item => item.StaffId == source.Survey.AgronomistId);
        return Hash(JsonSerializer.Serialize(new
        {
            source.Survey.SurveyId, source.Survey.PropertyId, source.Survey.PropertyName, source.Survey.SurveyDate,
            source.Survey.AgronomistId, source.Survey.AgronomistName, source.Survey.CreatedBy, agronomist?.Email, agronomist?.MobilePhone,
            SurveySamples = samples.OrderBy(item => item.ItemId).Select(item => new { item.ItemId, item.BlockId, item.BlockName,
                item.CropTypeId, item.CropTypeName, item.BarcodeId, item.LabRefNo, item.LabId, item.TestTypeId }),
            // Area is used only by the explicitly flagged legacy sugar potassium rule.
            Areas = source.Survey.Blocks.OrderBy(item => item.BlockId).ThenBy(item => item.BlockName).Select(item => new { item.BlockId, item.BlockName, item.Size }),
            source.Template, Targets = source.Targets.OrderBy(item => item.TargetId).ThenBy(item => item.BalanceProductId),
            Samples = source.Samples.OrderBy(item => item.LabSoilId), History = source.History.OrderBy(item => item.LabSoilId),
            Products = source.Products.OrderBy(item => item.SoilTestProductId), source.ProductLimits, source.SugarInputs,
            regionId, definitions, notSampled, Matches = matches.OrderBy(match => match.LabSoilId), engine = _engineVersion
        }));
    }

    private sealed class TargetResult
    {
        public SoilReportTargetRule Rule { get; init; } = new();
        public double Actual { get; set; }
        public double Minimum { get; set; }
        public double Maximum { get; set; }
        public double Target { get; set; }
        public double Required { get; set; }
        public double Balance { get; set; }
        public int ProductId { get; set; }
        public string Product { get; set; } = "";
        public string? Warning { get; set; }
    }

    private static TargetResult CalculateTarget(SoilReportTargetRule rule, SoilTemplateDetail template,
        Dictionary<string, double?> values, int sampleId, List<SoilCalculationStep> trace)
    {
        values.TryGetValue(rule.ObjectName, out var actual);
        if (!actual.HasValue)
        {
            // Legacy LabSoil fields default to zero for null columns. Preserve the calculation while keeping the displayed lab value missing.
            actual = 0;
            Trace(trace, sampleId, "missing-lab-value", rule.ObjectName, "Legacy zero default used for a missing laboratory field; explicit QA warning added.", 0);
        }
        var result = new TargetResult { Rule = rule, Actual = actual.Value, Minimum = rule.AcceptableRangeMin,
            Maximum = rule.AcceptableRangeMax, Target = rule.Target, ProductId = rule.BalanceProductId, Product = rule.BalanceProductName };
        var tec = Value(values, "Tec");
        if (template.IsCationBoron && rule.ObjectName.Equals("BoronPpm", StringComparison.OrdinalIgnoreCase))
        {
            var cec = Value(values, "CecMeq");
            double Boron(string? text, double minimum) => text switch
            {
                "[ACTUAL]" => actual.Value, "[MINORACTUAL]" => Math.Max(actual.Value, minimum), _ => Number(text, "Boron rule")
            };
            if (cec < Number(template.CatRule1Val1, "Boron CEC threshold"))
            {
                // Preserve the legacy order: target uses the original minimum, then minimum changes, then maximum.
                result.Target = Boron(template.CatRule1Target, result.Minimum);
                result.Minimum = Boron(template.CatRule1Min, result.Minimum);
                result.Maximum = Boron(template.CatRule1Max, result.Minimum);
            }
            else if (cec >= Number(template.CatRule2Val1, "Boron CEC threshold") && cec < Number(template.CatRule2Val2, "Boron CEC threshold"))
            {
                result.Target = Number(template.CatRule2Target, "Boron target"); result.Minimum = Number(template.CatRule2Min, "Boron minimum"); result.Maximum = Number(template.CatRule2Max, "Boron maximum");
            }
            else if (cec >= Number(template.CatRule3Val1, "Boron CEC threshold"))
            {
                result.Target = Number(template.CatRule3Target, "Boron target"); result.Minimum = Number(template.CatRule3Min, "Boron minimum"); result.Maximum = Number(template.CatRule3Max, "Boron maximum");
            }
            Trace(trace, sampleId, "cation-boron", rule.ObjectName, "Apply the template's CEC-dependent boron target and range.", result.Target,
                ("CEC", cec), ("actual", actual.Value), ("minimum", result.Minimum), ("maximum", result.Maximum));
        }
        var delta = result.Target - actual.Value;
        var multiplier = 0d;
        var validParameters = rule.FormulaEnum == 10
            ? double.TryParse(rule.Params, NumberStyles.Float, CultureInfo.InvariantCulture, out multiplier) && double.IsFinite(multiplier)
            : string.IsNullOrEmpty(rule.Params);
        if (!validParameters)
        {
            // Legacy reflection catches missing/extra parameters and leaves the amount at zero. Retain that outcome, but make the defect visible.
            result.Warning = "The legacy formula has missing or invalid parameters. Its required amount remains zero; review the calculation template before QA.";
            Trace(trace, sampleId, "invalid-legacy-formula-parameters", rule.ObjectName, result.Warning, 0, ("formula", rule.FormulaEnum));
            return result;
        }
        var required = rule.FormulaEnum switch
        {
            0 => delta * 2 * (rule.BelowTargetIsGood ? -1 : 1),
            30 => delta * 3.2 * (rule.BelowTargetIsGood ? -1 : 1),
            40 => delta * 4 * (rule.BelowTargetIsGood ? -1 : 1),
            10 => Math.Round(delta * tec * multiplier, 0),
            20 => -delta * tec,
            _ => throw new InvalidOperationException($"Unknown formula {rule.FormulaEnum} for {rule.ItemName}.")
        };
        result.Required = Math.Max(0, required) / (rule.IsPercent ? 100 : 1);
        Trace(trace, sampleId, $"required-formula-{rule.FormulaEnum}", rule.ObjectName,
            "Apply the legacy required-amount formula, clamp negative values to zero, then convert percentages.", result.Required,
            ("actual", actual.Value), ("target", result.Target), ("TEC", tec), ("parameter", multiplier), ("percentDivisor", rule.IsPercent ? 100 : 1));
        if (result.Required > 0 && rule.BalanceProductId > 0 && !string.IsNullOrWhiteSpace(rule.BalanceParams))
        {
            var balanceMultiplier = Number(rule.BalanceParams.Split(',')[0], $"{rule.ItemName} product multiplier");
            result.Balance = result.Required * balanceMultiplier;
            Trace(trace, sampleId, "balance-product", rule.ObjectName, $"Convert elemental requirement to {result.Product}.", result.Balance,
                ("required", result.Required), ("multiplier", balanceMultiplier));
            if ((rule.ObjectName.Equals("CopperPpm", StringComparison.OrdinalIgnoreCase) || rule.ObjectName.Equals("ZincPpm", StringComparison.OrdinalIgnoreCase)) && actual >= result.Minimum)
            {
                result.Required = result.Balance = 0;
                Trace(trace, sampleId, "copper-zinc-minimum", rule.ObjectName, "Copper and zinc applications trigger below the minimum, not below target.", 0, ("actual", actual.Value), ("minimum", result.Minimum));
            }
        }
        return result;
    }

    private static double Number(string? text, string label) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value)
        ? value : throw new InvalidOperationException($"{label} must contain a finite number; check the calculation template.");
    private static double Value(IReadOnlyDictionary<string, double?> values, string key) => values.TryGetValue(key, out var value) ? value ?? 0 : 0;
    private static void Trace(List<SoilCalculationStep> steps, int sample, string rule, string measure, string explanation, double? result, params (string Key, double Value)[] inputs)
    {
        if (result.HasValue && !double.IsFinite(result.Value)) throw new InvalidOperationException($"Sample {sample}: {rule} produced an invalid {measure} result.");
        steps.Add(new() { LabSoilId = sample, Rule = rule, Measure = measure, Explanation = explanation, Result = result,
            Inputs = inputs.ToDictionary(input => input.Key, input => input.Value) });
    }
}
