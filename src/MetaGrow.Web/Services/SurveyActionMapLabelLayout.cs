using MetaGrow.Web.Models;

namespace MetaGrow.Web.Services;

/// <summary>Places complete block names in usable polygon space, with outside callouts as a fallback.</summary>
public static class SurveyActionMapLabelLayout
{
    private const double _padding = 3;
    private const double _calloutWidth = 170;
    private sealed record Candidate(SurveyMapPoint Point, double Clearance);

    public static IReadOnlyList<SurveyActionMapLabel> Build(
        IReadOnlyList<(string Name, List<List<SurveyMapPoint>> Rings)> polygons)
    {
        var labels = polygons.Select(polygon => PlaceInside(polygon.Name, polygon.Rings)).ToList();
        var points = polygons.SelectMany(polygon => polygon.Rings[0]).ToList();
        if (points.Count == 0) return labels;
        var left = points.Min(point => point.X);
        var right = points.Max(point => point.X);
        var middle = (left + right) / 2;

        // Separate left/right gutters and sort by anchor height to keep leaders and boxes orderly.
        foreach (var isLeft in new[] { true, false })
        {
            var indexes = Enumerable.Range(0, labels.Count)
                .Where(index => labels[index].IsCallout && (labels[index].Anchor.X <= middle) == isLeft)
                .OrderBy(index => labels[index].Anchor.Y).ToList();
            var bottom = 12d;
            foreach (var index in indexes)
            {
                var label = labels[index];
                var y = Math.Max(bottom, label.Anchor.Y - label.Height / 2);
                labels[index] = label with { X = isLeft ? left - _calloutWidth - 18 : right + 18, Y = y };
                bottom = y + label.Height + 5;
            }
            // Move a crowded column upward together while retaining the gap between names.
            var shift = Math.Min(Math.Max(0, bottom - 638), indexes.Count == 0 ? 0 : labels[indexes[0]].Y - 12);
            foreach (var index in indexes) labels[index] = labels[index] with { Y = labels[index].Y - shift };
        }
        return labels;
    }

    public static string ViewBox(IReadOnlyList<SurveyActionMapLabel> labels)
    {
        // Preserve the normal farm bounds and include every outside box with a small safety margin.
        var left = Math.Min(0, labels.Select(label => label.X - 6).DefaultIfEmpty(0).Min());
        var top = Math.Min(0, labels.Select(label => label.Y - 6).DefaultIfEmpty(0).Min());
        var right = Math.Max(1000, labels.Select(label => label.X + label.Width + 6).DefaultIfEmpty(1000).Max());
        var bottom = Math.Max(650, labels.Select(label => label.Y + label.Height + 6).DefaultIfEmpty(650).Max());
        return string.Join(" ", new[] { left, top, right - left, bottom - top }.Select(SurveyActionMapLayout.Number));
    }

    private static SurveyActionMapLabel PlaceInside(string name, List<List<SurveyMapPoint>> rings)
    {
        var outer = rings[0];
        var left = outer.Min(point => point.X);
        var top = outer.Min(point => point.Y);
        var width = outer.Max(point => point.X) - left;
        var height = outer.Max(point => point.Y) - top;
        var candidates = new List<Candidate>();
        // Bounded sampling finds usable space in concave polygons and either side of holes.
        for (var row = 0; row < 25; row++)
        {
            for (var column = 0; column < 25; column++)
            {
                var point = new SurveyMapPoint(left + width * (column + .5) / 25, top + height * (row + .5) / 25);
                if (Contains(point, rings)) candidates.Add(new(point, Clearance(point, rings)));
            }
        }
        var preferred = candidates.OrderByDescending(candidate => candidate.Clearance).ToList();
        var anchor = preferred.FirstOrDefault()?.Point ?? outer[0];

        // Prefer the existing 11px font, then reduce only as far as 10px for print readability.
        foreach (var fontSize in new[] { 11d, 10d })
        {
            for (var lineCount = 1; lineCount <= 3; lineCount++)
            {
                var lines = Wrap(name, Math.Max(1, width - _padding * 2), fontSize, lineCount);
                var labelWidth = lines.Max(line => TextWidth(line, fontSize)) + _padding * 2;
                var labelHeight = lines.Count * fontSize * 1.2 + _padding * 2;
                if (lines.Count > lineCount || labelWidth > width || labelHeight > height) continue;
                foreach (var candidate in preferred)
                {
                    var label = new SurveyActionMapLabel(candidate.Point.X - labelWidth / 2,
                        candidate.Point.Y - labelHeight / 2, labelWidth, labelHeight, fontSize, lines, candidate.Point, false);
                    if (Fits(label, rings)) return label;
                }
            }
        }

        // Outside labels wrap freely and retain every character, including unusually long single words.
        var calloutLines = Wrap(name, _calloutWidth - _padding * 2, 11, int.MaxValue);
        return new(0, 0, _calloutWidth, calloutLines.Count * 13.2 + _padding * 2, 11, calloutLines, anchor, true);
    }

    private static List<string> Wrap(string name, double maximumWidth, double fontSize, int targetLines)
    {
        var words = name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var balancedWidth = targetLines == int.MaxValue ? maximumWidth : Math.Min(maximumWidth, Math.Max(
            words.Select(word => TextWidth(word, fontSize)).DefaultIfEmpty(0).Max(),
            TextWidth(name, fontSize) / targetLines + fontSize));
        var lines = new List<string>();
        var current = "";
        foreach (var word in words)
        {
            if (current.Length > 0 && TextWidth(current + " " + word, fontSize) > balancedWidth)
            {
                lines.Add(current);
                current = "";
            }
            if (targetLines == int.MaxValue && TextWidth(word, fontSize) > maximumWidth)
            {
                // Split an oversized token at character boundaries without dropping its suffix.
                foreach (var character in word)
                {
                    if (TextWidth(current + character, fontSize) > maximumWidth)
                    {
                        lines.Add(current);
                        current = "";
                    }
                    current += character;
                }
            }
            else current = current.Length == 0 ? word : current + " " + word;
        }
        if (current.Length > 0) lines.Add(current.TrimEnd());
        return lines.Count > 0 ? lines : [""];
    }

    // Conservative Arial metrics leave room for bold strokes and full-width characters.
    private static double TextWidth(string text, double fontSize) => text.Sum(character =>
        fontSize * (char.IsWhiteSpace(character) ? .35 : "MW@mw".Contains(character) || character >= '\u2e80'
            ? 1 : "ilI.,'".Contains(character) ? .35 : .75));

    private static bool Fits(SurveyActionMapLabel label, List<List<SurveyMapPoint>> rings)
    {
        // All corners must be inside, and no boundary (including a hole) may cross the text box.
        var corners = new[] { new SurveyMapPoint(label.X, label.Y), new SurveyMapPoint(label.X + label.Width, label.Y),
            new SurveyMapPoint(label.X, label.Y + label.Height), new SurveyMapPoint(label.X + label.Width, label.Y + label.Height) };
        if (corners.Any(point => !Contains(point, rings))) return false;
        foreach (var ring in rings)
        {
            var previous = ring[^1];
            foreach (var point in ring)
            {
                if (Intersects(previous, point, label)) return false;
                previous = point;
            }
        }
        return true;
    }

    private static bool Contains(SurveyMapPoint point, List<List<SurveyMapPoint>> rings)
    {
        // Even/odd ray casting handles outer boundaries and holes regardless of winding.
        var inside = false;
        foreach (var ring in rings)
        {
            var previous = ring[^1];
            foreach (var vertex in ring)
            {
                if ((vertex.Y > point.Y) != (previous.Y > point.Y) &&
                    point.X < (previous.X - vertex.X) * (point.Y - vertex.Y) / (previous.Y - vertex.Y) + vertex.X)
                    inside = !inside;
                previous = vertex;
            }
        }
        return inside;
    }

    private static double Clearance(SurveyMapPoint point, List<List<SurveyMapPoint>> rings)
    {
        var distance = double.MaxValue;
        foreach (var ring in rings)
        {
            var previous = ring[^1];
            foreach (var vertex in ring)
            {
                var dx = vertex.X - previous.X;
                var dy = vertex.Y - previous.Y;
                var length = dx * dx + dy * dy;
                var fraction = length == 0 ? 0 : Math.Clamp(((point.X - previous.X) * dx + (point.Y - previous.Y) * dy) / length, 0, 1);
                var x = previous.X + fraction * dx - point.X;
                var y = previous.Y + fraction * dy - point.Y;
                distance = Math.Min(distance, x * x + y * y);
                previous = vertex;
            }
        }
        return distance;
    }

    private static bool Intersects(SurveyMapPoint start, SurveyMapPoint end, SurveyActionMapLabel box)
    {
        // Clip the boundary segment against the label rectangle, including fully enclosed hole edges.
        var minimum = 0d;
        var maximum = 1d;
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        return Clip(-dx, start.X - box.X, ref minimum, ref maximum) &&
            Clip(dx, box.X + box.Width - start.X, ref minimum, ref maximum) &&
            Clip(-dy, start.Y - box.Y, ref minimum, ref maximum) &&
            Clip(dy, box.Y + box.Height - start.Y, ref minimum, ref maximum);
    }

    private static bool Clip(double direction, double distance, ref double minimum, ref double maximum)
    {
        if (direction == 0) return distance >= 0;
        var fraction = distance / direction;
        if (direction < 0) minimum = Math.Max(minimum, fraction);
        else maximum = Math.Min(maximum, fraction);
        return minimum <= maximum;
    }
}
