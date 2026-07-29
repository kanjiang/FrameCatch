# Windows 截图标注小工具 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 构建常驻托盘的 Windows 截图工具：多屏矩形/套索截图，编辑器内矩形/椭圆/箭头/画笔/荧光笔/马赛克/文字标注，支持复制与保存。

**Architecture:** .NET 8 WPF 单实例应用；Shell（托盘+热键）触发 Capture Overlay（虚拟桌面截屏+选区裁切），再打开 Editor（矢量标注层 + 可撤销马赛克像素操作），经 ImageExportService 合成 PNG 导出。

**Tech Stack:** C# / .NET 8 / WPF / xUnit / System.Drawing.Common（截屏辅助）/ WinForms NotifyIcon 互操作 / System.Text.Json

## Global Constraints

- 平台：Windows 10/11；开发机需 .NET 8 SDK；发布可为 framework-dependent 或 self-contained
- 坐标：虚拟屏幕物理像素；DPI：PerMonitorV2
- UI 文案：中文
- 默认热键：Ctrl+Shift+A
- 第一版不做：录屏、滚动截图、窗口一键截图、多边形点击围选、OCR、钉图
- 规格来源：`docs/superpowers/specs/2026-07-29-windows-screenshot-tool-design.md`

---

## File Structure

```
ScreenshotTool.sln
src/ScreenshotTool/
  ScreenshotTool.csproj
  App.xaml / App.xaml.cs
  app.manifest
  Native/Win32.cs
  Shell/TrayIconService.cs
  Shell/HotkeyService.cs
  Shell/AppSettings.cs
  Shell/SettingsWindow.xaml(.cs)
  Capture/ScreenCaptureService.cs
  Capture/CaptureOverlayWindow.xaml(.cs)
  Capture/RegionGeometry.cs
  Editor/EditorWindow.xaml(.cs)
  Editor/AnnotationModels.cs
  Editor/AnnotationCanvas.cs
  Editor/AnnotationCommands.cs
  Editor/UndoStack.cs
  Editor/ImageExportService.cs
  Assets/tray.ico
tests/ScreenshotTool.Tests/
  ScreenshotTool.Tests.csproj
  RegionGeometryTests.cs
  UndoStackTests.cs
  MosaicCommandTests.cs
```

---

### Task 1: 安装 SDK 并搭建解决方案

**Files:**
- Create: `ScreenshotTool.sln`
- Create: `src/ScreenshotTool/ScreenshotTool.csproj`
- Create: `src/ScreenshotTool/App.xaml`
- Create: `src/ScreenshotTool/App.xaml.cs`
- Create: `tests/ScreenshotTool.Tests/ScreenshotTool.Tests.csproj`

**Interfaces:**
- Consumes: 无
- Produces: 可编译的 WPF 空应用 + 引用主项目的 xUnit 测试项目

- [ ] **Step 1: 安装 .NET 8 SDK（若 `dotnet --version` 失败）**

```powershell
winget install Microsoft.DotNet.SDK.8 --accept-package-agreements --accept-source-agreements
# 新开终端后验证
dotnet --version
```

Expected: 输出以 `8.` 开头的版本号。

- [ ] **Step 2: 创建解决方案与项目**

在仓库根目录 `c:\My workspace\截图工具` 执行：

```powershell
dotnet new sln -n ScreenshotTool
dotnet new wpf -n ScreenshotTool -o src/ScreenshotTool -f net8.0
dotnet new xunit -n ScreenshotTool.Tests -o tests/ScreenshotTool.Tests -f net8.0
dotnet sln ScreenshotTool.sln add src/ScreenshotTool/ScreenshotTool.csproj
dotnet sln ScreenshotTool.sln add tests/ScreenshotTool.Tests/ScreenshotTool.Tests.csproj
dotnet add tests/ScreenshotTool.Tests/ScreenshotTool.Tests.csproj reference src/ScreenshotTool/ScreenshotTool.csproj
```

编辑 `src/ScreenshotTool/ScreenshotTool.csproj`，确保包含：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWPF>true</UseWPF>
    <UseWindowsForms>true</UseWindowsForms>
    <ApplicationManifest>app.manifest</ApplicationManifest>
    <ApplicationIcon>Assets\tray.ico</ApplicationIcon>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="System.Drawing.Common" Version="8.0.11" />
  </ItemGroup>
</Project>
```

测试项目 `TargetFramework` 改为 `net8.0-windows`，并加：

```xml
<UseWPF>true</UseWPF>
```

- [ ] **Step 3: 验证编译**

```powershell
dotnet build ScreenshotTool.sln
```

Expected: `Build succeeded`

- [ ] **Step 4: Commit**

```powershell
git add ScreenshotTool.sln src/ScreenshotTool tests/ScreenshotTool.Tests
git commit -m "chore: scaffold .NET 8 WPF solution and test project"
```

---

### Task 2: DPI 清单、Win32、单实例启动

**Files:**
- Create: `src/ScreenshotTool/app.manifest`
- Create: `src/ScreenshotTool/Native/Win32.cs`
- Modify: `src/ScreenshotTool/App.xaml.cs`
- Modify: `src/ScreenshotTool/App.xaml`（关闭启动窗口或指向空）

**Interfaces:**
- Consumes: 无
- Produces:
  - `Win32`：`RegisterHotKey` / `UnregisterHotKey` / `GetCursorPos` / GDI 截屏相关 P/Invoke
  - `App`：named mutex 单实例；第二次启动 MessageBox「已在托盘运行」后退出

- [ ] **Step 1: 写入 `app.manifest`（PerMonitorV2）**

```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <assemblyIdentity version="1.0.0.0" name="ScreenshotTool"/>
  <compatibility xmlns="urn:schemas-microsoft-com:compatibility.v1">
    <application>
      <supportedOS Id="{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}"/>
    </application>
  </compatibility>
  <application xmlns="urn:schemas-microsoft-com:asm.v3">
    <windowsSettings>
      <dpiAware xmlns="http://schemas.microsoft.com/SMI/2005/WindowsSettings">true/PM</dpiAware>
      <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2</dpiAwareness>
    </windowsSettings>
  </application>
</assembly>
```

- [ ] **Step 2: 实现 `Native/Win32.cs`（最小可用集）**

```csharp
using System.Runtime.InteropServices;

namespace ScreenshotTool.Native;

internal static class Win32
{
    public const int WM_HOTKEY = 0x0312;
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    public static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    public static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int w, int h,
        IntPtr hdcSrc, int xSrc, int ySrc, int rop);

    public const int SRCCOPY = 0x00CC0020;
}
```

- [ ] **Step 3: `App.xaml.cs` 单实例**

```csharp
using System.Threading;
using System.Windows;

namespace ScreenshotTool;

public partial class App : Application
{
    private const string MutexName = "Global\\ScreenshotTool.SingleInstance";
    private Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show("截图工具已在托盘运行。", "截图工具",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        // 后续 Task 接入 TrayIconService；临时：先不 Show 任何窗口
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
```

`App.xaml` 去掉 `StartupUri`，仅保留：

```xml
<Application x:Class="ScreenshotTool.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <Application.Resources/>
</Application>
```

- [ ] **Step 4: Build**

```powershell
dotnet build ScreenshotTool.sln
```

Expected: PASS

- [ ] **Step 5: Commit**

```powershell
git add src/ScreenshotTool
git commit -m "feat: add DPI manifest, Win32 P/Invoke, and single-instance app"
```

---

### Task 3: AppSettings 读写

**Files:**
- Create: `src/ScreenshotTool/Shell/AppSettings.cs`
- Create: `tests/ScreenshotTool.Tests/AppSettingsTests.cs`

**Interfaces:**
- Consumes: `System.Text.Json`
- Produces:
  - `class AppSettings { uint HotkeyModifiers; uint HotkeyKey; string? DefaultSaveDirectory; string StrokeColor; double StrokeThickness; }`
  - `static AppSettings Load()` / `void Save()`
  - 路径：`%AppData%/ScreenshotTool/settings.json`
  - 默认：`HotkeyModifiers = MOD_CONTROL|MOD_SHIFT`，`HotkeyKey = 0x41`（A），`StrokeColor = "#FF0000"`，`StrokeThickness = 3`

- [ ] **Step 1: 写失败测试**

```csharp
using ScreenshotTool.Shell;

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
```

- [ ] **Step 2: 运行测试确认失败**

```powershell
dotnet test tests/ScreenshotTool.Tests --filter AppSettingsTests
```

Expected: FAIL（类型不存在）

- [ ] **Step 3: 实现 `AppSettings.cs`**

```csharp
using System.IO;
using System.Text.Json;
using ScreenshotTool.Native;

namespace ScreenshotTool.Shell;

public sealed class AppSettings
{
    public uint HotkeyModifiers { get; set; } = Win32.MOD_CONTROL | Win32.MOD_SHIFT;
    public uint HotkeyKey { get; set; } = 0x41;
    public string? DefaultSaveDirectory { get; set; }
    public string StrokeColor { get; set; } = "#FFFF0000";
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
```

将 `Win32` 的热键常量改为 `public`（若仍为 internal，测试项目需 `InternalsVisibleTo`；更简单：在 `AppSettings` 中写死 `0x0002 | 0x0004` 默认值，避免测试依赖 internal）。

- [ ] **Step 4: 测试通过**

```powershell
dotnet test tests/ScreenshotTool.Tests --filter AppSettingsTests
```

Expected: PASS

- [ ] **Step 5: Commit**

```powershell
git add src/ScreenshotTool/Shell/AppSettings.cs tests/ScreenshotTool.Tests/AppSettingsTests.cs
git commit -m "feat: add AppSettings JSON load/save with defaults"
```

---

### Task 4: RegionGeometry 矩形与套索裁切

**Files:**
- Create: `src/ScreenshotTool/Capture/RegionGeometry.cs`
- Create: `tests/ScreenshotTool.Tests/RegionGeometryTests.cs`

**Interfaces:**
- Consumes: `System.Windows.Media.Imaging.WriteableBitmap`，点列表
- Produces:
  - `static Int32Rect GetBoundingRect(IReadOnlyList<Point> points)`
  - `static WriteableBitmap CropRectangle(BitmapSource source, Int32Rect rect)`
  - `static WriteableBitmap CropLasso(BitmapSource source, IReadOnlyList<Point> points)` — 路径外 alpha=0；路径过短（点数 < 3 或包围盒宽高均 < 2）返回 `null`

- [ ] **Step 1: 写失败测试**

```csharp
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenshotTool.Capture;

public class RegionGeometryTests
{
    private static WriteableBitmap SolidBitmap(int w, int h, Color c)
    {
        var bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
        var stride = w * 4;
        var pixels = new byte[h * stride];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = c.B; pixels[i + 1] = c.G; pixels[i + 2] = c.R; pixels[i + 3] = c.A;
        }
        bmp.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
        return bmp;
    }

    [Fact]
    public void CropRectangle_ReturnsExpectedSize()
    {
        var src = SolidBitmap(100, 80, Colors.Red);
        var cropped = RegionGeometry.CropRectangle(src, new Int32Rect(10, 10, 30, 20));
        Assert.Equal(30, cropped.PixelWidth);
        Assert.Equal(20, cropped.PixelHeight);
    }

    [Fact]
    public void CropLasso_OutsideIsTransparent()
    {
        var src = SolidBitmap(50, 50, Colors.Blue);
        var points = new[]
        {
            new Point(10, 10), new Point(40, 10), new Point(40, 40), new Point(10, 40)
        };
        var cropped = RegionGeometry.CropLasso(src, points);
        Assert.NotNull(cropped);
        // 裁切图角落相对包围盒外沿：包围盒内但路径外的点应透明；中心应不透明
        var center = SampleAlpha(cropped!, cropped!.PixelWidth / 2, cropped.PixelHeight / 2);
        Assert.Equal(255, center);
    }

    [Fact]
    public void CropLasso_TooShort_ReturnsNull()
    {
        var src = SolidBitmap(20, 20, Colors.Red);
        Assert.Null(RegionGeometry.CropLasso(src, new[] { new Point(1, 1), new Point(2, 1) }));
    }

    private static byte SampleAlpha(WriteableBitmap bmp, int x, int y)
    {
        var pixels = new byte[4];
        bmp.CopyPixels(new Int32Rect(x, y, 1, 1), pixels, 4, 0);
        return pixels[3];
    }
}
```

- [ ] **Step 2: 运行确认失败**

```powershell
dotnet test --filter RegionGeometryTests
```

Expected: FAIL

- [ ] **Step 3: 实现 `RegionGeometry.cs`**

实现要点：
- `CropRectangle`：用 `CroppedBitmap` 或手动 `CopyPixels` 到新 `WriteableBitmap`
- `CropLasso`：算包围盒 → 从源图裁包围盒 → 用 `PathGeometry`/`FillContains` 或逐像素射线法，路径外 alpha 置 0
- 坐标相对包围盒左上角

- [ ] **Step 4: 测试通过**

```powershell
dotnet test --filter RegionGeometryTests
```

Expected: PASS

- [ ] **Step 5: Commit**

```powershell
git add src/ScreenshotTool/Capture/RegionGeometry.cs tests/ScreenshotTool.Tests/RegionGeometryTests.cs
git commit -m "feat: add rectangle and lasso region crop helpers"
```

---

### Task 5: ScreenCaptureService

**Files:**
- Create: `src/ScreenshotTool/Capture/ScreenCaptureService.cs`

**Interfaces:**
- Consumes: `Win32.BitBlt` 或 `Graphics.CopyFromScreen`
- Produces:
  - `record VirtualScreenInfo(int X, int Y, int Width, int Height)`
  - `static VirtualScreenInfo GetVirtualScreen()` — 使用 Win32 `GetSystemMetrics` SM_XVIRTUALSCREEN 等，或 `SystemParameters` + DPI 换算到物理像素
  - `static BitmapSource CaptureVirtualScreen()` — 返回整块虚拟桌面 Bgra32 图

- [ ] **Step 1: 扩展 `Win32.cs` 增加 GetSystemMetrics**

```csharp
[DllImport("user32.dll")]
public static extern int GetSystemMetrics(int nIndex);

public const int SM_XVIRTUALSCREEN = 76;
public const int SM_YVIRTUALSCREEN = 77;
public const int SM_CXVIRTUALSCREEN = 78;
public const int SM_CYVIRTUALSCREEN = 79;
```

- [ ] **Step 2: 实现捕获服务**

```csharp
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenshotTool.Native;
using PixelFormat = System.Drawing.Imaging.PixelFormat;

namespace ScreenshotTool.Capture;

public static class ScreenCaptureService
{
    public readonly record struct VirtualScreenInfo(int X, int Y, int Width, int Height);

    public static VirtualScreenInfo GetVirtualScreen() => new(
        Win32.GetSystemMetrics(Win32.SM_XVIRTUALSCREEN),
        Win32.GetSystemMetrics(Win32.SM_YVIRTUALSCREEN),
        Win32.GetSystemMetrics(Win32.SM_CXVIRTUALSCREEN),
        Win32.GetSystemMetrics(Win32.SM_CYVIRTUALSCREEN));

    public static BitmapSource CaptureVirtualScreen()
    {
        var vs = GetVirtualScreen();
        using var bmp = new Bitmap(vs.Width, vs.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(vs.X, vs.Y, 0, 0, new System.Drawing.Size(vs.Width, vs.Height));
        }
        return ToBitmapSource(bmp);
    }

    private static BitmapSource ToBitmapSource(Bitmap bmp)
    {
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var wb = new WriteableBitmap(bmp.Width, bmp.Height, 96, 96, PixelFormats.Bgra32, null);
            wb.WritePixels(new Int32Rect(0, 0, bmp.Width, bmp.Height), data.Scan0, data.Stride * bmp.Height, data.Stride);
            wb.Freeze();
            return wb;
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }
}
```

- [ ] **Step 3: 手工冒烟（可选控制台/临时按钮）** — 在后续 Overlay 接入时验证；此处先 `dotnet build`

```powershell
dotnet build ScreenshotTool.sln
```

Expected: PASS

- [ ] **Step 4: Commit**

```powershell
git add src/ScreenshotTool/Capture/ScreenCaptureService.cs src/ScreenshotTool/Native/Win32.cs
git commit -m "feat: capture full virtual screen to BitmapSource"
```

---

### Task 6: CaptureOverlayWindow（矩形 + 套索）

**Files:**
- Create: `src/ScreenshotTool/Capture/CaptureOverlayWindow.xaml`
- Create: `src/ScreenshotTool/Capture/CaptureOverlayWindow.xaml.cs`

**Interfaces:**
- Consumes: `ScreenCaptureService`, `RegionGeometry`
- Produces:
  - `event Action<BitmapSource>? CaptureConfirmed`
  - `event Action? CaptureCancelled`
  - 窗口：无边框、置顶、覆盖虚拟屏；背景为截屏图；半透明暗罩；模式 Rectangle/Lasso
  - 快捷键：`R`/`F`/`Enter`/`Esc`；右键取消
  - 松手后显示确认栏（确认/重选）；路径过短忽略

- [ ] **Step 1: 实现 XAML 骨架**

窗口属性：`WindowStyle=None`, `AllowsTransparency=True`, `Topmost=True`, `ResizeMode=NoResize`  
定位：`Left=vs.X`, `Top=vs.Y`, `Width=vs.Width`, `Height=vs.Height`（注意 WPF DIP：若 PerMonitorV2 下与物理像素不一致，用物理像素/`CompositionTarget` 变换；优先用与 `GetSystemMetrics` 一致的物理尺寸，并通过 `SetProcessDpiAwarenessContext` 已声明保证对齐）。

内容：
- `Image` 全屏显示截屏
- 半透明黑色 Canvas 遮罩，选区「挖空」高亮
- 顶栏模式按钮：矩形 / 套索
- 选区完成后的确认条：确认、重选、尺寸文字

- [ ] **Step 2: 实现交互逻辑（code-behind）**

状态机：`Idle → Drawing → PendingConfirm →` 确认触发 `CaptureConfirmed` 或 重选回 `Idle`。

伪逻辑：

```csharp
public partial class CaptureOverlayWindow : Window
{
    public event Action<BitmapSource>? CaptureConfirmed;
    public event Action? CaptureCancelled;

    public static CaptureOverlayWindow ShowNew()
    {
        var screen = ScreenCaptureService.CaptureVirtualScreen();
        var vs = ScreenCaptureService.GetVirtualScreen();
        var w = new CaptureOverlayWindow(screen, vs);
        w.Show();
        w.Activate();
        return w;
    }
}
```

鼠标：
- 矩形：记录起点终点，画橡胶框
- 套索：收集点列，画折线；MouseUp 闭合

确认：
- 矩形 → `RegionGeometry.CropRectangle`
- 套索 → `RegionGeometry.CropLasso`；null 则重置

- [ ] **Step 3: 从 App 临时挂接测试入口（可随后替换）**

在 `OnStartup` 成功拿 mutex 后，先创建隐藏 `Window` 作热键消息泵预留；本 Task 可用托盘未就绪时的调试：`CaptureOverlayWindow.ShowNew()` 仅在开发调试开关下调用。正式接线在 Task 10。

- [ ] **Step 4: Build + 手工跑一次矩形/套索**

```powershell
dotnet build src/ScreenshotTool
dotnet run --project src/ScreenshotTool
```

Expected: 能全屏框选并得到裁切图事件（可用临时 MessageBox 显示像素尺寸）。

- [ ] **Step 5: Commit**

```powershell
git add src/ScreenshotTool/Capture/
git commit -m "feat: add multi-monitor capture overlay with rect and lasso"
```

---

### Task 7: 标注模型、命令与撤销栈

**Files:**
- Create: `src/ScreenshotTool/Editor/AnnotationModels.cs`
- Create: `src/ScreenshotTool/Editor/AnnotationCommands.cs`
- Create: `src/ScreenshotTool/Editor/UndoStack.cs`
- Create: `tests/ScreenshotTool.Tests/UndoStackTests.cs`

**Interfaces:**
- Consumes: 无
- Produces:
  - `enum ToolKind { Select, Rectangle, Ellipse, Arrow, Pen, Highlighter, Mosaic, Text }`
  - `abstract record AnnotationItem`
  - `record RectAnnotation`, `EllipseAnnotation`, `ArrowAnnotation`, `PathAnnotation`（Pen/Highlighter）, `TextAnnotation`
  - `interface IAnnotationCommand { void Do(); void Undo(); }`
  - `class AddAnnotationCommand`, `RemoveAnnotationCommand`, `MoveAnnotationCommand`
  - `class UndoStack { void Execute(IAnnotationCommand); void Undo(); void Redo(); bool CanUndo; bool CanRedo; }`

- [ ] **Step 1: 写 UndoStack 测试**

```csharp
public class UndoStackTests
{
    [Fact]
    public void Execute_Undo_Redo_Works()
    {
        var list = new List<int>();
        var stack = new UndoStack();
        stack.Execute(new DelegateCommand(
            () => list.Add(1),
            () => list.RemoveAt(list.Count - 1)));
        Assert.Single(list);
        stack.Undo();
        Assert.Empty(list);
        stack.Redo();
        Assert.Single(list);
    }
}
```

（`DelegateCommand` 可放在测试内或产品代码辅助。）

- [ ] **Step 2: 测试失败 → 实现模型与 UndoStack → 测试通过**

- [ ] **Step 3: Commit**

```powershell
git commit -m "feat: add annotation models, commands, and undo stack"
```

---

### Task 8: 马赛克命令（像素备份撤销）

**Files:**
- Create: `src/ScreenshotTool/Editor/MosaicHelper.cs`（或放入 AnnotationCommands）
- Modify: `src/ScreenshotTool/Editor/AnnotationCommands.cs`
- Create: `tests/ScreenshotTool.Tests/MosaicCommandTests.cs`

**Interfaces:**
- Consumes: `WriteableBitmap` 底图
- Produces:
  - `class MosaicCommand : IAnnotationCommand` — Do：备份 `Int32Rect` 像素 → 块大小默认 8px 像素化；Undo：写回备份
  - `static void ApplyMosaic(WriteableBitmap bmp, Int32Rect rect, int blockSize)`

- [ ] **Step 1: 测试**

```csharp
[Fact]
public void Mosaic_ChangesPixels_UndoRestores()
{
    var bmp = CreateSolid(32, 32, Colors.Red);
    // 在中心写一个不同颜色像素便于对比
    SetPixel(bmp, 16, 16, Colors.Blue);
    var before = GetPixel(bmp, 16, 16);
    var cmd = new MosaicCommand(bmp, new Int32Rect(8, 8, 16, 16), blockSize: 8);
    var stack = new UndoStack();
    stack.Execute(cmd);
    var mid = GetPixel(bmp, 16, 16);
    Assert.NotEqual(before, mid);
    stack.Undo();
    Assert.Equal(before, GetPixel(bmp, 16, 16));
}
```

（`GetPixel`/`SetPixel`/`CreateSolid` 在测试文件内实现为读写 BGRA 四元组。）

- [ ] **Step 2: 实现并让测试 PASS**

- [ ] **Step 3: Commit**

```powershell
git commit -m "feat: add undoable mosaic pixel command"
```

---

### Task 9: AnnotationCanvas + EditorWindow UI

**Files:**
- Create: `src/ScreenshotTool/Editor/AnnotationCanvas.cs`
- Create: `src/ScreenshotTool/Editor/EditorWindow.xaml`
- Create: `src/ScreenshotTool/Editor/EditorWindow.xaml.cs`
- Create: `src/ScreenshotTool/Editor/ImageExportService.cs`

**Interfaces:**
- Consumes: 标注模型、UndoStack、MosaicCommand、Region 裁切结果
- Produces:
  - `EditorWindow(BitmapSource image)` — 工具栏 + 画布 + 底部按钮
  - `AnnotationCanvas`：按当前 `ToolKind` 处理鼠标；渲染矢量；选择/移动/Delete
  - `ImageExportService.Compose(WriteableBitmap baseImage, IEnumerable<AnnotationItem> items) -> BitmapSource`
  - `CopyToClipboard(BitmapSource)` — PNG；失败重试 3 次
  - `SaveToFile(BitmapSource, string path)`

工具栏控件：工具切换、颜色、线宽、撤销、重做  
底部：复制、保存、复制并保存、关闭（有未导出修改时确认）

文字工具：点击弹出 `TextBox` 浮层，Enter/失焦提交为 `TextAnnotation`。

- [ ] **Step 1: 实现 ImageExportService（纯逻辑优先）**

用 `DrawingVisual` + `RenderTargetBitmap` 先画底图再画矢量。

- [ ] **Step 2: 实现 AnnotationCanvas 与各工具手势**

- [ ] **Step 3: 实现 EditorWindow 接线**

- [ ] **Step 4: Build + 手工验证各工具与导出**

```powershell
dotnet build ScreenshotTool.sln
```

- [ ] **Step 5: Commit**

```powershell
git commit -m "feat: add annotation editor with export copy and save"
```

---

### Task 10: 托盘 + 热键 + 主流程串联

**Files:**
- Create: `src/ScreenshotTool/Shell/TrayIconService.cs`
- Create: `src/ScreenshotTool/Shell/HotkeyService.cs`
- Create: `src/ScreenshotTool/Assets/tray.ico`（可用简单纯色 ico 或从系统图标导出）
- Modify: `src/ScreenshotTool/App.xaml.cs`

**Interfaces:**
- Consumes: `AppSettings`, `CaptureOverlayWindow`, `EditorWindow`
- Produces:
  - `TrayIconService`：菜单「截图」「设置」「退出」；`Dispose` 清理
  - `HotkeyService(Window messageWindow, AppSettings)`：`Register`/`Unregister`；触发 `HotkeyPressed`
  - 流程：`StartCapture()` → Overlay → 确认 → `new EditorWindow(bmp).Show()`；取消则结束

实现热键：隐藏 `Window`（0 尺寸）作消息泵，`HwndSource` / `ComponentDispatcher` 收 `WM_HOTKEY`。

- [ ] **Step 1: 实现 TrayIconService（NotifyIcon）**

```csharp
_notifyIcon = new NotifyIcon
{
    Icon = new Icon(pathToIco),
    Visible = true,
    Text = "截图工具"
};
_notifyIcon.ContextMenuStrip = new ContextMenuStrip();
// 截图 / 设置 / 退出
```

- [ ] **Step 2: 实现 HotkeyService；注册失败 MessageBox**

- [ ] **Step 3: App.OnStartup 装配全程**

```csharp
_settings = AppSettings.Load();
_tray = new TrayIconService(StartCapture, OpenSettings, Shutdown);
_hotkeyWindow = CreateMessageWindow();
_hotkeys = new HotkeyService(_hotkeyWindow, _settings, StartCapture);
_hotkeys.Register();
```

注意：UI 必须在 STA；捕获时若 Overlay 已打开则忽略重复触发。

- [ ] **Step 4: 端到端手工验收（对照规格成功标准）**

- [ ] **Step 5: Commit**

```powershell
git commit -m "feat: wire tray, hotkey, capture, and editor pipeline"
```

---

### Task 11: 设置窗口

**Files:**
- Create: `src/ScreenshotTool/Shell/SettingsWindow.xaml`
- Create: `src/ScreenshotTool/Shell/SettingsWindow.xaml.cs`

**Interfaces:**
- Consumes: `AppSettings`, `HotkeyService`
- Produces: 可修改热键修饰键+主键；保存后重新注册；失败则提示并恢复旧值；可设默认保存目录与默认描边颜色/线宽

- [ ] **Step 1: 实现设置 UI 与保存逻辑**

- [ ] **Step 2: 从托盘「设置」打开**

- [ ] **Step 3: Commit**

```powershell
git commit -m "feat: add settings window for hotkey and defaults"
```

---

### Task 12: README、发布脚本、收尾验收

**Files:**
- Create: `README.md`
- Create: `scripts/publish.ps1`

**Interfaces:**
- Consumes: 完整应用
- Produces: 本地可运行说明；`dotnet publish -c Release -r win-x64 --self-contained false`（或 true）

- [ ] **Step 1: 写 README（运行方式、热键、功能列表）**

- [ ] **Step 2: publish 脚本**

```powershell
dotnet publish src/ScreenshotTool/ScreenshotTool.csproj -c Release -r win-x64 --self-contained true -o dist/ScreenshotTool
```

- [ ] **Step 3: 对照规格 §12 成功标准逐项手工勾选**

- [ ] **Step 4: Commit**

```powershell
git commit -m "docs: add README and publish script"
```

---

## Self-Review

| 规格项 | 对应 Task |
|--------|-----------|
| 托盘 + 热键 | Task 10 |
| 多屏矩形/套索 | Task 5–6 |
| 标注工具全套 | Task 7–9 |
| 马赛克可撤销 | Task 8 |
| 复制/保存/复制并保存 | Task 9 |
| 设置热键 | Task 3, 11 |
| 单实例 / DPI | Task 2 |
| 单元测试裁切/撤销/马赛克 | Task 4, 7, 8 |

占位符扫描：无 TBD。类型名在各 Task Interfaces 块保持一致（`UndoStack`、`MosaicCommand`、`RegionGeometry`、`CaptureOverlayWindow`）。
