using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using ApiModels;
using MetaGrow.Web.Models;

namespace MetaGrow.Web.Services;

/// <summary>Projects KML into printable SVG, without external tiles, scripts, or map API keys.</summary>
public static class SurveyActionMapLayout
{
    private sealed record Point(double X, double Y);
    private sealed record Geometry(SurveyMapBlockDto Block, List<List<Point>> Rings);
    private static readonly string[][] _palettes =
    [
        ["#d1ef71", "#8eb027", "#5f720f", "#abfdca", "#41ff91", "#00db8b", "#00257b", "#04515e"],
        ["#ffa8a3", "#f85a5a", "#b42a2a", "#821414", "#ff0000", "#ff6900", "#ffa900", "#ffff00"],
        ["#6d6461", "#ab9a85", "#c5b58d", "#e5d593", "#cda753", "#d68474", "#8a381a", "#3a2714"],
        ["#0900ff", "#009fff", "#00dfff", "#b0d3ee", "#7db1d9", "#3677a8"],
        ["#ffd29d", "#f6a145", "#00c6e5", "#0080cc", "#002b55", "#fd7d00", "#f2fb7a", "#d87414"]
    ];

    public static SurveyActionMapView Build(SurveyActionMapDto data, string surveyType)
    {
        var result = new SurveyActionMapView { Warnings = [.. data.Warnings] };
        if (data.Recommendations.Count == 0) return result;
        // Plan work from recommendations, not polygons: blocks without KML still need their work done.
        // Internal identifiers are only for matching; user-facing lists contain actual names only.
        var blockNames = data.Blocks.Where(block => block.BlockType == surveyType && !string.IsNullOrWhiteSpace(block.BlockName))
            .GroupBy(block => block.BlockId).ToDictionary(group => group.Key, group => group.First().BlockName);
        result.WorkSummary = data.Recommendations.Where(action => !string.IsNullOrWhiteSpace(action.Summary))
            .GroupBy(action => new { action.ActionGroupId, action.Summary })
            .OrderBy(group => group.Key.ActionGroupId).ThenBy(group => group.Key.Summary, StringComparer.Ordinal)
            .Select(group => new SurveyActionWorkSummary(group.First().ActionGroupName, group.Key.Summary,
                group.SelectMany(action => action.BlockIds).Distinct()
                    .Where(blockNames.ContainsKey).Select(id => blockNames[id])
                    .Order(StringComparer.OrdinalIgnoreCase).ToList())).ToList();
        var geometry = new List<Geometry>();
        foreach (var block in data.Blocks)
        {
            var count = geometry.Count;
            foreach (var kml in block.Kml)
            {
                try
                {
                    foreach (var rings in ReadPolygons(kml)) geometry.Add(new(block, rings));
                }
                catch (Exception exception) when (exception is XmlException or FormatException or InvalidOperationException)
                {
                    result.Warnings.Add($"Invalid map data for {block.BlockName}; valid polygons are still shown.");
                }
            }
            if (geometry.Count == count) result.MissingKmlBlocks.Add(block.BlockName);
        }
        // Unnamed blocks are covered by the availability warning, never by an internal-ID label.
        result.MissingKmlBlocks = result.MissingKmlBlocks.Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct().Order(StringComparer.OrdinalIgnoreCase).ToList();
        if (geometry.Count == 0)
        {
            result.Warnings.Add("No block boundaries are available for this survey. Recommendations remain available in the report.");
            result.Warnings = result.Warnings.Distinct().ToList();
            return result;
        }

        var availableIds = geometry.Where(shape => shape.Block.BlockType == surveyType).Select(shape => shape.Block.BlockId).ToHashSet();
        var missingIds = data.Recommendations.SelectMany(action => action.BlockIds).Distinct().Where(id => !availableIds.Contains(id)).ToList();
        if (missingIds.Count > 0)
            result.Warnings.Add($"Recommendations for {missingIds.Count} block(s) cannot be drawn because their boundaries are unavailable.");

        // Use a local equirectangular projection: longitude is scaled at the farm's latitude.
        var points = geometry.SelectMany(shape => shape.Rings.SelectMany(ring => ring)).ToList();
        var longitudeScale = Math.Max(.01, Math.Cos(points.Average(point => point.Y) * Math.PI / 180));
        var minX = points.Min(point => point.X) * longitudeScale;
        var maxX = points.Max(point => point.X) * longitudeScale;
        var minY = points.Min(point => point.Y);
        var maxY = points.Max(point => point.Y);
        var scale = Math.Min(920 / Math.Max(maxX - minX, .000001), 570 / Math.Max(maxY - minY, .000001));
        var offsetX = (1000 - (maxX - minX) * scale) / 2;
        var offsetY = (650 - (maxY - minY) * scale) / 2;
        Point Project(Point point) => new(offsetX + (point.X * longitudeScale - minX) * scale, offsetY + (maxY - point.Y) * scale);

        foreach (var category in data.Recommendations.GroupBy(action => action.ActionGroupId).OrderBy(group => group.Key))
        {
            foreach (var name in category.Select(action => action.MapName).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            {
                // An unnamed legacy map contains all recommendations in its category.
                var actions = category.Where(action => name.Length == 0 || action.MapName == name).ToList();
                var summaries = actions.SelectMany(action => action.BlockIds).Distinct().ToDictionary(blockId => blockId,
                    blockId => string.Join(" + ", actions.Where(action => action.BlockIds.Contains(blockId))
                        .OrderBy(action => action.SurveyActionId).Select(action => action.Summary).Where(summary => summary.Length > 0)));
                var mappedIds = geometry.Where(shape => shape.Block.BlockType == surveyType).Select(shape => shape.Block.BlockId).ToHashSet();
                if (!summaries.Any(summary => summary.Value.Length > 0 && mappedIds.Contains(summary.Key)))
                {
                    result.Warnings.Add($"No mapped blocks for {category.First().ActionGroupName}{(name.Length > 0 ? $" ({name})" : "")}.");
                    continue;
                }
                var panel = new SurveyActionMapPanel
                {
                    Key = $"{category.Key}:{name}",
                    Title = category.First().ActionGroupName + (name.Length == 0 ? " — all recommendations" : $" — {name}")
                };
                var distinct = summaries.Values.Where(summary => summary.Length > 0).Distinct().Order(StringComparer.Ordinal).ToList();
                for (var index = 0; index < distinct.Count; index++)
                {
                    var palette = _palettes[Math.Clamp(category.Key - 1, 0, _palettes.Length - 1)];
                    var colour = palette[index % palette.Length];
                    panel.Legend.Add(new(index + 1, distinct[index], colour, TextColour(colour))
                    {
                        Blocks = summaries.Where(summary => summary.Value == distinct[index])
                            .Where(summary => blockNames.ContainsKey(summary.Key)).Select(summary => blockNames[summary.Key])
                            .Order(StringComparer.OrdinalIgnoreCase).ToList()
                    });
                }
                // Legend numbers disambiguate colours when a category exceeds the legacy palette size.
                foreach (var shape in geometry)
                {
                    var summary = shape.Block.BlockType == surveyType ? summaries.GetValueOrDefault(shape.Block.BlockId, "") : "";
                    var legend = panel.Legend.FirstOrDefault(item => item.Summary == summary);
                    var projected = shape.Rings.Select(ring => ring.Select(Project).ToList()).ToList();
                    var outer = projected[0];
                    var path = string.Join(" ", projected.Select(ring => "M " + string.Join(" L ", ring.Select(point => $"{Number(point.X)} {Number(point.Y)}")) + " Z"));
                    panel.Shapes.Add(new(shape.Block.BlockName, path,
                        (outer.Min(point => point.X) + outer.Max(point => point.X)) / 2,
                        (outer.Min(point => point.Y) + outer.Max(point => point.Y)) / 2,
                        summary, legend?.Colour ?? "#d4d4d4", legend?.TextColour ?? "#333333", legend?.Number));
                }
                result.Maps.Add(panel);
            }
        }
        result.Warnings = result.Warnings.Distinct().ToList();
        return result;
    }

    public static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string TextColour(string colour)
    {
        var red = Convert.ToInt32(colour.Substring(1, 2), 16);
        var green = Convert.ToInt32(colour.Substring(3, 2), 16);
        var blue = Convert.ToInt32(colour.Substring(5, 2), 16);
        return red * .299 + green * .587 + blue * .114 > 145 ? "#111111" : "#ffffff";
    }

    private static IEnumerable<List<List<Point>>> ReadPolygons(string kml)
    {
        // Uploaded KML is untrusted XML. Disable external entities and bound document size.
        using var reader = XmlReader.Create(new StringReader(kml), new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 2_000_000
        });
        var document = XDocument.Load(reader);
        foreach (var polygon in document.Descendants().Where(element => element.Name.LocalName == "Polygon"))
        {
            var boundaries = polygon.Elements().Where(element => element.Name.LocalName is "outerBoundaryIs" or "innerBoundaryIs")
                .OrderBy(element => element.Name.LocalName == "outerBoundaryIs" ? 0 : 1).ToList();
            if (!boundaries.Any(element => element.Name.LocalName == "outerBoundaryIs")) continue;
            var rings = new List<List<Point>>();
            foreach (var boundary in boundaries)
            {
                var coordinates = boundary.Descendants().FirstOrDefault(element => element.Name.LocalName == "coordinates")?.Value ?? "";
                var ring = new List<Point>();
                foreach (var coordinate in coordinates.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                {
                    var values = coordinate.Split(',');
                    if (values.Length < 2 || !double.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude) ||
                        !double.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude) ||
                        !double.IsFinite(longitude) || !double.IsFinite(latitude) || Math.Abs(longitude) > 180 || Math.Abs(latitude) > 90)
                        throw new FormatException("Invalid KML coordinate.");
                    ring.Add(new(longitude, latitude));
                }
                if (ring.Distinct().Count() < 3) throw new FormatException("A polygon needs at least three distinct vertices.");
                rings.Add(ring);
            }
            if (rings.Count > 0) yield return rings;
        }
    }
}
