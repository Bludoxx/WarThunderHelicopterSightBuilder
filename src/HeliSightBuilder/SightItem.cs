using System.Text.Json.Serialization;

namespace HeliSightBuilder.Native;

public enum SightItemKind { Line, Ellipse, FilledEllipse, Rectangle }

public sealed record SightItem(SightItemKind Kind, double X1, double Y1, double X2, double Y2)
{
    public SightItem Scale(double factor) => new(Kind, X1 * factor, Y1 * factor, X2 * factor, Y2 * factor);

    [JsonIgnore]
    public RectangleF Bounds
    {
        get
        {
            if (Kind is SightItemKind.Ellipse or SightItemKind.FilledEllipse)
                return RectangleF.FromLTRB((float)(X1 - Math.Abs(X2)), (float)(Y1 - Math.Abs(Y2)),
                    (float)(X1 + Math.Abs(X2)), (float)(Y1 + Math.Abs(Y2)));
            var right = Kind == SightItemKind.Rectangle ? X1 + X2 : X2;
            var bottom = Kind == SightItemKind.Rectangle ? Y1 + Y2 : Y2;
            return RectangleF.FromLTRB((float)Math.Min(X1, right), (float)Math.Min(Y1, bottom),
                (float)Math.Max(X1, right), (float)Math.Max(Y1, bottom));
        }
    }

    [JsonIgnore]
    public PointF Center => Kind switch
    {
        SightItemKind.Line => new((float)((X1 + X2) / 2), (float)((Y1 + Y2) / 2)),
        SightItemKind.Ellipse or SightItemKind.FilledEllipse => new((float)X1, (float)Y1),
        _ => new((float)(X1 + X2 / 2), (float)(Y1 + Y2 / 2))
    };
}
