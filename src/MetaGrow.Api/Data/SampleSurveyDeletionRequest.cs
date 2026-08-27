using ApiModels.MetaGrow;

namespace MetaGrow.Api.Data;

/// <summary>Approval and audit record for a controlled Sample survey deletion.</summary>
public sealed class SampleSurveyDeletionRequest
{
    public Guid Id { get; set; }
    public MetaGrowSurveyType SurveyType { get; set; } = MetaGrowSurveyType.Sample;
    public int SurveyId { get; set; }
    public int PropertyId { get; set; }
    public string PropertyName { get; set; } = string.Empty;
    public DateTime SurveyDate { get; set; }
    public DateTime? ExpectedModificationDate { get; set; }
    public int SampleCount { get; set; }
    public int PhotoCount { get; set; }
    public int ActionCount { get; set; }
    public int LinkedSoilResultCount { get; set; }
    public int LinkedTissueResultCount { get; set; }
    public int LinkedSapResultCount { get; set; }
    public int LinkedQuickSoilResultCount { get; set; }
    public int LinkedLegacyResultCount { get; set; }
    public bool DeleteLinkedLabResults { get; set; }
    public MetaGrowSampleSurveyDeletionStatus Status { get; set; }
    public string RequestedByUserId { get; set; } = string.Empty;
    public string RequestedByEmail { get; set; } = string.Empty;
    public DateTime RequestedUtc { get; set; }
    public string? ReviewedByUserId { get; set; }
    public string? ReviewedByEmail { get; set; }
    public DateTime? ReviewedUtc { get; set; }
    public string? ReviewNote { get; set; }
    public string? LastError { get; set; }
}
