using ApiModels.MetaGrow;

namespace MetaGrow.Api.Data;

/// <summary>
/// Approval and audit record for a directional legacy-property merge.
/// The complete reviewed block plan is retained so a reviewer can re-run the
/// live TGS preview immediately before a future atomic merge operation.
/// </summary>
public sealed class PropertyMergeRequest
{
    public Guid Id { get; set; }
    public int SourcePropertyId { get; set; }
    public string SourcePropertyName { get; set; } = string.Empty;
    public int TargetPropertyId { get; set; }
    public string TargetPropertyName { get; set; } = string.Empty;
    public string PlanJson { get; set; } = string.Empty;
    public string PlanHash { get; set; } = string.Empty;
    public MetaGrowPropertyMergeStatus Status { get; set; }
    public string RequestedByUserId { get; set; } = string.Empty;
    public string RequestedByEmail { get; set; } = string.Empty;
    public DateTime RequestedUtc { get; set; }
    public string? ReviewedByUserId { get; set; }
    public string? ReviewedByEmail { get; set; }
    public DateTime? ReviewedUtc { get; set; }
    public string? ReviewNote { get; set; }
    public string? LastError { get; set; }
}
