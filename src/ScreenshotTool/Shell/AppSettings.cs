using System.IO;
using System.Text.Json;

namespace ScreenshotTool.Shell;

public sealed class AppSettings
{
    public uint HotkeyModifiers { get; set; } = 0x0002 | 0x0004;
    public uint HotkeyKey { get; set; } = 0x41;
    public string? DefaultSaveDirectory { get; set; }
    public string StrokeColor { get; set; } = "#FF0000";
    public double StrokeThickness { get; set; } = 3;

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ScreenshotTool", "settings.json");

    public static AppSettings Load() => LoadFrom(DefaultPath);

    public static AppSettings LoadFrom(string path)
    {
        try
        {
            if (!File.Exists(path)) return new AppSettings();
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save() => SaveTo(DefaultPath);

    public void SaveTo(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }
}
