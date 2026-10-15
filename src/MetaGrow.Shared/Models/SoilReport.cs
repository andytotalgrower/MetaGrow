using ApiModels.MetaGrow;

namespace MetaGrow.Shared.Models;

/// <summary>A revision includes its complete input snapshot; approval always identifies this exact result.</summary>
public sealed class SoilReportDocument
{
    public Guid Id { get; set; }
    public int Revision { get; set; }
    public int SurveyId { get; set; }
    public string PropertyName { get; set; } = "";
    public DateTime SurveyDate { get; set; }
    public string Agronomist { get; set; } = "";
    public string AgronomistEmail { get; set; } = "";
    public string AgronomistPhone { get; set; } = "";
    public string TemplateName { get; set; } = "";
    public string EngineVersion { get; set; } = "";
    public string InputHash { get; set; } = "";
    public string DefinitionHash { get; set; } = "";
    public List<SoilReportDefinition> DefinitionSnapshot { get; set; } = [];
    public string Status { get; set; } = "Editing";
    public DateTime CalculatedUtc { get; set; }
    public List<SoilReportBlock> Blocks { get; set; } = [];
    public List<SoilReportMeasure> Measures { get; set; } = [];
    public List<SoilReportHistoryRound> History { get; set; } = [];
    public List<SoilReportNote> Notes { get; set; } = [];
    public List<SoilReportAction> Actions { get; set; } = [];
    public List<SoilCalculationStep> Trace { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public List<SoilReportOmittedSample> NotSampled { get; set; } = [];
    public List<SoilReportSampleMatch> SampleMatches { get; set; } = [];
    public SoilReportOptions Options { get; set; } = new();
    // Empty on historical soil-only revisions; tissue values and targets are frozen with the same QA revision.
    public TissueReportDocument Tissue { get; set; } = new();
    // Material choices belong to the frozen revision. Keep all source data so changing the
    // presentation never discards advice, overrides or the audit evidence for either material.
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasTissueResults => Tissue.Samples.Any(sample => sample.SoilLabIds.Any(id => Blocks.Any(block => block.LabSoilId == id && !block.Excluded)));
    [System.Text.Json.Serialization.JsonIgnore]
    public bool ShowsTissue => Options.IncludeTissue && HasTissueResults;
    [System.Text.Json.Serialization.JsonIgnore]
    public List<SoilReportBlock> ReportedBlocks => Blocks.Where(block => !block.Excluded && (Options.IncludeSoil
        || Options.IncludeTissue && Tissue.Samples.Any(sample => sample.SoilLabIds.Contains(block.LabSoilId)))).ToList();
    [System.Text.Json.Serialization.JsonIgnore]
    public string MaterialLabel => Options.IncludeSoil ? ShowsTissue ? "Soil And Tissue" : "Soil" : "Tissue";
    [System.Text.Json.Serialization.JsonIgnore]
    public string FileMaterialLabel => MaterialLabel.Replace(" And ", "_");
    public string Title => Options.IncludeSoil ? ShowsTissue ? "Soil And Tissue Report" : "Soil Nutrient Balance Report" : "Tissue Report";
}

public sealed class SoilReportOptions
{
    // Defaults preserve historical soil-only and combined revisions when these JSON properties are absent.
    public bool IncludeSoil { get; set; } = true;
    public bool IncludeTissue { get; set; } = true;
    public bool ShowStatusOverview { get; set; } = true;
    public bool ShowHistory { get; set; } = true;
    public bool ShowRequiredAmounts { get; set; } = true;
    public bool ShowBlockDetails { get; set; } = true;
    // Absent in saved revisions: opt in through review instead of changing an already approved report.
    public bool ShowIndividualBlockDetails { get; set; }
    public bool ShowSugarInputs { get; set; } = true;
}

public sealed class SoilReportAction
{
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
}

public sealed class SoilReportNote
{
    public string RuleId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    public List<int> LabSoilIds { get; set; } = [];
}

/// <summary>Definition metadata is frozen from the maintained MetaGrow database layout.</summary>
public sealed class SoilReportMeasure
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    // Frozen with the revision; old reports fall back to their full label.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? ShortName { get; set; }
    public string Section { get; set; } = "";
    public string SectionColour { get; set; } = "";
    public string? BackgroundColour { get; set; }
    public int SectionOrder { get; set; }
    public int Order { get; set; }
    public string NumberFormat { get; set; } = "#,##0.0";
    public bool ShowIfZero { get; set; } = true;
}

public sealed class SoilReportBlock
{
    public int LabSoilId { get; set; }
    public int BlockId { get; set; }
    public string Name { get; set; } = "";
    public string Crop { get; set; } = "";
    public string SampleReference { get; set; } = "";
    public string SampleType { get; set; } = "";
    public string Laboratory { get; set; } = "";
    public DateTime SampleDate { get; set; }
    public string Definition { get; set; } = "";
    private Dictionary<string, SoilReportValue> _values = new(StringComparer.OrdinalIgnoreCase);
    // SQL and legacy XML differ in casing (Nh4nppm / Nh4nPpm). Preserve matching after a saved revision's JSON round trip.
    public Dictionary<string, SoilReportValue> Values
    {
        get => _values;
        set => _values = new(value, StringComparer.OrdinalIgnoreCase);
    }
    public List<SoilReportRecommendation> Recommendations { get; set; } = [];
    public List<SoilReportRequirement> Requirements { get; set; } = [];
    public List<SoilReportSugarInput> SugarInputs { get; set; } = [];
    public bool Excluded { get; set; }
}

public sealed class SoilReportValue
{
    public double? Value { get; set; }
    public double? Minimum { get; set; }
    public double? Maximum { get; set; }
    public double? Target { get; set; }
    public bool BelowTargetIsGood { get; set; }
    public string Position => !Value.HasValue || !Minimum.HasValue || !Maximum.HasValue ? "unrated"
        : Value < Minimum ? "low" : Value > Maximum ? "high" : "range";
    // Position and severity stay separate: sodium and other inverse measures retain honest labels.
    public string Severity => Position is "low" ? (BelowTargetIsGood ? "caution" : "deficient")
        : Position is "high" ? (BelowTargetIsGood ? "deficient" : "caution") : Position;
}

public sealed class SoilReportRecommendation
{
    public string Product { get; set; } = "";
    public int Order { get; set; }
    public double CalculatedRate { get; set; }
    public double Rate { get; set; }
    public bool Overridden { get; set; }
    public string Reason { get; set; } = "";
}

public sealed class SoilReportRequirement
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public double Amount { get; set; }
}

public sealed class SoilReportSugarInput
{
    public string Nutrient { get; set; } = "";
    public string Stage { get; set; } = "";
    public double DistrictYield { get; set; }
    public double Amount { get; set; }
}

public sealed class SoilReportHistoryRound
{
    public int SurveyId { get; set; }
    public DateTime Date { get; set; }
    public int BlockCount { get; set; }
    private Dictionary<string, double> _averages = new(StringComparer.OrdinalIgnoreCase);
    // Historical values use the same case-insensitive definition bindings as current blocks.
    public Dictionary<string, double> Averages
    {
        get => _averages;
        set => _averages = new(value, StringComparer.OrdinalIgnoreCase);
    }
}

/// <summary>Human-readable rules plus numeric operands make the derivation inspectable without parsing application logs.</summary>
public sealed class SoilCalculationStep
{
    public int LabSoilId { get; set; }
    public string Rule { get; set; } = "";
    public string Measure { get; set; } = "";
    public string Explanation { get; set; } = "";
    public Dictionary<string, double> Inputs { get; set; } = [];
    public double? Result { get; set; }
}

public sealed class SoilReportSettings
{
    public int SurveyId { get; set; }
    public int TemplateId { get; set; }
    public int RegionId { get; set; } = 1;
    public long Version { get; set; }
    public List<TissueTemplateSelection> TissueTemplates { get; set; } = [];
    public List<TissueReportCropChoice> TissueCrops { get; set; } = [];
    public bool TemplatesComplete => TemplateId > 0 && TissueCrops.All(crop => TissueTemplates.Any(selection => selection.CropId == crop.CropId && selection.TemplateId > 0));
}

public sealed class SoilReportEdit
{
    public long ExpectedVersion { get; set; }
    public Guid ExpectedRevisionId { get; set; }
    public List<SoilReportAction> Actions { get; set; } = [];
    public SoilReportOptions Options { get; set; } = new();
    public List<SoilReportOverride> Overrides { get; set; } = [];
    public List<int> ExcludedLabSoilIds { get; set; } = [];
    public string Reason { get; set; } = "";
}

public sealed class SoilReportOverride
{
    public int LabSoilId { get; set; }
    public string Product { get; set; } = "";
    public double Rate { get; set; }
    public string Reason { get; set; } = "";
}

public sealed class SoilReportTransition
{
    public long ExpectedVersion { get; set; }
    public Guid ExpectedRevisionId { get; set; }
    public string Action { get; set; } = "";
    public double QaHours { get; set; }
    public string Note { get; set; } = "";
    // Submission explicitly confirms the saved preview, separately from editing its actions and rates.
    public bool PreviewConfirmed { get; set; }
}

/// <summary>Current workflow state is separate from the immutable calculation revision.</summary>
public sealed class SoilReportWorkspace
{
    // Read from the saved source template, never inferred from a lab crop or the template's display name.
    public string TemplateCropName { get; set; } = "";
    public bool IsSugarcaneTemplate => TemplateCropName.Trim().Equals("Sugar", StringComparison.OrdinalIgnoreCase)
        || TemplateCropName.Trim().Equals("Sugarcane", StringComparison.OrdinalIgnoreCase);
    // An unresolved match opens the preparation popup before any calculation revision is saved.
    public SoilReportMatchingReview? Matching { get; set; }
    public SoilReportMatchingReview? TissueMatching { get; set; }
    public bool TemplateSelectionRequired { get; set; }
    public List<SoilReportProduct> Products { get; set; } = [];
    public SoilReportSettings Settings { get; set; } = new();
    public SoilReportDocument? Document { get; set; }
    public bool CanApprove { get; set; }
    public string ApprovalMessage { get; set; } = "";
}

public sealed record SoilReportAuditEntry(long Id, Guid? RevisionId, DateTime OccurredUtc, string Actor,
    string Action, string Reason, string PreviousStatus, string Status, double QaHours);
public sealed record SoilReportRevisionSummary(Guid Id, int Number, DateTime CreatedUtc, string CreatedBy, string InputHash);
public sealed record SoilReportListItem(int SurveyId, string PropertyName, DateTime SurveyDate, string Agronomist, string Status, int Revision);

/// <summary>MetaGrow-only exceptions for survey items that were never collected, with an explicit audit reason.</summary>
public sealed class SoilReportNotSampled
{
    public int SurveyItemId { get; set; }
    public string Reason { get; set; } = "";
}

public sealed record SoilReportOmittedSample(int SurveyItemId, string BlockName, long Barcode, string Reason);
public sealed record SoilReportSampleChoice(int SurveyItemId, string BlockName, long Barcode, bool HasLabResult);

public sealed class SoilReportPreparation
{
    public long Version { get; set; }
    public bool CanEdit { get; set; }
    public List<SoilReportSampleChoice> Samples { get; set; } = [];
    public List<SoilReportNotSampled> NotSampled { get; set; } = [];
}

public sealed class SoilReportNotSampledChange
{
    public long ExpectedVersion { get; set; }
    public List<SoilReportNotSampled> Items { get; set; } = [];
}
