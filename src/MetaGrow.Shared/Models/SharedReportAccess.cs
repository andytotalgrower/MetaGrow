namespace MetaGrow.Shared.Models;

/// <summary>Validated share access includes the creator's current report preference.</summary>
public sealed class SharedReportAccess
{
    public int SurveyId { get; set; }
    public string ReportArea { get; set; } = ApiModels.MetaGrow.MetaGrowReportAreas.MultiCrop;
    public string BrandKey { get; set; } = ReportBrandPreference._metagen;
    public SoilReportDocument? SoilReport { get; set; }
}
