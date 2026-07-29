using System.Windows;
using ScreenshotTool.Editor;

namespace ScreenshotTool.Tests;

public class PathAnnotationTests
{
    [Fact]
    public void Constructor_SnapshotsPoints_FromMutableList()
    {
        var points = new List<Point> { new(0, 0), new(10, 10) };
        var annotation = new PathAnnotation(
            Guid.NewGuid(),
            points,
            System.Windows.Media.Colors.Blue,
            2,
            false);

        points.Add(new Point(20, 20));
        points[0] = new Point(5, 5);

        Assert.Equal(2, annotation.Points.Count);
        Assert.Equal(new Point(0, 0), annotation.Points[0]);
        Assert.Equal(new Point(10, 10), annotation.Points[1]);
    }
}
