# Windows 截图标注小工具

常驻系统托盘的 Windows 截图工具，支持多显示器矩形/套索截图、标注编辑，以及复制与保存。

## 环境要求

- Windows 10 / 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)（开发、调试）
- 目标机若使用框架依赖发布，需安装 [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)；自包含发布无需单独安装运行时

若终端中 `dotnet` 不可用，可使用用户级 SDK 路径：

```powershell
$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
# 或直接调用
& "$env:USERPROFILE\.dotnet\dotnet.exe" --version
```

## 快速运行

在仓库根目录执行：

```powershell
dotnet run --project src/ScreenshotTool/ScreenshotTool.csproj
```

启动后无主窗口，仅在系统托盘显示图标。若已有实例在运行，新进程会退出并提示已在托盘运行。

## 使用方式

### 全局热键

默认热键：**Ctrl+Shift+A**。可在「设置」中修改；若与其他程序冲突，会提示注册失败并保持旧热键。

### 托盘菜单

右键托盘图标：

- **截图** — 进入全屏捕获遮罩（双击托盘图标同样触发）
- **设置** — 修改热键、默认保存目录、默认描边颜色与线宽
- **退出** — 关闭应用

### 捕获遮罩

- 默认矩形模式；按 **R** 或点击「矩形」切换矩形，按 **F** 或点击「套索」切换自由套索
- 矩形：拖拽框选，松手后可确认或重选；双击或 **Enter** 确认
- 套索：按下拖动绘制路径，松手自动闭合；路径外区域透明（PNG alpha）
- **Esc** 或右键：取消捕获，回到托盘

### 标注编辑器

工具栏提供：选择、矩形、椭圆、箭头、画笔、荧光笔、文字。可调颜色与线宽。

- **Ctrl+Z** / **Ctrl+Y**：撤销 / 重做
- **Delete**：删除选中的矢量标注
- 底部按钮：**复制**（剪贴板 PNG）、**保存**（另存为）、**复制并保存**、**关闭**

## 功能一览

| 类别 | 功能 |
|------|------|
| 截图 | 多显示器虚拟桌面截屏；矩形框选；自由套索（透明边缘） |
| 标注 | 矩形、椭圆、箭头、画笔、荧光笔、文字 |
| 编辑 | 撤销/重做；矢量标注选择与删除 |
| 导出 | 复制到剪贴板、保存 PNG、复制并保存 |
| 系统集成 | 托盘常驻、全局热键、单实例、Per-Monitor DPI Aware |

## 构建与测试

```powershell
dotnet build ScreenshotTool.sln
dotnet test ScreenshotTool.sln
```

## 发布

使用仓库内脚本生成自包含 win-x64 发布包：

```powershell
.\scripts\publish.ps1
```

输出目录：`dist/ScreenshotTool/`。可直接运行其中的 `ScreenshotTool.exe`，无需在目标机安装 .NET 运行时。

手动发布等价命令：

```powershell
dotnet publish src/ScreenshotTool/ScreenshotTool.csproj -c Release -r win-x64 --self-contained true -o dist/ScreenshotTool
```

## 设置文件

用户设置保存在：

`%AppData%\ScreenshotTool\settings.json`

## 项目结构

```
src/ScreenshotTool/     主应用（WPF）
tests/ScreenshotTool.Tests/   单元测试
scripts/publish.ps1     发布脚本
docs/                   设计规格与文档
```
