using System.Globalization;
using Avalonia;
using Avalonia.Media;

namespace ZhipuUsageWidget.Models;

public sealed class ChartSeriesViewModel
{
    public string Label { get; init; } = string.Empty;

    public string DisplayValue { get; init; } = string.Empty;

    public IBrush Stroke { get; init; } = Brushes.DodgerBlue;

    public IList<Point> Points { get; init; } = [];

    public Geometry? SmoothPath => Points.Count < 2 ? null : BuildSmoothPath();

    private Geometry BuildSmoothPath()
    {
        var pts = Points;
        var minY = double.MaxValue;
        var maxY = double.MinValue;
        for (var i = 0; i < pts.Count; i++)
        {
            if (pts[i].Y < minY) minY = pts[i].Y;
            if (pts[i].Y > maxY) maxY = pts[i].Y;
        }

        // Build path using mini-language: M x,y C cp1x,cp1y cp2x,cp2y x,y ...
        var sb = new System.Text.StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"M {pts[0].X},{pts[0].Y}");

        for (var i = 0; i < pts.Count - 1; i++)
        {
            var p0 = i > 0 ? pts[i - 1] : pts[i];
            var p1 = pts[i];
            var p2 = pts[i + 1];
            var p3 = i < pts.Count - 2 ? pts[i + 2] : pts[i + 1];

            var cp1x = p1.X + (p2.X - p0.X) / 6;
            var cp1y = Math.Clamp(p1.Y + (p2.Y - p0.Y) / 6, minY, maxY);
            var cp2x = p2.X - (p3.X - p1.X) / 6;
            var cp2y = Math.Clamp(p2.Y - (p3.Y - p1.Y) / 6, minY, maxY);

            sb.Append(CultureInfo.InvariantCulture, $" C {cp1x},{cp1y} {cp2x},{cp2y} {p2.X},{p2.Y}");
        }

        return Geometry.Parse(sb.ToString());
    }
}
