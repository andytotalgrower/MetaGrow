namespace MetaGrow.Api.Data;

/// <summary>A permanent, revocable bearer link granting read-only access to one report.</summary>
public sealed class ReportShare
{
    public Guid Id { get; set; }
    public int SurveyId { get; set; }
    public string ReportArea { get; set; } = ApiModels.MetaGrow.MetaGrowReportAreas.MultiCrop;
    public string Name { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public string ProtectedToken { get; set; } = string.Empty;
    public string CreatedByUserId { get; set; } = string.Empty;
    public string CreatedByEmail { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }
    public DateTime? RevokedUtc { get; set; }
    public string? RevokedByUserId { get; set; }
    public string? RevokedByEmail { get; set; }
    public DateTime? LastViewedUtc { get; set; }
    public long ViewCount { get; set; }
}
