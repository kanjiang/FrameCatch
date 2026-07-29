using System.IO;
using ScreenshotTool.Shell;

namespace ScreenshotTool.Tests;

public class AppSettingsTests
{
    [Fact]
    public void Load_WhenMissing_ReturnsDefaults()
    {
        var path = Path.Combine(Path.GetTempPath(), "ScreenshotToolTest", Guid.NewGuid().ToString("N"), "settings.json");
        var settings = AppSettings.LoadFrom(path);
        Assert.Equal(0x41u, settings.HotkeyKey);
        Assert.Equal(3.0, settings.StrokeThickness);
    }

    [Fact]
    public void Save_ThenLoad_RoundTrips()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ScreenshotToolTest", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "settings.json");
        var original = new AppSettings { HotkeyKey = 0x42, StrokeThickness = 5 };
        original.SaveTo(path);
        var loaded = AppSettings.LoadFrom(path);
        Assert.Equal(0x42u, loaded.HotkeyKey);
        Assert.Equal(5.0, loaded.StrokeThickness);
    }
}
