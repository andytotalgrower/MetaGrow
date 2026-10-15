namespace MetaGrow.Shared.Models;

/// <summary>Editable report layout. The stable key retains the legacy selection mapping; names are user-facing.</summary>
public sealed class SoilReportDefinition
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public Guid Version { get; set; }
    public List<SoilReportDefinitionSection> Sections { get; set; } = [];
}

public sealed class SoilReportDefinitionSection
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Colour { get; set; } = "#0077c1";
    public int Order { get; set; }
    // Preserve visit-only rows during conversion, but omit them from Sample survey reports.
    public bool SampleSurveyVisible { get; set; } = true;
    public List<SoilReportDefinitionItem> Items { get; set; } = [];
}

public sealed class SoilReportDefinitionItem
{
    public int Id { get; set; }
    // Legacy XML reused IDs for different measures; retain provenance separately from the database identity.
    public int? LegacyItemId { get; set; }
    public string Field { get; set; } = "";
    public string Label { get; set; } = "";
    // Optional overview heading; omit nulls so unchanged legacy definition hashes stay stable.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? ShortName { get; set; }
    public int Order { get; set; }
    public string NumberFormat { get; set; } = "#,##0.0";
    public bool ShowIfZero { get; set; } = true;
    public string? BackgroundColour { get; set; }
}

public sealed record SoilDefinitionField(string Key, string Label);
public sealed record SoilDefinitionChange(Guid Id, DateTime ChangedUtc, string ChangedBy, string Reason, Guid Version);
public sealed record SaveSoilDefinition(SoilReportDefinition Definition, string Reason);
