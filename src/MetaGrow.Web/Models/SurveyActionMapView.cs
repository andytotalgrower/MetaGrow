namespace MetaGrow.Web.Models;

/// <summary>Presentation-only map data; database contracts remain in ApiModels.</summary>
public sealed class SurveyActionMapView
{
    public List<SurveyActionMapPanel> Maps { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    // Keep missing boundaries separate from diagnostic text for the online disclosure.
    public List<string> MissingKmlBlocks { get; set; } = [];
    public List<SurveyActionWorkSummary> WorkSummary { get; set; } = [];
}

/// <summary>Worker-facing instructions grouped independently of geometry and combined map colours.</summary>
public sealed record SurveyActionWorkSummary(string Category, string Recommendation, IReadOnlyList<string> Blocks);

public sealed class SurveyActionMapPanel
{
    // Include outside callouts in the SVG bounds so printing cannot clip their names.
    public string ViewBox { get; set; } = "0 0 1000 650";
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public List<SurveyActionMapShape> Shapes { get; set; } = [];
    public List<SurveyActionMapLegend> Legend { get; set; } = [];
    // Visibility is deliberately local to this report session and also controls printing.
    public bool Visible { get; set; } = true;
}

public sealed record SurveyActionMapLegend(int Number, string Summary, string Colour, string TextColour)
{
    // Key entries describe all assigned blocks, including those whose boundaries are unavailable.
    public IReadOnlyList<string> Blocks { get; init; } = [];
    // Map-key text includes each block's measured area while preserving its plain name above.
    public IReadOnlyList<string> BlockDescriptions { get; init; } = [];
    // Sum each assigned block once, even when it consists of several polygons.
    public double? AreaHectares { get; init; }
    public bool HasIncompleteArea { get; init; }
}
public sealed record SurveyActionMapShape(string BlockName, string Path, double LabelX, double LabelY,
    string Summary, string Colour, string TextColour, int? LegendNumber)
{
    // Combined area across the block's polygons, independent of the visible SVG label.
    public double AreaHectares { get; init; }
    public SurveyActionMapLabel? Label { get; init; }
}

/// <summary>A point in the map's SVG coordinate space.</summary>
public readonly record struct SurveyMapPoint(double X, double Y);

/// <summary>A complete block name laid out inside its polygon or in an outside callout.</summary>
public sealed record SurveyActionMapLabel(double X, double Y, double Width, double Height,
    double FontSize, IReadOnlyList<string> Lines, SurveyMapPoint Anchor, bool IsCallout);
