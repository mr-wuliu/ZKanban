using System.Windows;
using System.Windows.Media;

namespace ZhipuUsageWidget.Models;

public sealed class ChartSeriesViewModel
{
    public string Label { get; init; } = string.Empty;

    public string DisplayValue { get; init; } = string.Empty;

    public Brush Stroke { get; init; } = Brushes.DodgerBlue;

    public PointCollection Points { get; init; } = [];

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

        var figure = new PathFigure { StartPoint = pts[0] };

        for (var i = 0; i < pts.Count - 1; i++)
        {
            var p0 = i > 0 ? pts[i - 1] : pts[i];
            var p1 = pts[i];
            var p2 = pts[i + 1];
            var p3 = i < pts.Count - 2 ? pts[i + 2] : pts[i + 1];

            var cp1 = new Point(
                p1.X + (p2.X - p0.X) / 6,
                Math.Clamp(p1.Y + (p2.Y - p0.Y) / 6, minY, maxY));
            var cp2 = new Point(
                p2.X - (p3.X - p1.X) / 6,
                Math.Clamp(p2.Y - (p3.Y - p1.Y) / 6, minY, maxY));

            figure.Segments.Add(new BezierSegment(cp1, cp2, p2, true));
        }

        return new PathGeometry { Figures = { figure } };
    }
}
