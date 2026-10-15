using System.Security.Cryptography;
using System.Text;
using MetaGrow.Shared.Models;

namespace MetaGrow.Api.Services.SoilReports;

/// <summary>Preserves legacy selection rules; a frozen MetaGrow database snapshot supplies rows and formatting.</summary>
public sealed class SoilReportDefinitions
{
    private readonly Dictionary<string, IReadOnlyList<SoilReportMeasure>> _definitions = new(StringComparer.OrdinalIgnoreCase);
    public string VersionHash { get; }

    // Clone and canonically order the complete database layout, so later edits cannot change an in-flight calculation.
    public IReadOnlyList<SoilReportDefinition> Snapshot { get; }

    public SoilReportDefinitions(IEnumerable<SoilReportDefinition> definitions)
    {
        var snapshot = System.Text.Json.JsonSerializer.Deserialize<List<SoilReportDefinition>>(
            System.Text.Json.JsonSerializer.Serialize(definitions))!;
        snapshot = snapshot.OrderBy(item => item.Key, StringComparer.Ordinal).ToList();
        foreach (var definition in snapshot)
        {
            definition.Sections = definition.Sections.OrderBy(section => section.Order).ThenBy(section => section.Id).ToList();
            foreach (var section in definition.Sections)
                section.Items = section.Items.OrderBy(item => item.Order).ThenBy(item => item.Id).ToList();
            _definitions.Add(definition.Key, definition.Sections.Where(section => section.SampleSurveyVisible)
                .SelectMany(section => section.Items.Select(item => new SoilReportMeasure
                {
                    Key = item.Field, Label = item.Label, ShortName = item.ShortName, Section = section.Name, SectionOrder = section.Order,
                    Order = item.Order, NumberFormat = item.NumberFormat, ShowIfZero = item.ShowIfZero,
                    SectionColour = section.Colour, BackgroundColour = item.BackgroundColour
                })).ToList());
        }
        if (_definitions.Count == 0) throw new InvalidOperationException("Soil report definitions have not been installed in MetaGrow.");
        Snapshot = snapshot;
        VersionHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(snapshot))));
    }

    public IReadOnlyList<SoilReportMeasure> Get(string name) => _definitions.TryGetValue(name, out var rows)
        ? rows : throw new InvalidOperationException($"Unknown soil report definition: {name}.");

    public string Select(string crop, IReadOnlyDictionary<string, double?> values)
    {
        double Value(string key) => values.TryGetValue(key, out var value) ? value ?? 0 : 0;
        var sugar = crop.Contains("sugar", StringComparison.OrdinalIgnoreCase);
        var banana = crop.Contains("banana", StringComparison.OrdinalIgnoreCase);
        var hortus = Value("PhosphorousColwellPpm") > 0 && Value("PhosphorousColwellPpm") == Value("PhosphorousOlsenPpm");
        if (sugar) return hortus ? "LabReportDefinition_Hortus_Soil_Sugar.xml" : "LabReportDefinition_Soil_Sugar.xml";
        if (!banana)
        {
            if (Value("SiliconCaClPpm") > 0 && Value("PhosphorousBsesPpm") > 0 && Value("PhosphorousBufferingIndex") > 0)
                return "LabReportDefinition_Hortus_Soil3.xml";
            if (hortus) return "LabReportDefinition_Hortus.xml";
        }
        if (Value("TotalZincPpm") > 0) return "LabReportDefinition_Soil_Totals.xml";
        if (Value("PhosphorousTotalPpm") > 0) return "LabReportDefinition_Soil_TotalP.xml";
        if (banana)
            return Value("PhosphorousOlsenPpm") > 0 && Value("PhosphorousColwellPpm") > 0 && Value("PhosphorousBsesPpm") > 0 && Value("PhosphorousBufferingIndex") > 0
                ? "LabReportDefinition_Soil_BananaX.xml" : "LabReportDefinition_Soil_Banana.xml";
        return Value("PhosphorousOlsenPpm") > 0 ? "LabReportDefinition_Soil_POlsen.xml" : "LabReportDefinition.xml";
    }
}
