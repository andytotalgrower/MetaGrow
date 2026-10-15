using System.Text.Json;
using ApiModels;
using ApiModels.MetaGrow;
using MetaGrow.Shared.Models;

namespace MetaGrow.Api.Services.SoilReports;

/// <summary>Adds observed tissue values and direct target comparisons. It never runs soil recommendation formulas.</summary>
public static class TissueReportAssembler
{
    public const string _definitionKey = "LabReportDefinitionTissue.xml";

    public sealed record Scope(SoilReportSource MatchingSource, Dictionary<int, SampleSurveyItem> SoilMatches);

    public static Scope Prepare(SoilReportSource source, IReadOnlyCollection<SoilReportManualMatch> soilManual,
        IReadOnlyCollection<SoilReportNotSampled> omissions)
    {
        var soilReview = SoilReportSampleMatcher.Review(source, soilManual, omissions);
        var soilMatches = soilReview.Rows.Where(row => !row.RequiresReview && row.SurveyItemId > 0)
            .ToDictionary(row => row.LabSoilId, row => source.Survey.Samples.Single(sample => sample.ItemId == row.SurveyItemId));
        var tissueTypes = source.Survey.LabTestTypes.Where(type => type.TestTypeId == 20).Select(type => type.LabTestTypeId).ToHashSet();
        var allTissueItems = source.Survey.Samples.Where(sample => tissueTypes.Contains(sample.TestTypeId)).ToList();
        var included = allTissueItems.Where(sample => soilMatches.Values.Any(soil => SameBlock(source, sample, soil))).ToList();
        if (included.Count > 0 && !source.Tissue.Loaded)
            throw new InvalidOperationException("Tissue report inputs were not supplied by TgsApi.Core. Deploy the combined-report API and read procedure before opening this survey.");

        // Adapt the existing conservative matcher at the boundary. Its legacy LabSoilId property is scoped to
        // tissue here; separate state, endpoints and snapshots prevent collisions with actual soil IDs.
        var survey = JsonSerializer.Deserialize<SampleSurveyEditorDto>(JsonSerializer.Serialize(source.Survey))!;
        survey.Samples = included;
        foreach (var type in survey.LabTestTypes) type.TestTypeId = type.TestTypeId == 20 ? 10 : -Math.Abs(type.TestTypeId);
        var matching = new SoilReportSource { Survey = survey };
        foreach (var tissue in source.Tissue.Samples)
        {
            var maps = source.Tissue.BlockMappings.Where(map => SoilReportSampleMatcher.SameName(map.LabBlockName, tissue.BlockName)
                && (tissue.BlockId <= 0 || map.BlockId == tissue.BlockId))
                .DistinctBy(map => (map.BlockId, NormalizeType(map.BlockType))).ToList();
            var blockId = tissue.BlockId;
            var blockType = NormalizeType(tissue.BlockType);
            if (maps.Count == 1 && (blockId <= 0 || blockType.Length == 0))
            {
                blockId = maps[0].BlockId; blockType = NormalizeType(maps[0].BlockType);
            }
            var result = new SoilReportLabSample
            {
                LabSoilId = tissue.LabTissueId, SampleSurveyId = tissue.SampleSurveyId,
                BlockId = blockId, BlockName = tissue.BlockName, LabId = tissue.LabId, LabName = tissue.LabName,
                LabTestTypeId = tissue.LabTestTypeId, SampleReference = tissue.SampleReference,
                Values = tissue.Values, Metadata = new(tissue.Metadata), SurveyDate = tissue.SurveyDate
            };
            result.Metadata["BlockType"] = JsonSerializer.SerializeToElement(blockType);
            if (maps.Count > 1 && blockType.Length == 0) result.Metadata["MappingConflict"] = JsonSerializer.SerializeToElement(true);
            bool Identifies(SampleSurveyItem sample) => sample.LabId == result.LabId &&
                (sample.BlockId > 0 && sample.BlockId == blockId && CompatibleType(SoilReportSampleMatcher.SampleBlockType(source, sample), blockType)
                || SoilReportSampleMatcher.SameReference(sample, result)
                || SoilReportSampleMatcher.SameName(sample.BlockName, result.BlockName));
            // Known tissue-only blocks are deliberately omitted, while unidentified results still need review.
            if (included.Any(Identifies) || included.Count > 0 && !allTissueItems.Any(Identifies) && blockId <= 0)
                matching.Samples.Add(result);
        }
        return new(matching, soilMatches);
    }

    public static SoilReportMatchingReview Review(Scope scope, IReadOnlyCollection<SoilReportManualMatch> manual)
    {
        var review = SoilReportSampleMatcher.Review(scope.MatchingSource, manual);
        foreach (var row in review.Rows)
        {
            row.Message = row.Message.Replace("soil", "tissue", StringComparison.OrdinalIgnoreCase);
            row.AutomaticMessage = row.AutomaticMessage.Replace("soil", "tissue", StringComparison.OrdinalIgnoreCase);
            if (!row.IsManual && scope.MatchingSource.Samples.Single(sample => sample.LabSoilId == row.LabSoilId).Metadata.ContainsKey("MappingConflict"))
            {
                row.RequiresReview = true; row.SurveyItemId = row.AutomaticSurveyItemId = 0;
                row.Message = row.AutomaticMessage = "More than one saved block mapping applies. Confirm the collected tissue sample.";
            }
        }
        return review;
    }

    public static List<TissueReportCropChoice> Crops(SoilReportSource source, Scope scope, SoilReportMatchingReview review) =>
        review.Rows.Where(row => !row.RequiresReview && row.SurveyItemId > 0)
            .Select(row => Crop(source, scope, scope.MatchingSource.Survey.Samples.Single(sample => sample.ItemId == row.SurveyItemId)))
            .DistinctBy(sample => sample.CropTypeId).OrderBy(sample => sample.CropTypeName)
            .Select(sample => new TissueReportCropChoice(sample.CropTypeId, sample.CropTypeName ?? "")).ToList();

    public static TissueReportDocument Build(SoilReportSource source, Scope scope, SoilReportMatchingReview review,
        SoilReportDefinitions definitions, IReadOnlyCollection<SoilReportManualMatch> manual)
    {
        if (review.RequiresReview) throw new InvalidOperationException("Review the tissue sample matches before opening the report.");
        var document = new TissueReportDocument();
        document.ExpectedSoilLabIds = scope.SoilMatches.Where(pair => scope.MatchingSource.Survey.Samples.Any(item => SameBlock(source, pair.Value, item)))
            .Select(pair => pair.Key).ToList();
        foreach (var missing in scope.MatchingSource.Survey.Samples.Where(sample => !review.Rows.Any(row => row.SurveyItemId == sample.ItemId)))
            document.MissingSamples.Add($"{missing.BlockName} (survey item {missing.ItemId})");
        if (review.Rows.Count == 0) return document;
        foreach (var row in review.Rows)
        {
            var item = scope.MatchingSource.Survey.Samples.Single(sample => sample.ItemId == row.SurveyItemId);
            var crop = Crop(source, scope, item);
            var selection = source.Tissue.Selections.SingleOrDefault(selection => selection.CropId == crop.CropTypeId);
            var template = source.Tissue.Templates.SingleOrDefault(template => template.Settings.TemplateId == selection?.TemplateId)
                ?? throw new InvalidOperationException($"Choose a tissue template for {crop.CropTypeName}.");
            if (template.Settings.TestTypeId != 20 || SoilTemplateRules.IsLegacy(template.Settings.TemplateName))
                throw new InvalidOperationException("Choose an active Tissue template.");
            if (template.Targets.GroupBy(target => target.ObjectName, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
                throw new InvalidOperationException($"Tissue template {template.Settings.TemplateName} has duplicate test items. Resolve these in the template manager.");
            var lab = source.Tissue.Samples.Single(sample => sample.LabTissueId == row.LabSoilId);
            var sample = new TissueReportSample
            {
                LabTissueId = lab.LabTissueId, SurveyItemId = item.ItemId, BlockName = crop.BlockName ?? lab.BlockName,
                Crop = crop.CropTypeName ?? "", TemplateId = template.Settings.TemplateId, TemplateName = template.Settings.TemplateName,
                SampleDate = lab.SurveyDate, SamplePart = lab.SamplePart, SampleReference = lab.SampleReference, Laboratory = lab.LabName,
                SoilLabIds = scope.SoilMatches.Where(pair => SameBlock(source, pair.Value, item) && pair.Value.CropTypeId == crop.CropTypeId).Select(pair => pair.Key).ToList()
            };
            foreach (var value in lab.Values)
            {
                if (value.Value.HasValue && !double.IsFinite(value.Value.Value)) throw new InvalidOperationException("A tissue reading is not a finite number.");
                sample.Values[value.Key] = new() { Value = value.Value };
            }
            foreach (var target in template.Targets)
            {
                if (!double.IsFinite(target.Target) || !double.IsFinite(target.AcceptableRangeMin) || !double.IsFinite(target.AcceptableRangeMax)
                    || target.AcceptableRangeMin > target.Target || target.Target > target.AcceptableRangeMax)
                    throw new InvalidOperationException($"Check the range for {target.ItemName} in tissue template {template.Settings.TemplateName}.");
                if (!sample.Values.TryGetValue(target.ObjectName, out var value)) sample.Values[target.ObjectName] = value = new();
                value.Minimum = target.AcceptableRangeMin; value.Maximum = target.AcceptableRangeMax; value.Target = target.Target;
                value.BelowTargetIsGood = target.BelowTargetIsGood;
            }
            document.Samples.Add(sample);
            var choice = manual.SingleOrDefault(match => match.LabSoilId == lab.LabTissueId);
            document.Matches.Add(new(lab.LabTissueId, item.ItemId, sample.BlockName, row.Method, row.Message,
                choice?.ConfirmedById ?? "", choice?.ConfirmedBy ?? ""));
        }
        // Database definitions control presentation; unreported measurements do not manufacture empty rows.
        document.Measures = definitions.Get(_definitionKey).Where(measure => document.Samples.Any(sample =>
            sample.Values.TryGetValue(measure.Key, out var value) && value.Value.HasValue && (measure.ShowIfZero || value.Value != 0))).ToList();
        return document;
    }

    private static SampleSurveyItem Crop(SoilReportSource source, Scope scope, SampleSurveyItem tissue)
    {
        var soils = scope.SoilMatches.Values.Where(soil => SameBlock(source, soil, tissue)).DistinctBy(soil => soil.CropTypeId).ToList();
        var crop = soils.Count == 1 ? soils[0] : soils.SingleOrDefault(soil => soil.CropTypeId == tissue.CropTypeId);
        if (crop is null || crop.CropTypeId <= 0 || string.IsNullOrWhiteSpace(crop.CropTypeName))
            throw new InvalidOperationException($"Confirm the survey crop for {tissue.BlockName} before selecting its tissue template.");
        return crop;
    }
    private static bool SameBlock(SoilReportSource source, SampleSurveyItem first, SampleSurveyItem second) =>
        first.BlockId > 0 && first.BlockId == second.BlockId
        && CompatibleType(SoilReportSampleMatcher.SampleBlockType(source, first), SoilReportSampleMatcher.SampleBlockType(source, second));
    private static bool CompatibleType(string first, string second) => first != "ambiguous" && second != "ambiguous"
        && (first.Length == 0 || second.Length == 0 || first == second);
    private static string NormalizeType(string? type) => string.IsNullOrWhiteSpace(type) ? "" : type.StartsWith("ban", StringComparison.OrdinalIgnoreCase) ? "banana" : "mcs";
}
