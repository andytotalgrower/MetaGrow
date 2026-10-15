namespace MetaGrow.Shared.Models;

public sealed record TissueReportCropChoice(int CropId, string CropName);

/// <summary>Only tissue from reported soil blocks is included; all values and template ranges belong to this revision.</summary>
public sealed class TissueReportDocument
{
    public List<TissueReportSample> Samples { get; set; } = [];
    public List<SoilReportMeasure> Measures { get; set; } = [];
    public List<SoilReportSampleMatch> Matches { get; set; } = [];
    public List<string> MissingSamples { get; set; } = [];
    public List<int> ExpectedSoilLabIds { get; set; } = [];
}

public sealed class TissueReportSample
{
    public int LabTissueId { get; set; }
    public int SurveyItemId { get; set; }
    // One tissue sample may accompany multiple soil depths for the same actual block without duplicating the source sample.
    public List<int> SoilLabIds { get; set; } = [];
    public string BlockName { get; set; } = "";
    public string Crop { get; set; } = "";
    public int TemplateId { get; set; }
    public string TemplateName { get; set; } = "";
    public string SamplePart { get; set; } = "";
    public string SampleReference { get; set; } = "";
    public string Laboratory { get; set; } = "";
    public DateTime SampleDate { get; set; }
    private Dictionary<string, SoilReportValue> _values = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, SoilReportValue> Values
    {
        get => _values;
        set => _values = new(value, StringComparer.OrdinalIgnoreCase);
    }
}
