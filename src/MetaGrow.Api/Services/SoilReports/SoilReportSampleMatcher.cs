using System.Globalization;
using System.Text.Json;
using ApiModels;
using ApiModels.MetaGrow;
using MetaGrow.Shared.Models;

namespace MetaGrow.Api.Services.SoilReports;

/// <summary>Uses the import's block mapping first, keeping ambiguity and manual choices explicit.</summary>
public static class SoilReportSampleMatcher
{
    public static List<SampleSurveyItem> SoilSamples(SoilReportSource source)
    {
        var soilTypes = source.Survey.LabTestTypes.Where(type => type.TestTypeId == 10).Select(type => type.LabTestTypeId).ToHashSet();
        return source.Survey.Samples.Where(sample => soilTypes.Contains(sample.TestTypeId)).ToList();
    }

    public static SoilReportMatchingReview Review(SoilReportSource source,
        IReadOnlyCollection<SoilReportManualMatch>? manual = null, IReadOnlyCollection<SoilReportNotSampled>? notSampled = null)
    {
        manual ??= []; notSampled ??= [];
        var samples = SoilSamples(source).Where(sample => !notSampled.Any(item => item.SurveyItemId == sample.ItemId)).ToList();
        if (source.Samples.Select(result => result.LabSoilId).Distinct().Count() != source.Samples.Count
            || samples.Select(sample => sample.ItemId).Distinct().Count() != samples.Count)
            throw new InvalidOperationException("Duplicate sample identifiers were returned. Check the imported soil results and survey items.");
        var review = new SoilReportMatchingReview { InputHash = InputHash(source) };
        foreach (var result in source.Samples.OrderBy(result => result.BlockName).ThenBy(result => result.LabSoilId))
        {
            var eligible = samples.Where(sample => Compatible(source, sample, result)).ToList();
            var row = Automatic(source, result, eligible);
            row.AutomaticSurveyItemId = row.SurveyItemId;
            row.AutomaticMessage = row.Message;
            row.Choices = eligible.OrderBy(sample => sample.BlockName).ThenBy(sample => sample.ItemId)
                .Select(sample => new SoilReportMatchChoice(sample.ItemId,
                    $"{sample.BlockName} · item {sample.ItemId} · {sample.TestName ?? "Soil"} · ref {Display(sample.LabRefNo)} · barcode {(sample.BarcodeId > 0 ? sample.BarcodeId.ToString(CultureInfo.InvariantCulture) : "none")}")).ToList();
            var choices = manual.Where(item => item.LabSoilId == result.LabSoilId).ToList();
            if (choices.Count > 0)
            {
                var choice = choices[0];
                var sample = eligible.SingleOrDefault(sample => sample.ItemId == choice.SurveyItemId);
                if (choices.Count != 1 || sample is null || choice.SourceHash != PairHash(source, result, sample)
                    || string.IsNullOrWhiteSpace(choice.Reason))
                {
                    row.SurveyItemId = 0; row.RequiresReview = true;
                    row.Message = "The saved match is no longer valid. Check the current records and choose again, or restore automatic matching.";
                }
                else
                {
                    row.SurveyItemId = sample.ItemId; row.RequiresReview = false; row.IsManual = true;
                    row.Method = "manual"; row.Reason = choice.Reason; row.Message = "Confirmed by an agronomist: " + choice.Reason;
                }
            }
            review.Rows.Add(row);
        }
        // Resolve all rows together. Never let result order decide which duplicate gets the sample.
        foreach (var group in review.Rows.Where(row => row.SurveyItemId > 0).GroupBy(row => row.SurveyItemId).Where(group => group.Count() > 1))
            foreach (var row in group)
            {
                row.RequiresReview = true;
                row.Message = $"More than one laboratory result points to survey item {group.Key}. Choose a different sample for each result.";
                row.AutomaticSurveyItemId = 0;
                row.AutomaticMessage = row.Message;
            }
        if (manual.Any(item => !source.Samples.Any(result => result.LabSoilId == item.LabSoilId)))
            review.Issues.Add("A saved match refers to a laboratory result that is no longer in this survey. Save the current matches to remove that old choice.");
        return review;
    }

    private static SoilReportMatchRow Automatic(SoilReportSource source, SoilReportLabSample result, List<SampleSurveyItem> eligible)
    {
        var row = new SoilReportMatchRow { LabSoilId = result.LabSoilId, BlockName = result.BlockName,
            Laboratory = result.LabName, Reference = result.SampleReference, RequiresReview = true };
        var blocks = eligible.Where(sample => SameMappedBlock(source, sample, result)).ToList();
        var method = "mapped-block";
        if (blocks.Count == 0)
        {
            // Names are a fallback only when at least one side lacks a mapped block. Do not fuzzy-match names.
            blocks = eligible.Where(sample => (sample.BlockId <= 0 || result.BlockId <= 0)
                && SameName(sample.BlockName, result.BlockName)).ToList();
            method = "block-name";
        }
        var references = eligible.Where(sample => SameReference(sample, result)).ToList();
        if (blocks.Count > 0)
        {
            // An obsolete reference with no match is harmless; one pointing to another sample is a conflict.
            if (references.Any(sample => !blocks.Contains(sample)))
                row.Message = "The block and laboratory reference identify different survey samples. Confirm the correct sample.";
            else
            {
                var selected = blocks.Count == 1 ? blocks[0] : references.Count == 1 ? references[0] : null;
                if (selected is not null)
                {
                    row.SurveyItemId = selected.ItemId; row.RequiresReview = false;
                    row.Method = blocks.Count == 1 ? method : method + "-reference";
                    row.Message = method == "mapped-block" ? "Matched the mapped property block." : "Matched a unique block name (case and whitespace ignored).";
                    row.Message += references.Count == 0 ? " No matching laboratory reference or barcode was required." : " Laboratory reference/barcode also matched.";
                }
                else row.Message = "This block has multiple soil samples. Select which sample belongs to this laboratory result.";
            }
        }
        else if (references.Count == 1 && !ConflictingMappedBlock(source, references[0], result))
        {
            row.SurveyItemId = references[0].ItemId; row.RequiresReview = false; row.Method = "reference";
            row.Message = "Matched a unique laboratory reference/barcode; no conflicting block mapping exists.";
        }
        else if (references.Count == 1 && ConflictingMappedBlock(source, references[0], result))
            row.Message = "The laboratory reference identifies a sample, but its saved block link differs from the result. Confirm the correct sample.";
        else row.Message = eligible.Count == 0
            ? "No soil sample from this laboratory is available in the survey. Check the sample laboratory and type."
            : "No unique, consistent block or reference match was found. Select the correct survey sample.";
        return row;
    }

    public static Dictionary<int, SampleSurveyItem> Match(SoilReportSource source,
        IReadOnlyCollection<SoilReportNotSampled>? notSampled = null, IReadOnlyCollection<SoilReportManualMatch>? manual = null)
    {
        notSampled ??= []; manual ??= [];
        ValidateNotSampled(source, notSampled, manual);
        var expected = SoilSamples(source).Where(sample => !notSampled.Any(item => item.SurveyItemId == sample.ItemId)).ToList();
        if (expected.Count == 0) throw new InvalidOperationException("This survey has no soil samples to report.");
        var review = Review(source, manual, notSampled);
        if (review.RequiresReview)
            throw new InvalidOperationException("Review the soil sample matches before opening the report. "
                + (review.Rows.FirstOrDefault(row => row.RequiresReview)?.Message ?? review.Issues.First()));
        var matches = review.Rows.ToDictionary(row => row.LabSoilId, row => expected.Single(sample => sample.ItemId == row.SurveyItemId));
        if (matches.Count != expected.Count)
        {
            var missing = expected.Where(sample => !matches.Values.Any(match => match.ItemId == sample.ItemId));
            throw new InvalidOperationException($"The survey has {expected.Count} expected soil samples but only {matches.Count} matched results. Missing: {string.Join(", ", missing.Take(10).Select(sample => $"{sample.BlockName} (item {sample.ItemId}, barcode {sample.BarcodeId})"))}. Import the remaining results, or record a reason under Not sampled for samples that were never collected.");
        }
        foreach (var sample in matches.Values)
            if (string.IsNullOrWhiteSpace(sample.CropTypeName))
                throw new InvalidOperationException($"Choose the crop on survey sample {sample.ItemId} before opening the report.");
        return matches;
    }

    public static bool HasLabResult(SoilReportSource source, SampleSurveyItem sample, IReadOnlyCollection<SoilReportManualMatch>? manual = null)
    {
        var review = Review(source, manual);
        return review.Rows.Any(row => !row.RequiresReview ? row.SurveyItemId == sample.ItemId
            : source.Samples.Any(result => result.LabSoilId == row.LabSoilId && Compatible(source, sample, result)
                && (SameMappedBlock(source, sample, result) || SameReference(sample, result)
                    || (sample.BlockId <= 0 || result.BlockId <= 0) && SameName(sample.BlockName, result.BlockName))));
    }

    public static void ValidateNotSampled(SoilReportSource source, IReadOnlyCollection<SoilReportNotSampled> notSampled,
        IReadOnlyCollection<SoilReportManualMatch>? manual = null)
    {
        var samples = SoilSamples(source);
        if (notSampled.Select(item => item.SurveyItemId).Distinct().Count() != notSampled.Count)
            throw new ArgumentException("A survey sample can only be marked not sampled once.");
        foreach (var item in notSampled)
        {
            var sample = samples.SingleOrDefault(sample => sample.ItemId == item.SurveyItemId)
                ?? throw new ArgumentException("Only soil samples in this survey can be marked not sampled.");
            if (string.IsNullOrWhiteSpace(item.Reason) || item.Reason.Length > 500)
                throw new ArgumentException("Enter a reason of 1–500 characters for each sample not collected.");
            // Ambiguous evidence must be resolved, not hidden by marking a potentially returned sample uncollected.
            if (HasLabResult(source, sample, manual))
                throw new ArgumentException($"{sample.BlockName} (barcode {sample.BarcodeId}) has a lab result or an unresolved possible match. Resolve its sample match and remove its not-sampled exclusion before continuing.");
        }
        if (notSampled.Count > 0 && notSampled.Count == samples.Count)
            throw new ArgumentException("At least one soil sample must remain in the report.");
    }

    public static string InputHash(SoilReportSource source) => SoilReportCalculator.Hash(JsonSerializer.Serialize(new
    {
        source.Survey.SurveyId, source.Survey.PropertyId,
        Samples = SoilSamples(source).OrderBy(sample => sample.ItemId).Select(SampleIdentity),
        Results = source.Samples.OrderBy(result => result.LabSoilId).Select(ResultIdentity),
        Blocks = source.Survey.Blocks.OrderBy(block => block.BlockId).ThenBy(block => block.BlockType)
            .Select(block => new { block.BlockId, block.BlockName, block.BlockType }),
        Types = source.Survey.LabTestTypes.OrderBy(type => type.LabTestTypeId).Select(type => new { type.LabTestTypeId, type.LabId, type.TestTypeId })
    }));

    public static string PairHash(SoilReportSource source, SoilReportLabSample result, SampleSurveyItem sample) =>
        SoilReportCalculator.Hash(JsonSerializer.Serialize(new { source.Survey.SurveyId, source.Survey.PropertyId,
            Sample = SampleIdentity(sample), Result = ResultIdentity(result), SampleBlockType = SampleBlockType(source, sample) }));

    private static object SampleIdentity(SampleSurveyItem sample) => new { sample.ItemId, sample.SurveyId, sample.BlockId,
        sample.BlockName, sample.LabId, sample.TestTypeId, sample.BarcodeId, sample.LabRefNo };
    private static object ResultIdentity(SoilReportLabSample result) => new { result.LabSoilId, result.SampleSurveyId,
        result.BlockId, result.BlockName, result.LabId, result.LabTestTypeId, result.SampleReference, BlockType = ResultBlockType(result) };

    private static bool Compatible(SoilReportSource source, SampleSurveyItem sample, SoilReportLabSample result) =>
        sample.SurveyId == source.Survey.SurveyId && result.SampleSurveyId == source.Survey.SurveyId && sample.LabId == result.LabId
        // Soil packages may differ between collection and import. Material must remain soil, never tissue or sap.
        && !source.Survey.LabTestTypes.Any(type => type.LabTestTypeId == result.LabTestTypeId && (type.TestTypeId != 10 || type.LabId != result.LabId));

    private static bool SameMappedBlock(SoilReportSource source, SampleSurveyItem sample, SoilReportLabSample result) =>
        sample.BlockId > 0 && sample.BlockId == result.BlockId && !ConflictingMappedBlock(source, sample, result);

    private static bool ConflictingMappedBlock(SoilReportSource source, SampleSurveyItem sample, SoilReportLabSample result)
    {
        if (sample.BlockId <= 0 || result.BlockId <= 0) return false;
        if (sample.BlockId != result.BlockId) return true;
        var sampleType = SampleBlockType(source, sample); var resultType = ResultBlockType(result);
        // Banana and multi-crop block IDs can overlap. An unresolved collision is never an automatic match.
        return sampleType == "ambiguous" || sampleType.Length > 0 && resultType.Length > 0 && sampleType != resultType;
    }

    internal static string SampleBlockType(SoilReportSource source, SampleSurveyItem sample)
    {
        var blocks = source.Survey.Blocks.Where(block => block.BlockId == sample.BlockId).ToList();
        if (blocks.Select(block => BlockType(block.BlockType)).Distinct().Count() > 1)
            blocks = blocks.Where(block => SameName(block.BlockName, sample.BlockName)).ToList();
        var types = blocks.Select(block => BlockType(block.BlockType)).Distinct().ToList();
        return types.Count > 1 || types.Count == 0 && source.Survey.Blocks.Count(block => block.BlockId == sample.BlockId) > 1
            ? "ambiguous" : types.SingleOrDefault() ?? "";
    }
    private static string ResultBlockType(SoilReportLabSample result) => BlockType(result.Metadata
        .FirstOrDefault(pair => pair.Key.Equals("BlockType", StringComparison.OrdinalIgnoreCase)).Value.ToString());
    // Use the same banana/multi-crop aliases as the laboratory import mapping.
    private static string BlockType(string? value) => string.IsNullOrWhiteSpace(value) ? "" : value.StartsWith("ban", StringComparison.OrdinalIgnoreCase) ? "banana" : "mcs";
    internal static bool SameReference(SampleSurveyItem sample, SoilReportLabSample result)
    {
        var reference = NormalizeReference(result.SampleReference);
        return reference.Length > 0 && (NormalizeReference(sample.LabRefNo) == reference
            || sample.BarcodeId > 0 && NormalizeReference(sample.BarcodeId.ToString(CultureInfo.InvariantCulture)) == reference);
    }
    private static string NormalizeReference(string? reference) => long.TryParse(reference?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
        ? number.ToString(CultureInfo.InvariantCulture) : (reference ?? "").Trim().ToUpperInvariant();
    private static string NormalizeName(string? name) => string.Join(" ", (name ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
    internal static bool SameName(string? first, string? second) => NormalizeName(first).Length > 0 && NormalizeName(first) == NormalizeName(second);
    private static string Display(string? value) => string.IsNullOrWhiteSpace(value) ? "none" : value.Trim();
}
