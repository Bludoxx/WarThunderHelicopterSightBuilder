using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace HeliSightBuilder.Native;

public static partial class SightLogic
{
    public const int ImportedGameSegmentTarget = 3500;
    [GeneratedRegex(@"[-+]?(?:\d+\.\d+|\d+|\.\d+)(?:[eE][-+]?\d+)?")]
    private static partial Regex NumberRegex();

    [GeneratedRegex(@"\[(VECTOR_[A-Z_]+),([^\]]+)\]")]
    public static partial Regex CommandRegex();

    public static string Number(double value)
    {
        if (!double.IsFinite(value))
            throw new InvalidDataException("Sight geometry contains a non-finite coordinate.");
        if (Math.Abs(value) < .000001) value = 0;
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    public static string Commands(IEnumerable<SightItem> items, double scale = 1)
    {
        if (!double.IsFinite(scale) || scale <= 0)
            throw new InvalidDataException("Sight scale must be a positive finite number.");
        var result = items.Select(raw =>
        {
            if (!EditorStateRules.IsValidItem(raw))
                throw new InvalidDataException("Sight geometry contains an invalid or excessively large coordinate.");
            var i = raw.Scale(scale);
            if (!EditorStateRules.IsValidItem(i))
                throw new InvalidDataException("Scaled sight geometry exceeds the supported coordinate range.");
            return i.Kind switch
            {
                SightItemKind.Line => $"[VECTOR_LINE, {Number(i.X1)}, {Number(i.Y1)}, {Number(i.X2)}, {Number(i.Y2)}]",
                SightItemKind.Ellipse => $"[VECTOR_ELLIPSE, {Number(i.X1)}, {Number(i.Y1)}, {Number(Math.Abs(i.X2))}, {Number(Math.Abs(i.Y2))}]",
                SightItemKind.FilledEllipse => $"[VECTOR_FILLED_ELLIPSE, {Number(i.X1)}, {Number(i.Y1)}, {Number(Math.Abs(i.X2))}, {Number(Math.Abs(i.Y2))}]",
                _ => $"[VECTOR_RECTANGLE, {Number(i.X1)}, {Number(i.Y1)}, {Number(i.X2)}, {Number(i.Y2)}]"
            };
        }).ToArray();
        if (result.Length == 0) throw new InvalidOperationException("Custom sight is empty.");
        return string.Join(",\n", result);
    }

    public static List<SightItem> Preset(string name, double size, double gap)
    {
        var h = size / 2;
        return name switch
        {
            "Dot" => FilledDot(0, 0, Math.Max(.3, h)),
            "Circle" => [new(SightItemKind.Ellipse, 0, 0, Math.Max(.3, h), Math.Max(.3, h))],
            "Cross" => [new(SightItemKind.Line, -h, 0, -gap, 0), new(SightItemKind.Line, gap, 0, h, 0),
                new(SightItemKind.Line, 0, -h, 0, -gap), new(SightItemKind.Line, 0, gap, 0, h)],
            "Box" => [new(SightItemKind.Rectangle, -h, -h, size, size)],
            "T Sight" => [new(SightItemKind.Line, 0, -h, 0, h), new(SightItemKind.Line, -h * .6, -h, h * .6, -h)],
            _ => throw new ArgumentOutOfRangeException(nameof(name))
        };
    }

    public static List<SightItem> FilledDot(double centerX, double centerY, double radius)
    {
        radius = Math.Max(.001, Math.Abs(radius));
        return [new(SightItemKind.FilledEllipse, centerX, centerY, radius, radius)];
    }

    public static List<SightItem> Part(string name)
    {
        const double h = 9, s = 3.96, gap = 3;
        return name switch
        {
            "Crosshair" => [new(SightItemKind.Line, -h, 0, -gap, 0), new(SightItemKind.Line, gap, 0, h, 0),
                new(SightItemKind.Line, 0, -h, 0, -gap), new(SightItemKind.Line, 0, gap, 0, h)],
            "Brackets" => [new(SightItemKind.Line, -h, -h, -h + s, -h), new(SightItemKind.Line, -h, -h, -h, -h + s),
                new(SightItemKind.Line, h, -h, h - s, -h), new(SightItemKind.Line, h, -h, h, -h + s),
                new(SightItemKind.Line, -h, h, -h + s, h), new(SightItemKind.Line, -h, h, -h, h - s),
                new(SightItemKind.Line, h, h, h - s, h), new(SightItemKind.Line, h, h, h, h - s)],
            "Chevron" => [new(SightItemKind.Line, -h, -s, 0, h), new(SightItemKind.Line, 0, h, h, -s)],
            "Pipper" => [new(SightItemKind.Ellipse, 0, 0, 2, 2), new(SightItemKind.Ellipse, 0, 0, h, h)],
            "Rocket Ladder" => [new(SightItemKind.Line, -h, -h, h, -h), new(SightItemKind.Line, -h * .75, -s, h * .75, -s),
                new(SightItemKind.Line, -h * .55, s, h * .55, s), new(SightItemKind.Line, -h * .35, h, h * .35, h)],
            "Side Posts" => [new(SightItemKind.Line, -h, -h, -h, h), new(SightItemKind.Line, h, -h, h, h)],
            _ => throw new ArgumentOutOfRangeException(nameof(name))
        };
    }

    public static List<SightItem> ImportSvg(string path)
    {
        var normalized = Normalize(SvgLineArtImporter.Import(path));
        if (normalized.Count <= ImportedGameSegmentTarget) return normalized;

        List<SightItem> optimized = normalized;
        for (var tolerance = .005; tolerance <= .16 && optimized.Count > ImportedGameSegmentTarget;
             tolerance *= 1.6)
            optimized = SimplifyConnectedLines(normalized, tolerance);
        return optimized;
    }

    private static List<SightItem> SimplifyConnectedLines(List<SightItem> source, double tolerance)
    {
        var result = new List<SightItem>(Math.Min(source.Count, ImportedGameSegmentTarget));
        var chain = new List<DPoint>();

        void Flush()
        {
            if (chain.Count < 2)
            {
                chain.Clear();
                return;
            }

            var closed = Near(chain[0], chain[^1]);
            var simplified = closed
                ? SimplifyClosed(chain, tolerance)
                : DouglasPeucker(chain, tolerance);
            for (var index = 1; index < simplified.Count; index++)
                result.Add(new(SightItemKind.Line, simplified[index - 1].X, simplified[index - 1].Y,
                    simplified[index].X, simplified[index].Y));
            chain.Clear();
        }

        foreach (var item in source)
        {
            if (item.Kind != SightItemKind.Line)
            {
                Flush();
                result.Add(item);
                continue;
            }

            var start = new DPoint(item.X1, item.Y1);
            var end = new DPoint(item.X2, item.Y2);
            if (chain.Count == 0)
            {
                chain.Add(start);
                chain.Add(end);
            }
            else if (Near(chain[^1], start))
                chain.Add(end);
            else
            {
                Flush();
                chain.Add(start);
                chain.Add(end);
            }
        }
        Flush();
        return result;
    }

    private static List<DPoint> SimplifyClosed(List<DPoint> source, double tolerance)
    {
        var points = source.Take(source.Count - 1).ToList();
        if (points.Count < 4) return source;
        var split = 1;
        var farthest = 0d;
        for (var index = 1; index < points.Count; index++)
        {
            var distance = DistanceSquared(points[0], points[index]);
            if (distance <= farthest) continue;
            farthest = distance;
            split = index;
        }

        var first = DouglasPeucker(points.Take(split + 1).ToList(), tolerance);
        var secondSource = points.Skip(split).ToList();
        secondSource.Add(points[0]);
        var second = DouglasPeucker(secondSource, tolerance);
        first.AddRange(second.Skip(1));
        if (!Near(first[0], first[^1])) first.Add(first[0]);
        return first;
    }

    private static List<DPoint> DouglasPeucker(List<DPoint> points, double tolerance)
    {
        if (points.Count <= 2) return points;
        var keep = new bool[points.Count];
        keep[0] = keep[^1] = true;
        var pending = new Stack<(int Start, int End)>();
        pending.Push((0, points.Count - 1));
        while (pending.Count > 0)
        {
            var (start, end) = pending.Pop();
            var maximum = tolerance;
            var selected = -1;
            for (var index = start + 1; index < end; index++)
            {
                var distance = DistanceToLine(points[index], points[start], points[end]);
                if (distance <= maximum) continue;
                maximum = distance;
                selected = index;
            }
            if (selected < 0) continue;
            keep[selected] = true;
            pending.Push((start, selected));
            pending.Push((selected, end));
        }
        return points.Where((_, index) => keep[index]).ToList();
    }

    private static bool Near(DPoint a, DPoint b) =>
        Math.Abs(a.X - b.X) <= .00001 && Math.Abs(a.Y - b.Y) <= .00001;
    private static double DistanceSquared(DPoint a, DPoint b) =>
        (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y);
    private static double DistanceToLine(DPoint point, DPoint start, DPoint end)
    {
        var length = Math.Sqrt(DistanceSquared(start, end));
        if (length < 1e-12) return Math.Sqrt(DistanceSquared(point, start));
        return Math.Abs((end.Y - start.Y) * point.X - (end.X - start.X) * point.Y +
            end.X * start.Y - end.Y * start.X) / length;
    }

    private readonly record struct DPoint(double X, double Y);

    private static List<SightItem> Normalize(List<SightItem> items)
    {
        var left = items.Min(i => i.Bounds.Left);
        var top = items.Min(i => i.Bounds.Top);
        var right = items.Max(i => i.Bounds.Right);
        var bottom = items.Max(i => i.Bounds.Bottom);
        var scale = 32 / Math.Max(.001, Math.Max(right - left, bottom - top));
        var centerX = (left + right) / 2;
        var centerY = (top + bottom) / 2;
        return items.Select(i => i.Kind switch
        {
            SightItemKind.Line => new SightItem(i.Kind, (i.X1 - centerX) * scale, (i.Y1 - centerY) * scale,
                (i.X2 - centerX) * scale, (i.Y2 - centerY) * scale),
            SightItemKind.Ellipse or SightItemKind.FilledEllipse => new SightItem(i.Kind, (i.X1 - centerX) * scale, (i.Y1 - centerY) * scale,
                Math.Abs(i.X2 * scale), Math.Abs(i.Y2 * scale)),
            _ => new SightItem(i.Kind, (i.X1 - centerX) * scale, (i.Y1 - centerY) * scale,
                i.X2 * scale, i.Y2 * scale)
        }).ToList();
    }

    public static string Compact(string text)
    {
        var result = CompileMode(text, text.Contains("VECTOR_FILLED_ELLIPSE", StringComparison.Ordinal));
        if (result.Length == 0) throw new InvalidDataException("Sight has no valid vector commands.");
        return result;
    }

    private static string CompileMode(string text, bool opaqueFill)
    {
        var result = new List<string>();
        var lineChain = new List<double>();

        void FlushLineChain()
        {
            if (lineChain.Count < 4) return;
            const int maxPointsPerCommand = 7;
            var pointCount = lineChain.Count / 2;
            for (var startPoint = 0; startPoint < pointCount - 1;
                 startPoint += maxPointsPerCommand - 1)
            {
                var count = Math.Min(maxPointsPerCommand, pointCount - startPoint);
                result.Add($"[VECTOR_LINE,{string.Join(",",
                    lineChain.Skip(startPoint * 2).Take(count * 2).Select(Number))}]");
                if (startPoint + count >= pointCount) break;
            }
            lineChain.Clear();
        }

        foreach (Match m in CommandRegex().Matches(text))
        {
            var command = m.Groups[1].Value;
            var values = NumberRegex().Matches(m.Groups[2].Value)
                .Select(n => double.Parse(n.Value, CultureInfo.InvariantCulture)).ToArray();
            if (command == "VECTOR_LINE" && values.Length == 4)
            {
                var connects = lineChain.Count >= 2 &&
                    Math.Abs(lineChain[^2] - values[0]) <= .001 &&
                    Math.Abs(lineChain[^1] - values[1]) <= .001;
                if (!connects)
                {
                    FlushLineChain();
                    lineChain.Add(values[0]);
                    lineChain.Add(values[1]);
                }
                lineChain.Add(values[2]);
                lineChain.Add(values[3]);
            }
            else if (command == "VECTOR_FILLED_ELLIPSE")
            {
                FlushLineChain();
                result.Add($"[VECTOR_ELLIPSE,{string.Join(",", values.Select(Number))}]");
            }
            else if (command == "VECTOR_ELLIPSE" && opaqueFill)
            {
                FlushLineChain();
                if (values.Length < 4) continue;
                const int segments = 48;
                var points = new List<string>((segments + 1) * 2);
                for (var index = 0; index <= segments; index++)
                {
                    var angle = Math.PI * 2 * index / segments;
                    points.Add(Number(values[0] + Math.Cos(angle) * Math.Abs(values[2])));
                    points.Add(Number(values[1] + Math.Sin(angle) * Math.Abs(values[3])));
                }
                result.Add($"[VECTOR_LINE,{string.Join(",", points)}]");
            }
            else
            {
                FlushLineChain();
                result.Add($"[{command},{string.Join(",", values.Select(Number))}]");
            }
        }
        FlushLineChain();
        return string.Join(",", result);
    }

    public static string ReplaceSightFunction(string source, string mode0, string mode1, Color color,
        double lineWidth = 2)
    {
        if (!double.IsFinite(lineWidth) || lineWidth is < .1 or > 50)
            throw new ArgumentOutOfRangeException(nameof(lineWidth));

        const string marker = "function helicopterRocketSightMode";
        var start = source.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) throw new InvalidDataException($"{marker} was not found.");

        int FunctionEnd(int functionStart)
        {
            var brace = source.IndexOf('{', functionStart);
            if (brace < 0) throw new InvalidDataException("The sight function opening brace was not found.");
            var depth = 0;
            for (var i = brace; i < source.Length; i++)
            {
                if (source[i] == '{') depth++;
                else if (source[i] == '}' && --depth == 0) return i + 1;
            }
            throw new InvalidDataException("The sight function could not be parsed.");
        }

        var end = FunctionEnd(start);
        const string fillMarker = "function helicopterRocketSightFillMode";
        var fillStart = source.IndexOf(fillMarker, end, StringComparison.Ordinal);
        var replacementEnd = fillStart >= 0 ? FunctionEnd(fillStart) : end;

        var opaqueFill = mode0.Contains("VECTOR_FILLED_ELLIPSE", StringComparison.Ordinal) ||
            mode1.Contains("VECTOR_FILLED_ELLIPSE", StringComparison.Ordinal);
        var compiled0 = CompileMode(mode0, opaqueFill);
        var compiled1 = CompileMode(mode1, opaqueFill);
        var function =
            $"function helicopterRocketSightMode(sightMode){{if(sightMode==0)return [{compiled0}];return [{compiled1}]}}";
        var output = source[..start] + function + source[replacementEnd..];
        var lineColor = $"$1Color({color.R}, {color.G}, {color.B}, 255)";
        var fillColor = $"$1Color({color.R}, {color.G}, {color.B}, {(opaqueFill ? 255 : 0)})";
        output = Regex.Replace(output, @"(color\s*=\s*)Color\(\d+,\s*\d+,\s*\d+,\s*\d+\)", lineColor);
        output = Regex.Replace(output, @"(fillColor\s*=\s*)Color\(\d+,\s*\d+,\s*\d+,\s*\d+\)", fillColor);
        return Regex.Replace(output,
            @"(rocketSightLineWidth\s*=\s*)[-+]?(?:\d+(?:\.\d*)?|\.\d+)",
            match => match.Groups[1].Value + Number(lineWidth));
    }
}
