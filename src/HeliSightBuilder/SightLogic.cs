using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace HeliSightBuilder.Native;

public static partial class SightLogic
{
    [GeneratedRegex(@"[-+]?(?:\d+\.\d+|\d+|\.\d+)(?:[eE][-+]?\d+)?")]
    private static partial Regex NumberRegex();

    [GeneratedRegex(@"\[(VECTOR_[A-Z_]+),([^\]]+)\]")]
    public static partial Regex CommandRegex();

    [GeneratedRegex(@"[MmLlHhVvZz]|[-+]?(?:\d+\.\d+|\d+|\.\d+)(?:[eE][-+]?\d+)?")]
    private static partial Regex PathTokenRegex();

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
        var items = new List<SightItem>();
        foreach (var e in XDocument.Load(path, LoadOptions.None).Descendants())
        {
            double A(string name, double fallback = 0)
            {
                var m = NumberRegex().Match(e.Attribute(name)?.Value ?? "");
                return m.Success ? double.Parse(m.Value, CultureInfo.InvariantCulture) : fallback;
            }
            switch (e.Name.LocalName.ToLowerInvariant())
            {
                case "line":
                    items.Add(new(SightItemKind.Line, A("x1"), A("y1"), A("x2"), A("y2")));
                    break;
                case "rect":
                    items.Add(new(SightItemKind.Rectangle, A("x"), A("y"), A("width"), A("height")));
                    break;
                case "circle":
                    items.Add(new(SightItemKind.Ellipse, A("cx"), A("cy"), A("r", 1), A("r", 1)));
                    break;
                case "ellipse":
                    items.Add(new(SightItemKind.Ellipse, A("cx"), A("cy"), A("rx", 1), A("ry", 1)));
                    break;
                case "polyline":
                case "polygon":
                    var nums = NumberRegex().Matches(e.Attribute("points")?.Value ?? "").Select(m => double.Parse(m.Value, CultureInfo.InvariantCulture)).ToArray();
                    var pts = nums.Chunk(2).Where(p => p.Length == 2)
                        .Select(p => new PointF((float)p[0], (float)p[1])).ToList();
                    for (var i = 1; i < pts.Count; i++)
                        items.Add(new(SightItemKind.Line, pts[i - 1].X, pts[i - 1].Y, pts[i].X, pts[i].Y));
                    if (e.Name.LocalName.Equals("polygon", StringComparison.OrdinalIgnoreCase) && pts.Count > 2)
                        items.Add(new(SightItemKind.Line, pts[^1].X, pts[^1].Y, pts[0].X, pts[0].Y));
                    break;
                case "path":
                    items.AddRange(PathLines(e.Attribute("d")?.Value));
                    break;
            }
        }
        if (items.Count == 0) throw new InvalidDataException("No supported SVG line art was found.");
        return Normalize(items);
    }

    private static IEnumerable<SightItem> PathLines(string? value)
    {
        var tokens = PathTokenRegex().Matches(value ?? "").Select(m => m.Value).ToArray();
        var result = new List<SightItem>();
        var position = 0;
        var command = "";
        double x = 0, y = 0, startX = 0, startY = 0;
        while (position < tokens.Length)
        {
            if (tokens[position].Length == 1 && char.IsLetter(tokens[position][0]))
                command = tokens[position++];
            if (command.Length == 0) break;
            var upper = char.ToUpperInvariant(command[0]);
            var relative = char.IsLower(command[0]);
            double Next() => double.Parse(tokens[position++], CultureInfo.InvariantCulture);
            if (upper == 'M' && position + 1 < tokens.Length)
            {
                var nextX = Next();
                var nextY = Next();
                if (relative)
                {
                    nextX += x;
                    nextY += y;
                }
                x = startX = nextX;
                y = startY = nextY;
                command = relative ? "l" : "L";
            }
            else if (upper == 'L' && position + 1 < tokens.Length)
            {
                var nextX = Next();
                var nextY = Next();
                if (relative)
                {
                    nextX += x;
                    nextY += y;
                }
                result.Add(new(SightItemKind.Line, x, y, nextX, nextY));
                x = nextX;
                y = nextY;
            }
            else if (upper == 'H' && position < tokens.Length)
            {
                var nextX = Next();
                if (relative) nextX += x;
                result.Add(new(SightItemKind.Line, x, y, nextX, y));
                x = nextX;
            }
            else if (upper == 'V' && position < tokens.Length)
            {
                var nextY = Next();
                if (relative) nextY += y;
                result.Add(new(SightItemKind.Line, x, y, x, nextY));
                y = nextY;
            }
            else if (upper == 'Z')
            {
                result.Add(new(SightItemKind.Line, x, y, startX, startY));
                x = startX;
                y = startY;
                command = "";
            }
            else position++;
        }
        return result;
    }

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
        foreach (Match m in CommandRegex().Matches(text))
        {
            var command = m.Groups[1].Value;
            var values = NumberRegex().Matches(m.Groups[2].Value)
                .Select(n => double.Parse(n.Value, CultureInfo.InvariantCulture)).ToArray();
            if (command == "VECTOR_FILLED_ELLIPSE")
            {
                result.Add($"[VECTOR_ELLIPSE,{string.Join(",", values.Select(Number))}]");
            }
            else if (command == "VECTOR_ELLIPSE" && opaqueFill)
            {
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
                result.Add($"[{command},{string.Join(",", values.Select(Number))}]");
            }
        }
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
