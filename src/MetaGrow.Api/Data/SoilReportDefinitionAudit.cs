namespace MetaGrow.Api.Data;

/// <summary>Append-only layout history, saved in the same transaction as the layout change.</summary>
public sealed class SoilReportDefinitionAudit
{
    public Guid Id { get; set; }
    public string DefinitionKey { get; set; } = "";
    public Guid Version { get; set; }
    public DateTime ChangedUtc { get; set; }
    public string ChangedByUserId { get; set; } = "";
    public string ChangedBy { get; set; } = "";
    public string Reason { get; set; } = "";
    public string BeforeJson { get; set; } = "";
    public string AfterJson { get; set; } = "";
}
