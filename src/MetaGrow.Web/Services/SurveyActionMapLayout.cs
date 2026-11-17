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
    private sealed record Geometry(SurveyMapBlockDto Block, List<List<Point>> Rings, double AreaHectares);
    private const double _earthRadiusMetres = 6_371_009;
    private static readonly string[][] _palettes =
    [
        // Keep the olive nutrition shade light enough for dark callout leaders on screen and paper.
        ["#d1ef71", "#8eb027", "#cad69d", "#abfdca", "#41ff91", "#00db8b", "#00257b", "#04515e"],
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
        var incompleteAreas = new HashSet<(string Type, int Id)>();
        foreach (var block in data.Blocks)
        {
            // The API may retain successful files from a folder whose other files failed to load.
            if (data.Warnings.Contains($"Map data could not be loaded for {block.BlockName}.", StringComparer.Ordinal))
                incompleteAreas.Add((block.BlockType, block.BlockId));
            var count = geometry.Count;
            foreach (var kml in block.Kml)
            {
                try
                {
                    foreach (var rings in ReadPolygons(kml))
                        geometry.Add(new(block, rings, PolygonAreaHectares(rings)));
                }
                catch (Exception exception) when (exception is XmlException or FormatException or InvalidOperationException)
                {
                    incompleteAreas.Add((block.BlockType, block.BlockId));
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
        // Keep crop type in the key because banana and multi-crop identifiers can collide.
        var blockAreas = geometry.GroupBy(shape => (shape.Block.BlockType, shape.Block.BlockId))
            .ToDictionary(group => group.Key, group => group.Sum(shape => shape.AreaHectares));
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
        SurveyMapPoint Project(Point point) => new(offsetX + (point.X * longitudeScale - minX) * scale, offsetY + (maxY - point.Y) * scale);

        // Label placement depends on farm geometry, so calculate it once for every category map.
        var projectedGeometry = geometry.Select(shape => shape.Rings.Select(ring => ring.Select(Project).ToList()).ToList()).ToList();
        var labels = SurveyActionMapLabelLayout.Build(geometry.Select((shape, index) =>
            (shape.Block.BlockName, projectedGeometry[index])).ToList());

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
                    ViewBox = SurveyActionMapLabelLayout.ViewBox(labels),
                    Key = $"{category.Key}:{name}",
                    Title = category.First().ActionGroupName + (name.Length == 0 ? " — all recommendations" : $" — {name}")
                };
                var distinct = summaries.Values.Where(summary => summary.Length > 0).Distinct().Order(StringComparer.Ordinal).ToList();
                for (var index = 0; index < distinct.Count; index++)
                {
                    var palette = _palettes[Math.Clamp(category.Key - 1, 0, _palettes.Length - 1)];
                    var colour = palette[index % palette.Length];
                    var assignedIds = summaries.Where(summary => summary.Value == distinct[index]).Select(summary => summary.Key).ToList();
                    var measuredIds = assignedIds.Where(id => blockAreas.ContainsKey((surveyType, id))).ToList();
                    panel.Legend.Add(new(index + 1, distinct[index], colour, TextColour(colour))
                    {
                        AreaHectares = measuredIds.Count > 0 ? measuredIds.Sum(id => blockAreas[(surveyType, id)]) : null,
                        HasIncompleteArea = measuredIds.Count != assignedIds.Count ||
                            assignedIds.Any(id => incompleteAreas.Contains((surveyType, id))),
                        // Keep unavailable measurements explicit in the same list as measured blocks.
                        BlockDescriptions = assignedIds.Where(blockNames.ContainsKey)
                            .OrderBy(id => blockNames[id], StringComparer.OrdinalIgnoreCase)
                            .Select(id => blockAreas.TryGetValue((surveyType, id), out var area)
                                ? $"{blockNames[id]} ({Hectares(area)})"
                                : $"{blockNames[id]} (area unavailable)").ToList(),
                        Blocks = summaries.Where(summary => summary.Value == distinct[index])
                            .Where(summary => blockNames.ContainsKey(summary.Key)).Select(summary => blockNames[summary.Key])
                            .Order(StringComparer.OrdinalIgnoreCase).ToList()
                    });
                }
                // Reuse the same label positions for every recommendation category.
                for (var shapeIndex = 0; shapeIndex < geometry.Count; shapeIndex++)
                {
                    var shape = geometry[shapeIndex];
                    var summary = shape.Block.BlockType == surveyType ? summaries.GetValueOrDefault(shape.Block.BlockId, "") : "";
                    var legend = panel.Legend.FirstOrDefault(item => item.Summary == summary);
                    var projected = projectedGeometry[shapeIndex];
                    var outer = projected[0];
                    var path = string.Join(" ", projected.Select(ring => "M " + string.Join(" L ", ring.Select(point => $"{Number(point.X)} {Number(point.Y)}")) + " Z"));
                    panel.Shapes.Add(new(shape.Block.BlockName, path,
                        (outer.Min(point => point.X) + outer.Max(point => point.X)) / 2,
                        (outer.Min(point => point.Y) + outer.Max(point => point.Y)) / 2,
                        summary, legend?.Colour ?? "#d4d4d4", legend?.TextColour ?? "#333333", legend?.Number)
                    {
                        AreaHectares = blockAreas[(shape.Block.BlockType, shape.Block.BlockId)],
                        Label = labels[shapeIndex]
                    });
                }
                result.Maps.Add(panel);
            }
        }
        result.Warnings = result.Warnings.Distinct().ToList();
        return result;
    }

    public static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    public static string Hectares(double value) => value.ToString("0.00", CultureInfo.InvariantCulture) + " ha";

    private static double PolygonAreaHectares(List<List<Point>> rings)
    {
        // KML identifies outer boundaries and holes explicitly, irrespective of winding direction.
        var area = RingAreaHectares(rings[0]) - rings.Skip(1).Sum(RingAreaHectares);
        if (!double.IsFinite(area) || area < 0) throw new FormatException("Invalid polygon area.");
        return area;
    }

    private static double RingAreaHectares(List<Point> ring)
    {
        // Reuse the spherical polar-triangle calculation in TgsWebsite/TgsClientPortal BlockPolygon.
        // Work in geographic coordinates, before projection into the SVG, and close open rings.
        var previous = ring[^1];
        var previousTanLatitude = Math.Tan((Math.PI / 2 - previous.Y * Math.PI / 180) / 2);
        var previousLongitude = previous.X * Math.PI / 180;
        var total = 0d;
        foreach (var point in ring)
        {
            var tanLatitude = Math.Tan((Math.PI / 2 - point.Y * Math.PI / 180) / 2);
            var longitude = point.X * Math.PI / 180;
            var longitudeDelta = longitude - previousLongitude;
            var product = tanLatitude * previousTanLatitude;
            total += 2 * Math.Atan2(product * Math.Sin(longitudeDelta), 1 + product * Math.Cos(longitudeDelta));
            previousTanLatitude = tanLatitude;
            previousLongitude = longitude;
        }
        return Math.Abs(total) * _earthRadiusMetres * _earthRadiusMetres / 10_000;
    }

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
