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
        Assert.Equal("#FF0000", settings.StrokeColor);
        Assert.Equal(3.0, settings.StrokeThickness);
    }

    [Fact]
    public void Save_ThenLoad_RoundTrips()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ScreenshotToolTest", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "settings.json");
        var original = new AppSettings
        {
            HotkeyModifiers = 0x0001 | 0x0008,
            HotkeyKey = 0x42,
            DefaultSaveDirectory = Path.Combine(dir, "captures"),
            StrokeColor = "#FF2563EB",
            StrokeThickness = 5
        };
        original.SaveTo(path);
        var loaded = AppSettings.LoadFrom(path);
        Assert.Equal(0x0001u | 0x0008u, loaded.HotkeyModifiers);
        Assert.Equal(0x42u, loaded.HotkeyKey);
        Assert.Equal(Path.Combine(dir, "captures"), loaded.DefaultSaveDirectory);
        Assert.Equal("#FF2563EB", loaded.StrokeColor);
        Assert.Equal(5.0, loaded.StrokeThickness);
    }
}
