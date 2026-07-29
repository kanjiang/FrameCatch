# Windows 截图标注小工具 — 设计规格

**日期:** 2026-07-29  
**状态:** 待用户审阅  
**平台:** Windows 10/11  
**技术栈:** C# / .NET 8 / WPF

## 1. 目标

做一个常驻系统托盘的 Windows 截图小工具，支持：

- 矩形框选与自由手绘套索截图（多显示器）
- 截图后标注：矩形、椭圆、箭头、画笔、荧光笔、马赛克、文字
- 编辑器内「复制 / 保存 / 复制并保存」
- 全局热键 + 托盘入口

## 2. 非目标（第一版不做）

- 录屏、滚动长截图、窗口级一键截图
- 云同步、账号、插件市场
- 多边形点击围选（仅自由套索）
- OCR、钉图置顶窗口（可后续扩展）

## 3. 架构总览

应用单实例常驻。三层协作：

```
Tray + Hotkey (Shell)
        │
        ▼
Capture Overlay  ──裁切 Bitmap──►  Editor Window
   (多屏遮罩)                      (矢量标注 + 导出)
```

| 层 | 职责 |
|----|------|
| Shell | 托盘图标、菜单、全局热键、设置、单实例 |
| Capture | 虚拟桌面截屏、半透明遮罩、矩形/套索选区、裁切 |
| Editor | 工具栏、矢量标注、撤销重做、合成导出 |

**坐标约定:** 一律使用虚拟屏幕像素坐标；应用声明 Per-Monitor DPI Aware。

## 4. 用户流程

### 4.1 启动

1. 启动后不显示主窗口，仅托盘图标。
2. 若已有实例在运行，新进程退出并提示「已在托盘运行」。

### 4.2 触发截图

- 默认全局热键：`Ctrl+Shift+A`（设置中可改）
- 托盘菜单：「截图」「设置」「退出」

### 4.3 捕获 Overlay

1. 抓取整块虚拟桌面位图作为背景，覆盖所有显示器。
2. 默认矩形模式；可切换自由套索（快捷键 `R` / `F` 或工具按钮）。
3. **矩形:** 拖拽框选；松手后显示确认/重选；双击或 Enter 确认；显示宽高。裁切结果为不透明矩形位图。
4. **套索:** 按下拖动绘制路径，松手自动闭合并显示确认/重选；确认后按路径裁切，路径外像素透明（PNG alpha）。路径过短（几乎未移动）则忽略并保持捕获状态。
5. `Esc` 或右键：取消，回到托盘。

### 4.4 标注 Editor

1. 打开编辑窗口，显示裁切结果。
2. 选择工具绘制标注；可调颜色、线宽。
3. `Ctrl+Z` / `Ctrl+Y` 撤销重做；`Delete` 删除选中矢量标注。
4. 底部按钮：
   - **复制** → 剪贴板 PNG（保留透明）
   - **保存** → 另存为对话框（默认 PNG）
   - **复制并保存** → 两者都做
   - **关闭** → 丢弃（可二次确认，若有未导出修改）

## 5. 标注工具规格

| 工具 | 行为 | 存储方式 |
|------|------|----------|
| 矩形 | 拖拽描边矩形 | 矢量 |
| 椭圆 | 拖拽描边椭圆 | 矢量 |
| 箭头 | 起点→终点 + 箭头头部 | 矢量 |
| 画笔 | 自由路径描边 | 矢量 |
| 荧光笔 | 半透明粗笔路径 | 矢量 |
| 文字 | 点击放置，输入后确认；可改字号/颜色 | 矢量 |
| 马赛克 | 拖拽选区，对底层像素做块状像素化 | 位图像素操作（可撤销） |
| 选择 | 选中矢量标注以移动/删除 | — |

**马赛克撤销策略:** 执行前备份受影响矩形区域内的原始像素；撤销时写回。不支持在「选择」工具下移动马赛克块（已烧进底图）。

**导出合成顺序:** 当前底图（含已应用马赛克）→ 栅格化所有矢量标注 → 输出 PNG。

## 6. 模块与文件结构

```
ScreenshotTool/
  ScreenshotTool.csproj
  App.xaml / App.xaml.cs
  Shell/
    TrayIconService.cs
    HotkeyService.cs
    SettingsWindow.xaml(.cs)
    AppSettings.cs
  Capture/
    ScreenCaptureService.cs
    CaptureOverlayWindow.xaml(.cs)
    RegionGeometry.cs
  Editor/
    EditorWindow.xaml(.cs)
    AnnotationModels.cs
    AnnotationCanvas.cs
    AnnotationCommands.cs
    ImageExportService.cs
  Native/
    Win32.cs
  Assets/
    tray.ico
```

### 6.1 职责边界

- **ScreenCaptureService:** 仅负责按虚拟屏矩形抓取 `BitmapSource`/`WriteableBitmap`。
- **RegionGeometry:** 矩形与 `PathGeometry` 裁切、边界框计算；套索输出带 alpha 的位图。
- **AnnotationModels:** 各标注类型的不可变/可序列化数据（点、颜色、线宽、文本等）。
- **AnnotationCommands:** `IAnnotationCommand`（Do/Undo），含矢量增删改与马赛克像素备份。
- **ImageExportService:** 合成、剪贴板、文件保存；无 UI。

## 7. 设置（简单版）

持久化到 `%AppData%/ScreenshotTool/settings.json`：

- `HotkeyModifiers` / `HotkeyKey`（默认 Ctrl+Shift+A）
- `DefaultSaveDirectory`（可选）
- `StrokeColor` / `StrokeThickness` 默认值

设置窗口可改热键；注册失败时提示冲突并保持旧热键。

## 8. 错误处理

| 场景 | 行为 |
|------|------|
| 热键注册失败 | 消息提示，允许打开设置改键 |
| 截屏失败 / 显示器热插拔 | 关闭 Overlay，提示重试 |
| 保存路径无权限 | 提示并重开另存对话框 |
| 剪贴板被占用 | 短暂重试 2–3 次，仍失败则提示 |
| 第二次启动 | 退出新进程，托盘 balloon/提示已运行 |

## 9. 技术要点

- **截屏:** GDI `BitBlt` / `Graphics.CopyFromScreen` 覆盖虚拟桌面 `SystemParameters.VirtualScreen*`（经 DPI 换算到物理像素）。
- **热键:** `RegisterHotKey` / `UnregisterHotKey`。
- **托盘:** `NotifyIcon`（WinForms 互操作）或硬编码 Drawing 托盘；菜单中文。
- **DPI:** 清单启用 `PerMonitorV2`。
- **单实例:** named mutex。

## 10. 测试计划

**单元测试（优先）**

- 矩形裁切尺寸与偏移正确
- 套索裁切：路径外 alpha=0，路径内保留像素
- 撤销栈：添加矢量 → 撤销 → 重做
- 马赛克：应用后像素变化，撤销后恢复

**手工验收**

- 双显示器跨屏框选与套索
- 高 DPI（125%/150%）下选区与鼠标对齐
- 各标注工具视觉正确
- 复制到 QQ/微信/Word/画图
- 透明套索边缘在支持透明的应用中可见
- 热键冲突与改键

## 11. 前提与风险

- **前提:** 开发机需安装 .NET 8 SDK；目标机需 .NET 8 Desktop Runtime（或自包含发布）。
- **风险:** 多屏 + DPI 坐标偏移是最常见问题，需在真实多屏环境验证。
- **风险:** 部分应用粘贴不支持 PNG 透明，可接受（仍复制 PNG；必要时后续加 BMP 回退）。

## 12. 成功标准

1. 托盘常驻 + `Ctrl+Shift+A`（或自定义）可进入截图。
2. 矩形与自由套索均可完成截图；套索外透明。
3. 全部约定标注工具可用，撤销/重做可靠。
4. 复制、保存、复制并保存均可用。
5. 多显示器下选区与鼠标位置一致。
