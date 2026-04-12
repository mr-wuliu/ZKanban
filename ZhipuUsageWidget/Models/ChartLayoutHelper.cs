namespace ZhipuUsageWidget.Models;

public static class ChartLayoutHelper
{
    public const double ChartWidth = 370;
    public const double ChartLeft = 34;

    public static double ComputePointX(DateTime pointTime, DateTime rangeStart, DateTime rangeEnd)
    {
        var start = rangeStart.Date;
        var end = rangeEnd.Date == start ? DateTime.Now : rangeEnd.Date.AddDays(1);
        var rangeSeconds = Math.Max(1, (end - start).TotalSeconds);
        var offset = (pointTime - start).TotalSeconds;
        return ChartLeft + offset / rangeSeconds * ChartWidth;
    }

    public static DateTime ComputeTimeFromX(double x, DateTime rangeStart, DateTime rangeEnd)
    {
        var start = rangeStart.Date;
        var end = rangeEnd.Date == start ? DateTime.Now : rangeEnd.Date.AddDays(1);
        var rangeSeconds = Math.Max(1, (end - start).TotalSeconds);
        var ratio = Math.Clamp((x - ChartLeft) / ChartWidth, 0, 1);
        return start.AddSeconds(ratio * rangeSeconds);
    }

    public static List<ChartAxisLabelViewModel> BuildAxisLabels(DateTime rangeStart, DateTime rangeEnd)
    {
        var labels = new List<ChartAxisLabelViewModel>();
        var start = rangeStart.Date;
        var end = rangeEnd.Date;
        var sameDay = start == end;

        if (sameDay)
        {
            var now = DateTime.Now;
            var rangeSeconds = Math.Max(1, (now - start).TotalSeconds);
            foreach (var h in new[] { 0, 6, 12, 18, 23 })
            {
                var time = start.AddHours(h);
                if (time > now) break;
                var ratio = (time - start).TotalSeconds / rangeSeconds;
                labels.Add(new ChartAxisLabelViewModel
                {
                    Left = ratio * ChartWidth,
                    Text = time.ToString("HH:mm"),
                });
            }

            return labels;
        }

        var daySpan = Math.Max(1, (end - start).Days + 1);
        var totalSeconds = Math.Max(1, (end.AddDays(1) - start).TotalSeconds);
        const double labelWidth = 40;
        const double minGap = 30;
        var maxLabels = Math.Max(2, (int)(ChartWidth / (labelWidth + minGap)));

        var tickCount = Math.Min(maxLabels, daySpan);
        for (var i = 0; i < tickCount; i++)
        {
            var ratio = tickCount == 1 ? 0 : i / (double)(tickCount - 1);
            var time = start.AddSeconds(totalSeconds * ratio);
            labels.Add(new ChartAxisLabelViewModel
            {
                Left = ratio * ChartWidth,
                Text = daySpan <= 3 ? time.ToString("MM-dd HH:mm") : time.ToString("MM-dd"),
            });
        }

        return labels;
    }
}
