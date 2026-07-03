using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace HeliSightBuilder.Native;

internal static partial class SvgLineArtImporter
{
    private const double CurveTolerance = .35;

    [GeneratedRegex(@"[AaCcHhLlMmQqSsTtVvZz]|[-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][-+]?\d+)?")]
    private static partial Regex PathTokenRegex();

    [GeneratedRegex(@"[-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][-+]?\d+)?")]
    private static partial Regex NumberRegex();

    [GeneratedRegex(@"([A-Za-z]+)\s*\(([^)]*)\)")]
    private static partial Regex TransformRegex();

    public static List<SightItem> Import(string path)
    {
        var document = XDocument.Load(path, LoadOptions.None);
        if (document.Root is null)
            throw new InvalidDataException("The SVG has no root element.");

        var result = new List<SightItem>();
        Visit(document.Root, Affine.Identity, result);
        if (result.Count == 0)
            throw new InvalidDataException("No supported SVG line art was found.");
        return result;
    }

    private static void Visit(XElement element, Affine parent, List<SightItem> result)
    {
        var transform = parent.Then(ParseTransform(element.Attribute("transform")?.Value));
        switch (element.Name.LocalName.ToLowerInvariant())
        {
            case "line":
                AddLine(result, transform, Point(element, "x1", "y1"), Point(element, "x2", "y2"));
                break;
            case "rect":
                AddRectangle(result, transform, Attribute(element, "x"), Attribute(element, "y"),
                    Attribute(element, "width"), Attribute(element, "height"));
                break;
            case "circle":
                AddEllipse(result, transform, Attribute(element, "cx"), Attribute(element, "cy"),
                    Attribute(element, "r", 1), Attribute(element, "r", 1));
                break;
            case "ellipse":
                AddEllipse(result, transform, Attribute(element, "cx"), Attribute(element, "cy"),
                    Attribute(element, "rx", 1), Attribute(element, "ry", 1));
                break;
            case "polyline":
            case "polygon":
                AddPoly(result, transform, element.Attribute("points")?.Value,
                    element.Name.LocalName.Equals("polygon", StringComparison.OrdinalIgnoreCase));
                break;
            case "path":
                AddPath(result, transform, element.Attribute("d")?.Value);
                break;
        }

        foreach (var child in element.Elements())
            Visit(child, transform, result);
    }

    private static double Attribute(XElement element, string name, double fallback = 0)
    {
        var match = NumberRegex().Match(element.Attribute(name)?.Value ?? "");
        return match.Success ? double.Parse(match.Value, CultureInfo.InvariantCulture) : fallback;
    }

    private static DPoint Point(XElement element, string x, string y) =>
        new(Attribute(element, x), Attribute(element, y));

    private static void AddLine(List<SightItem> result, Affine transform, DPoint a, DPoint b)
    {
        a = transform.Apply(a);
        b = transform.Apply(b);
        if (DistanceSquared(a, b) < 1e-16) return;
        result.Add(new(SightItemKind.Line, a.X, a.Y, b.X, b.Y));
    }

    private static void AddRectangle(List<SightItem> result, Affine transform,
        double x, double y, double width, double height)
    {
        var points = new[]
        {
            new DPoint(x, y), new DPoint(x + width, y), new DPoint(x + width, y + height),
            new DPoint(x, y + height)
        };
        for (var index = 0; index < points.Length; index++)
            AddLine(result, transform, points[index], points[(index + 1) % points.Length]);
    }

    private static void AddEllipse(List<SightItem> result, Affine transform,
        double cx, double cy, double rx, double ry)
    {
        const int segments = 64;
        var previous = new DPoint(cx + rx, cy);
        for (var index = 1; index <= segments; index++)
        {
            var angle = Math.PI * 2 * index / segments;
            var next = new DPoint(cx + Math.Cos(angle) * rx, cy + Math.Sin(angle) * ry);
            AddLine(result, transform, previous, next);
            previous = next;
        }
    }

    private static void AddPoly(List<SightItem> result, Affine transform, string? value, bool close)
    {
        var values = NumberRegex().Matches(value ?? "")
            .Select(match => double.Parse(match.Value, CultureInfo.InvariantCulture)).ToArray();
        var points = values.Chunk(2).Where(pair => pair.Length == 2)
            .Select(pair => new DPoint(pair[0], pair[1])).ToList();
        for (var index = 1; index < points.Count; index++)
            AddLine(result, transform, points[index - 1], points[index]);
        if (close && points.Count > 2)
            AddLine(result, transform, points[^1], points[0]);
    }

    private static void AddPath(List<SightItem> result, Affine transform, string? value)
    {
        var tokens = PathTokenRegex().Matches(value ?? "").Select(match => match.Value).ToArray();
        var position = 0;
        var command = '\0';
        var current = new DPoint(0, 0);
        var start = current;
        var lastCubicControl = current;
        var lastQuadraticControl = current;
        var previousCommand = '\0';

        bool IsCommand() => position < tokens.Length && tokens[position].Length == 1 &&
            char.IsLetter(tokens[position][0]);
        bool Has(int count) => position + count <= tokens.Length &&
            !tokens.Skip(position).Take(count).Any(token => token.Length == 1 && char.IsLetter(token[0]));
        double Next() => double.Parse(tokens[position++], CultureInfo.InvariantCulture);
        DPoint NextPoint(bool relative)
        {
            var point = new DPoint(Next(), Next());
            return relative ? point + current : point;
        }

        while (position < tokens.Length)
        {
            if (IsCommand())
                command = tokens[position++][0];
            if (command == '\0') break;

            var upper = char.ToUpperInvariant(command);
            var relative = char.IsLower(command);
            if (upper == 'Z')
            {
                AddLine(result, transform, current, start);
                current = start;
                previousCommand = command;
                command = '\0';
                continue;
            }

            var required = upper switch
            {
                'M' or 'L' or 'T' => 2,
                'H' or 'V' => 1,
                'C' => 6,
                'S' or 'Q' => 4,
                'A' => 7,
                _ => 0
            };
            if (required == 0)
            {
                command = '\0';
                continue;
            }
            if (!Has(required))
            {
                if (IsCommand()) continue;
                break;
            }

            switch (upper)
            {
                case 'M':
                current = NextPoint(relative);
                start = current;
                command = relative ? 'l' : 'L';
                break;
                case 'L':
                var lineEnd = NextPoint(relative);
                AddLine(result, transform, current, lineEnd);
                current = lineEnd;
                break;
                case 'H':
                var horizontal = Next();
                if (relative) horizontal += current.X;
                var horizontalEnd = new DPoint(horizontal, current.Y);
                AddLine(result, transform, current, horizontalEnd);
                current = horizontalEnd;
                break;
                case 'V':
                var vertical = Next();
                if (relative) vertical += current.Y;
                var verticalEnd = new DPoint(current.X, vertical);
                AddLine(result, transform, current, verticalEnd);
                current = verticalEnd;
                break;
                case 'C':
                var cubic1 = NextPoint(relative);
                var cubic2 = NextPoint(relative);
                var cubicEnd = NextPoint(relative);
                FlattenCubic(result, transform, current, cubic1, cubic2, cubicEnd);
                current = cubicEnd;
                lastCubicControl = cubic2;
                break;
                case 'S':
                var smooth1 = char.ToUpperInvariant(previousCommand) is 'C' or 'S'
                    ? current * 2 - lastCubicControl : current;
                var smooth2 = NextPoint(relative);
                var smoothEnd = NextPoint(relative);
                FlattenCubic(result, transform, current, smooth1, smooth2, smoothEnd);
                current = smoothEnd;
                lastCubicControl = smooth2;
                break;
                case 'Q':
                var quadraticControl = NextPoint(relative);
                var quadraticEnd = NextPoint(relative);
                FlattenQuadratic(result, transform, current, quadraticControl, quadraticEnd);
                current = quadraticEnd;
                lastQuadraticControl = quadraticControl;
                break;
                case 'T':
                var reflected = char.ToUpperInvariant(previousCommand) is 'Q' or 'T'
                    ? current * 2 - lastQuadraticControl : current;
                var reflectedEnd = NextPoint(relative);
                FlattenQuadratic(result, transform, current, reflected, reflectedEnd);
                current = reflectedEnd;
                lastQuadraticControl = reflected;
                break;
                case 'A':
                var radiusX = Math.Abs(Next());
                var radiusY = Math.Abs(Next());
                var rotation = Next();
                var largeArc = Math.Abs(Next()) > .5;
                var sweep = Math.Abs(Next()) > .5;
                var arcEnd = NextPoint(relative);
                FlattenArc(result, transform, current, arcEnd, radiusX, radiusY,
                    rotation, largeArc, sweep);
                current = arcEnd;
                break;
            }
            previousCommand = command;
        }
    }

    private static void FlattenQuadratic(List<SightItem> result, Affine transform,
        DPoint start, DPoint control, DPoint end)
    {
        var cubic1 = start + (control - start) * (2d / 3);
        var cubic2 = end + (control - end) * (2d / 3);
        FlattenCubic(result, transform, start, cubic1, cubic2, end);
    }

    private static void FlattenCubic(List<SightItem> result, Affine transform,
        DPoint start, DPoint control1, DPoint control2, DPoint end, int depth = 0)
    {
        var flatness = Math.Max(DistanceToLine(control1, start, end),
            DistanceToLine(control2, start, end));
        if (depth >= 12 || flatness <= CurveTolerance)
        {
            AddLine(result, transform, start, end);
            return;
        }

        var a = Mid(start, control1);
        var b = Mid(control1, control2);
        var c = Mid(control2, end);
        var d = Mid(a, b);
        var e = Mid(b, c);
        var middle = Mid(d, e);
        FlattenCubic(result, transform, start, a, d, middle, depth + 1);
        FlattenCubic(result, transform, middle, e, c, end, depth + 1);
    }

    private static void FlattenArc(List<SightItem> result, Affine transform, DPoint start,
        DPoint end, double rx, double ry, double rotationDegrees, bool largeArc, bool sweep)
    {
        if (rx < 1e-9 || ry < 1e-9 || DistanceSquared(start, end) < 1e-16)
        {
            AddLine(result, transform, start, end);
            return;
        }

        var rotation = rotationDegrees * Math.PI / 180;
        var cos = Math.Cos(rotation);
        var sin = Math.Sin(rotation);
        var dx = (start.X - end.X) / 2;
        var dy = (start.Y - end.Y) / 2;
        var xPrime = cos * dx + sin * dy;
        var yPrime = -sin * dx + cos * dy;
        var radiiScale = xPrime * xPrime / (rx * rx) + yPrime * yPrime / (ry * ry);
        if (radiiScale > 1)
        {
            var scale = Math.Sqrt(radiiScale);
            rx *= scale;
            ry *= scale;
        }

        var numerator = Math.Max(0, rx * rx * ry * ry - rx * rx * yPrime * yPrime -
            ry * ry * xPrime * xPrime);
        var denominator = rx * rx * yPrime * yPrime + ry * ry * xPrime * xPrime;
        var factor = denominator <= 1e-18 ? 0 : Math.Sqrt(numerator / denominator);
        if (largeArc == sweep) factor = -factor;
        var centerPrimeX = factor * rx * yPrime / ry;
        var centerPrimeY = factor * -ry * xPrime / rx;
        var center = new DPoint(
            cos * centerPrimeX - sin * centerPrimeY + (start.X + end.X) / 2,
            sin * centerPrimeX + cos * centerPrimeY + (start.Y + end.Y) / 2);

        var startVector = new DPoint((xPrime - centerPrimeX) / rx,
            (yPrime - centerPrimeY) / ry);
        var endVector = new DPoint((-xPrime - centerPrimeX) / rx,
            (-yPrime - centerPrimeY) / ry);
        var startAngle = Math.Atan2(startVector.Y, startVector.X);
        var delta = VectorAngle(startVector, endVector);
        if (!sweep && delta > 0) delta -= Math.PI * 2;
        if (sweep && delta < 0) delta += Math.PI * 2;
        var segments = Math.Clamp((int)Math.Ceiling(Math.Abs(delta) * Math.Max(rx, ry) / 4), 4, 256);
        var previous = start;
        for (var index = 1; index <= segments; index++)
        {
            var angle = startAngle + delta * index / segments;
            var next = new DPoint(
                center.X + cos * rx * Math.Cos(angle) - sin * ry * Math.Sin(angle),
                center.Y + sin * rx * Math.Cos(angle) + cos * ry * Math.Sin(angle));
            AddLine(result, transform, previous, next);
            previous = next;
        }
    }

    private static Affine ParseTransform(string? value)
    {
        var result = Affine.Identity;
        foreach (Match match in TransformRegex().Matches(value ?? ""))
        {
            var values = NumberRegex().Matches(match.Groups[2].Value)
                .Select(number => double.Parse(number.Value, CultureInfo.InvariantCulture)).ToArray();
            var next = match.Groups[1].Value.ToLowerInvariant() switch
            {
                "matrix" when values.Length >= 6 =>
                    new Affine(values[0], values[1], values[2], values[3], values[4], values[5]),
                "translate" when values.Length >= 1 =>
                    new Affine(1, 0, 0, 1, values[0], values.Length > 1 ? values[1] : 0),
                "scale" when values.Length >= 1 =>
                    new Affine(values[0], 0, 0, values.Length > 1 ? values[1] : values[0], 0, 0),
                "rotate" when values.Length >= 1 => Rotation(values),
                "skewx" when values.Length >= 1 =>
                    new Affine(1, 0, Math.Tan(values[0] * Math.PI / 180), 1, 0, 0),
                "skewy" when values.Length >= 1 =>
                    new Affine(1, Math.Tan(values[0] * Math.PI / 180), 0, 1, 0, 0),
                _ => Affine.Identity
            };
            result = result.Then(next);
        }
        return result;
    }

    private static Affine Rotation(double[] values)
    {
        var angle = values[0] * Math.PI / 180;
        var cos = Math.Cos(angle);
        var sin = Math.Sin(angle);
        var rotation = new Affine(cos, sin, -sin, cos, 0, 0);
        if (values.Length < 3) return rotation;
        return new Affine(1, 0, 0, 1, values[1], values[2]).Then(rotation)
            .Then(new Affine(1, 0, 0, 1, -values[1], -values[2]));
    }

    private static DPoint Mid(DPoint a, DPoint b) => (a + b) * .5;
    private static double DistanceSquared(DPoint a, DPoint b) =>
        (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y);
    private static double DistanceToLine(DPoint point, DPoint start, DPoint end)
    {
        var length = Math.Sqrt(DistanceSquared(start, end));
        if (length < 1e-12) return Math.Sqrt(DistanceSquared(point, start));
        return Math.Abs((end.Y - start.Y) * point.X - (end.X - start.X) * point.Y +
            end.X * start.Y - end.Y * start.X) / length;
    }
    private static double VectorAngle(DPoint a, DPoint b) =>
        Math.Atan2(a.X * b.Y - a.Y * b.X, a.X * b.X + a.Y * b.Y);

    private readonly record struct DPoint(double X, double Y)
    {
        public static DPoint operator +(DPoint a, DPoint b) => new(a.X + b.X, a.Y + b.Y);
        public static DPoint operator -(DPoint a, DPoint b) => new(a.X - b.X, a.Y - b.Y);
        public static DPoint operator *(DPoint point, double scale) => new(point.X * scale, point.Y * scale);
    }

    private readonly record struct Affine(double A, double B, double C, double D, double E, double F)
    {
        public static readonly Affine Identity = new(1, 0, 0, 1, 0, 0);
        public DPoint Apply(DPoint point) =>
            new(A * point.X + C * point.Y + E, B * point.X + D * point.Y + F);
        public Affine Then(Affine local) => new(
            A * local.A + C * local.B,
            B * local.A + D * local.B,
            A * local.C + C * local.D,
            B * local.C + D * local.D,
            A * local.E + C * local.F + E,
            B * local.E + D * local.F + F);
    }
}
