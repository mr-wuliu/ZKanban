using ZhipuUsageWidget.Models;

namespace ZhipuUsageWidget.Tests;

public class ChartLayoutTests
{
    private static readonly DateTime Today = new(2026, 4, 9, 0, 0, 0);

    [Fact]
    public void AxisLabels_7DayRange_ReturnsStartAndEnd()
    {
        var start = Today;
        var end = Today.AddDays(6);

        var labels = ChartLayoutHelper.BuildAxisLabels(start, end);

        Assert.Equal(2, labels.Count);
        Assert.Equal("04-09", labels[0].Text);
        Assert.Equal("04-15", labels[1].Text);
    }

    [Fact]
    public void AxisLabels_30DayRange_ReturnsStartAndEnd()
    {
        var start = Today;
        var end = Today.AddDays(29);

        var labels = ChartLayoutHelper.BuildAxisLabels(start, end);

        Assert.Equal(2, labels.Count);
        Assert.Equal("04-09", labels[0].Text);
        Assert.Equal("05-08", labels[1].Text);
    }

    [Fact]
    public void AxisLabels_SameDay_ReturnsSingleLabel()
    {
        var start = Today;
        var end = Today;

        var labels = ChartLayoutHelper.BuildAxisLabels(start, end);

        Assert.Single(labels);
        Assert.Equal("04-09", labels[0].Text);
    }

    [Fact]
    public void PointX_AtRangeStart_ShouldBeAtChartLeft()
    {
        var start = Today;
        var end = Today.AddDays(6);

        var x = ChartLayoutHelper.ComputePointX(start, start, end);

        Assert.Equal(ChartLayoutHelper.ChartLeft, x, 0.1);
    }

    [Fact]
    public void PointX_AtRangeEndLastMoment_ShouldBeNearChartLeftPlusChartWidth()
    {
        var start = Today;
        var end = Today.AddDays(6);
        var lastMoment = end.AddDays(1).AddSeconds(-1);

        var x = ChartLayoutHelper.ComputePointX(lastMoment, start, end);

        Assert.True(Math.Abs(x - (ChartLayoutHelper.ChartLeft + ChartLayoutHelper.ChartWidth)) < 1,
            $"Expected near {ChartLayoutHelper.ChartLeft + ChartLayoutHelper.ChartWidth}, got {x}");
    }

    [Fact]
    public void PointX_AtMidpoint_ShouldBeAtCenter()
    {
        var start = Today;
        var end = Today.AddDays(6);
        var rangeSeconds = (end.AddDays(1) - start).TotalSeconds;
        var midTime = start.AddSeconds(rangeSeconds / 2);

        var x = ChartLayoutHelper.ComputePointX(midTime, start, end);
        var expectedX = ChartLayoutHelper.ChartLeft + ChartLayoutHelper.ChartWidth / 2;

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
            var x = ChartLayoutHelper.ComputePointX(dayStart, start, end);
            var expectedX = ChartLayoutHelper.ChartLeft + i / 7d * ChartLayoutHelper.ChartWidth;

            Assert.True(Math.Abs(x - expectedX) < 1,
                $"Day {i}: expected near {expectedX}, got {x}");
        }
    }

    [Fact]
    public void PointX_SingleDayRange_SpreadsAcrossWidth()
    {
        var start = Today;
        var end = Today;

        var x0 = ChartLayoutHelper.ComputePointX(start, start, end);
        var x23 = ChartLayoutHelper.ComputePointX(start.AddHours(23), start, end);

        var span = x23 - x0;
        Assert.True(span > ChartLayoutHelper.ChartWidth * 0.9,
            $"Points within a single day should span nearly full width, got span={span}");
    }
}
