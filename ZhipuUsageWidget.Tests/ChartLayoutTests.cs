using ZhipuUsageWidget.Models;

namespace ZhipuUsageWidget.Tests;

public class ChartLayoutTests
{
    private const double TestChartWidth = 370;
    private static readonly DateTime Today = new(2026, 4, 9, 0, 0, 0);

    [Fact]
    public void AxisLabels_7DayRange_ReturnsEvenlySpacedLabels()
    {
        var start = Today;
        var end = Today.AddDays(6);

        var labels = ChartLayoutHelper.BuildAxisLabels(start, end, TestChartWidth);

        // chartWidth=370 → maxLabels=5, daySpan=7 → tickCount=5
        Assert.Equal(5, labels.Count);
        Assert.Equal("04-09", labels[0].Text);
        Assert.Equal("04-16", labels[^1].Text);
        // Labels should be monotonically increasing
        for (var i = 1; i < labels.Count; i++)
            Assert.True(labels[i].Left > labels[i - 1].Left);
        Assert.Equal(ChartLayoutHelper.ChartLeft, labels[0].Left, 0.1);
        Assert.Equal(ChartLayoutHelper.ChartLeft + TestChartWidth, labels[^1].Left + labels[^1].Width, 0.1);
    }

    [Fact]
    public void AxisLabels_30DayRange_ReturnsEvenlySpacedLabels()
    {
        var start = Today;
        var end = Today.AddDays(29);

        var labels = ChartLayoutHelper.BuildAxisLabels(start, end, TestChartWidth);

        // chartWidth=370 → maxLabels=5, daySpan=30 → tickCount=5
        Assert.Equal(5, labels.Count);
        Assert.Equal("04-09", labels[0].Text);
        Assert.Equal("05-09", labels[^1].Text);
        Assert.Equal(ChartLayoutHelper.ChartLeft, labels[0].Left, 0.1);
        Assert.Equal(ChartLayoutHelper.ChartLeft + TestChartWidth, labels[^1].Left + labels[^1].Width, 0.1);
    }

    [Fact]
    public void AxisLabels_SameDay_ReturnsHourlyLabels()
    {
        var start = DateTime.Today;
        var end = DateTime.Today;

        var labels = ChartLayoutHelper.BuildAxisLabels(start, end, TestChartWidth);

        Assert.NotEmpty(labels);
        Assert.Equal("00:00", labels[0].Text);
        Assert.All(labels, l => Assert.Matches(@"^\d{2}:\d{2}$", l.Text));
        Assert.Equal(ChartLayoutHelper.ChartLeft, labels[0].Left, 0.1);
        // Monotonically increasing
        for (var i = 1; i < labels.Count; i++)
            Assert.True(labels[i].Left > labels[i - 1].Left);
    }

    [Fact]
    public void PointX_AtRangeStart_ShouldBeAtChartLeft()
    {
        var start = Today;
        var end = Today.AddDays(6);

        var x = ChartLayoutHelper.ComputePointX(start, start, end, TestChartWidth);

        Assert.Equal(ChartLayoutHelper.ChartLeft, x, 0.1);
    }

    [Fact]
    public void PointX_AtRangeEndLastMoment_ShouldBeNearChartLeftPlusChartWidth()
    {
        var start = Today;
        var end = Today.AddDays(6);
        var lastMoment = end.AddDays(1).AddSeconds(-1);

        var x = ChartLayoutHelper.ComputePointX(lastMoment, start, end, TestChartWidth);

        Assert.True(Math.Abs(x - (ChartLayoutHelper.ChartLeft + TestChartWidth)) < 1,
            $"Expected near {ChartLayoutHelper.ChartLeft + TestChartWidth}, got {x}");
    }

    [Fact]
    public void PointX_AtMidpoint_ShouldBeAtCenter()
    {
        var start = Today;
        var end = Today.AddDays(6);
        var rangeSeconds = (end.AddDays(1) - start).TotalSeconds;
        var midTime = start.AddSeconds(rangeSeconds / 2);

        var x = ChartLayoutHelper.ComputePointX(midTime, start, end, TestChartWidth);
        var expectedX = ChartLayoutHelper.ChartLeft + TestChartWidth / 2;

        Assert.True(Math.Abs(x - expectedX) < 1,
            $"Expected near {expectedX}, got {x}");
    }

    [Fact]
    public void PointX_AtDayBoundary_ShouldAlignToGrid()
    {
        var start = Today;
        var end = Today.AddDays(6);

        for (var i = 0; i <= 6; i++)
        {
            var dayStart = start.AddDays(i);
            var x = ChartLayoutHelper.ComputePointX(dayStart, start, end, TestChartWidth);
            var expectedX = ChartLayoutHelper.ChartLeft + i / 7d * TestChartWidth;

            Assert.True(Math.Abs(x - expectedX) < 1,
                $"Day {i}: expected near {expectedX}, got {x}");
        }
    }

    [Fact]
    public void PointX_SingleDayRange_SpreadsAcrossWidth()
    {
        // Use DateTime.Today so the range calculation (DateTime.Now) is on the same day
        var start = DateTime.Today;
        var end = DateTime.Today;

        var x0 = ChartLayoutHelper.ComputePointX(start, start, end, TestChartWidth);
        var x23 = ChartLayoutHelper.ComputePointX(start.AddHours(23), start, end, TestChartWidth);

        var span = x23 - x0;
        Assert.True(span > TestChartWidth * 0.9,
            $"Points within a single day should span nearly full width, got span={span}");
    }

    [Fact]
    public void PointX_UsesLastDataPointAsRightBoundary_WhenDataEndsEarly()
    {
        var start = Today;
        var end = Today.AddDays(6);
        var dataEnd = Today.AddDays(5).AddHours(12);

        var x = ChartLayoutHelper.ComputePointX(dataEnd, start, end, TestChartWidth, dataEnd);

        Assert.Equal(ChartLayoutHelper.ChartLeft + TestChartWidth, x, 0.1);
    }

    [Fact]
    public void AxisLabels_UsesLastDataPointAsRightBoundary_WhenDataEndsEarly()
    {
        var start = Today;
        var end = Today.AddDays(6);
        var dataEnd = Today.AddDays(5).AddHours(12);

        var labels = ChartLayoutHelper.BuildAxisLabels(start, end, TestChartWidth, dataEnd);

        Assert.Equal(ChartLayoutHelper.ChartLeft + TestChartWidth, labels[^1].Left + labels[^1].Width, 0.1);
    }

    [Fact]
    public void PointX_SameDay_DoesNotClampToLastDataPoint()
    {
        var start = DateTime.Today;
        var end = DateTime.Today;
        var dataEnd = start;
        var laterTime = start.AddHours(12);

        var x = ChartLayoutHelper.ComputePointX(laterTime, start, end, TestChartWidth, dataEnd);

        Assert.True(x > ChartLayoutHelper.ChartLeft + TestChartWidth * 0.3,
            $"Expected same-day chart to remain expanded by current time, got x={x}");
    }

    [Fact]
    public void AxisLabels_SameDay_CoversElapsedHours()
    {
        var start = Today;
        var end = Today;
        var dataEnd = start;
        var now = DateTime.Now;

        var labels = ChartLayoutHelper.BuildAxisLabels(start, end, TestChartWidth, dataEnd);

        Assert.Equal("00:00", labels[0].Text);
        Assert.Equal($"{Math.Max(0, (int)Math.Floor((now - now.Date).TotalHours)):00}:00", labels[^1].Text);
    }
}
