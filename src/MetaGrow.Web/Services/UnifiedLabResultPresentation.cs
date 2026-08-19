using ApiModels;

namespace MetaGrow.Web.Services;

public sealed record UnifiedLabResultRow(
    int RecordId,
    string ResultType,
    DateTime? DateReceived,
    string PropertyName,
    string BlockName,
    string CropName,
    string Reference,
    string OrderNumber,
    DateTime? SurveyDate,
    string KeyResults,
    string? SurveyUrl,
    string? SurveyLabel);

public static class UnifiedLabResultPresentation
{
    public const string Soil = "soil";
    public const string Tissue = "tissue";
    public const string QuickSoil = "quick-soil";
    public const string Sap = "sap";

    public static readonly string[] ResultTypes = [Soil, Tissue, QuickSoil, Sap];

    public static string NormalizeType(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        Tissue => Tissue,
        QuickSoil or "quicksoil" => QuickSoil,
        Sap => Sap,
        _ => Soil
    };

    public static string TypeLabel(string type) => NormalizeType(type) switch
    {
        Tissue => "Tissue",
        QuickSoil => "Quick Soil",
        Sap => "Sap",
        _ => "Soil"
    };

    public static IReadOnlyList<UnifiedLabResultRow> FromSoil(IEnumerable<LabSoilDto> source) => source
        .Select(item => new UnifiedLabResultRow(
            item.LabSoilId,
            "Soil",
            item.DateReceived,
            Display(item.PropertyName),
            Display(item.BlockName),
            Display(item.CropName),
            Display(item.FieldReference),
            Display(item.OrderNo),
            item.SurveyDate,
            Metrics(
                ("pH CaCl₂", item.PhCaCl2),
                ("CEC", item.CecMeq),
                ("NO₃-N", item.No3nPpm),
                ("Ca", item.CalciumPpm),
                ("Mg", item.MagnesiumPpm)),
            SurveyUrl(item.SampleSurveyId, item.VisitId),
            SurveyLabel(item.SampleSurveyId, item.VisitId)))
        .ToList();

    public static IReadOnlyList<UnifiedLabResultRow> FromTissue(IEnumerable<LabTissueDto> source) => source
        .Select(item => new UnifiedLabResultRow(
            item.LabTissueId,
            "Tissue",
            item.DateReceived,
            Display(item.PropertyName),
            Display(item.BlockName),
            Display(item.CropName),
            Display(item.FieldReference),
            Display(item.OrderNo ?? item.CustomerOrderNo),
            item.SurveyDate,
            Metrics(("N", item.NitrogenPc), ("P", item.PhosphorousPc), ("K", item.PotassiumPc), ("Ca", item.CalciumPc)),
            SurveyUrl(item.SampleSurveyId, item.VisitId),
            SurveyLabel(item.SampleSurveyId, item.VisitId)))
        .ToList();

    public static IReadOnlyList<UnifiedLabResultRow> FromQuickSoil(IEnumerable<LabQuickSoilDto> source) => source
        .Select(item => new UnifiedLabResultRow(
            item.LabQuickSoilId,
            "Quick Soil",
            item.DateReceived,
            Display(item.PropertyName ?? item.Grower),
            Display(item.BlockName),
            Display(item.CropName),
            Display(item.FieldReference),
            Display(item.SampleNumber),
            item.SurveyDate,
            Metrics(("pH", item.pH), ("EC", item.ElectricalConductivity), ("NO₃-N", item.NitrateNitrogen), ("K", item.Potassium)),
            SampleSurveyUrl(item.SampleSurveyId),
            SampleSurveyLabel(item.SampleSurveyId)))
        .ToList();

    public static IReadOnlyList<UnifiedLabResultRow> FromSap(IEnumerable<LabSapDto> source) => source
        .Select(item => new UnifiedLabResultRow(
            item.LabSapId,
            "Sap",
            item.DateReceived,
            Display(item.PropertyName ?? item.Grower),
            Display(item.BlockName),
            Display(item.CropName),
            Display(item.FieldReference),
            Display(item.SampleNumber),
            item.SurveyDate,
            Metrics(("Brix", item.Brix), ("pH", item.pH), ("NO₃-N", item.NitrateNitrogen), ("K", item.Potassium)),
            SampleSurveyUrl(item.SampleSurveyId),
            SampleSurveyLabel(item.SampleSurveyId)))
        .ToList();

    private static string Display(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();

    private static string Metrics(params (string Label, decimal Value)[] values) =>
        string.Join(" · ", values.Select(item => $"{item.Label} {item.Value:N2}"));

    private static string Metrics(params (string Label, double Value)[] values) =>
        string.Join(" · ", values.Select(item => $"{item.Label} {item.Value:N2}"));

    private static string? SurveyUrl(int? sampleSurveyId, int visitId) => sampleSurveyId > 0
        ? SampleSurveyUrl(sampleSurveyId.Value)
        : visitId > 0 ? $"/surveys/banana/{visitId}/edit" : null;

    private static string? SurveyLabel(int? sampleSurveyId, int visitId) => sampleSurveyId > 0
        ? SampleSurveyLabel(sampleSurveyId.Value)
        : visitId > 0 ? $"Banana #{visitId}" : null;

    private static string? SampleSurveyUrl(int sampleSurveyId) =>
        sampleSurveyId > 0 ? $"/surveys/samples/{sampleSurveyId}/edit" : null;

    private static string? SampleSurveyLabel(int sampleSurveyId) =>
        sampleSurveyId > 0 ? $"Survey #{sampleSurveyId}" : null;
}
