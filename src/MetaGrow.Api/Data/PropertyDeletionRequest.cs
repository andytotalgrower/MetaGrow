using ApiModels.MetaGrow;

namespace MetaGrow.Api.Data;

/// <summary>
/// Approval and audit record for deleting an unreferenced legacy property.
/// Agronomists create pending rows; managers and administrators perform the deletion.
/// </summary>
public sealed class PropertyDeletionRequest
{
    public Guid Id { get; set; }
    public int PropertyId { get; set; }
    public string PropertyName { get; set; } = string.Empty;
    public MetaGrowPropertyDeletionStatus Status { get; set; }
    public string RequestedByUserId { get; set; } = string.Empty;
    public string RequestedByEmail { get; set; } = string.Empty;
    public DateTime RequestedUtc { get; set; }
    public string? ReviewedByUserId { get; set; }
    public string? ReviewedByEmail { get; set; }
    public DateTime? ReviewedUtc { get; set; }
    public string? ReviewNote { get; set; }
    public string? LastError { get; set; }
}
