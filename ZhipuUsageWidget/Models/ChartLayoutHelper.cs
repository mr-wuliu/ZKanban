namespace ZhipuUsageWidget.Models;

public static class ChartLayoutHelper
{
    public const double ChartLeft = 34;
    private const double ShortLabelWidth = 40;
    private const double LongLabelWidth = 72;

    public static double ComputePointX(DateTime pointTime, DateTime rangeStart, DateTime rangeEnd, double chartWidth, DateTime? dataEnd = null)
    {
        var start = rangeStart.Date;
        var end = ResolveRangeEnd(start, rangeEnd, dataEnd);
        var rangeSeconds = Math.Max(1, (end - start).TotalSeconds);
        var offset = (pointTime - start).TotalSeconds;
        return ChartLeft + offset / rangeSeconds * chartWidth;
    }

    public static DateTime ComputeTimeFromX(double x, DateTime rangeStart, DateTime rangeEnd, double chartWidth, DateTime? dataEnd = null)
    {
        var start = rangeStart.Date;
        var end = ResolveRangeEnd(start, rangeEnd, dataEnd);
        var rangeSeconds = Math.Max(1, (end - start).TotalSeconds);
        var ratio = Math.Clamp((x - ChartLeft) / chartWidth, 0, 1);
        return start.AddSeconds(ratio * rangeSeconds);
    }

    public static List<ChartAxisLabelViewModel> BuildAxisLabels(DateTime rangeStart, DateTime rangeEnd, double chartWidth, DateTime? dataEnd = null)
    {
        var labels = new List<ChartAxisLabelViewModel>();
        var start = rangeStart.Date;
        var resolvedEnd = ResolveRangeEnd(start, rangeEnd, dataEnd);
        var end = resolvedEnd;
        var sameDay = rangeStart.Date == rangeEnd.Date;

        if (sameDay)
        {
            var rangeSeconds = Math.Max(1, (end - start).TotalSeconds);
            var elapsedHours = Math.Max(0, (int)Math.Floor((end - start).TotalHours));
            var tickHours = BuildSameDayTickHours(elapsedHours);
            foreach (var h in tickHours)
            {
                var time = start.AddHours(h);
                var ratio = (time - start).TotalSeconds / rangeSeconds;
                var tickX = ChartLeft + ratio * chartWidth;
                labels.Add(new ChartAxisLabelViewModel
                {
                    Left = ComputeLabelLeft(tickX, ShortLabelWidth, chartWidth),
                    Text = time.ToString("HH:mm"),
                    Width = ShortLabelWidth,
                });
            }

            return labels;
        }

        var daySpan = Math.Max(1, (int)Math.Ceiling((end - start).TotalDays));
        var totalSeconds = Math.Max(1, (end - start).TotalSeconds);
        const double minGap = 30;
        var labelWidth = daySpan <= 3 ? LongLabelWidth : ShortLabelWidth;
        var maxLabels = Math.Max(2, (int)(chartWidth / (labelWidth + minGap)));

        var tickCount = Math.Min(maxLabels, daySpan);
        for (var i = 0; i < tickCount; i++)
        {
            var ratio = tickCount == 1 ? 0 : i / (double)(tickCount - 1);
            var time = start.AddSeconds(totalSeconds * ratio);
            var tickX = ChartLeft + ratio * chartWidth;
            labels.Add(new ChartAxisLabelViewModel
            {
                Left = ComputeLabelLeft(tickX, labelWidth, chartWidth),
                Text = daySpan <= 3 ? time.ToString("MM-dd HH:mm") : time.ToString("MM-dd"),
                Width = labelWidth,
            });
        }

        return labels;
    }

    private static DateTime ResolveRangeEnd(DateTime start, DateTime rangeEnd, DateTime? dataEnd)
    {
        var requestedEnd = rangeEnd.Date == start ? DateTime.Now : rangeEnd.Date.AddDays(1);
        if (rangeEnd.Date == start)
        {
            return requestedEnd;
        }

        if (dataEnd is null)
        {
            return requestedEnd;
        }

        var clampedDataEnd = dataEnd.Value < start ? start : dataEnd.Value;
        return clampedDataEnd < requestedEnd ? clampedDataEnd : requestedEnd;
    }

    private static List<int> BuildSameDayTickHours(int elapsedHours)
    {
        if (elapsedHours <= 0)
        {
            return [0];
        }

        if (elapsedHours <= 6)
        {
            return Enumerable.Range(0, elapsedHours + 1).ToList();
        }

        var step = elapsedHours <= 12 ? 2 : elapsedHours <= 18 ? 3 : 4;
        var ticks = new List<int>();
        for (var hour = 0; hour <= elapsedHours; hour += step)
        {
            ticks.Add(hour);
        }

        if (ticks[^1] != elapsedHours)
        {
            ticks.Add(elapsedHours);
        }

        return ticks;
    }

    private static double ComputeLabelLeft(double tickX, double labelWidth, double chartWidth)
    {
        var minLeft = ChartLeft;
        var maxLeft = ChartLeft + chartWidth - labelWidth;
        return Math.Clamp(tickX - labelWidth / 2, minLeft, maxLeft);
    }
}
