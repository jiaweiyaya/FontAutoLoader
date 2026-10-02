# FontAutoLoader

<p align="center">
  <strong>专为番剧压制与字幕爱好者打造的 ASS 字幕字体自动化临时加载工具</strong>
</p>

<p align="center">
  <a href="https://github.com/jiaweiyaya/FontAutoLoader"><img src="https://img.shields.io/badge/GitHub-Repository-blue?logo=github" alt="Repository" /></a>
  <img src="https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011%20(x64)-0078D4?logo=windows" alt="Platform" />
  <img src="https://img.shields.io/badge/.NET-8.0%20%7C%2010.0-512BD4?logo=dotnet" alt=".NET" />
  <img src="https://img.shields.io/badge/UI-WinUI%203%20%2F%20Windows%20App%20SDK-2D7D9A" alt="UI" />
  <a href="https://www.gnu.org/licenses/gpl-3.0"><img src="https://img.shields.io/badge/License-GPL%20v3.0-green.svg" alt="License: GPL v3" /></a>
</p>

---

## 项目简介

在观看外挂字幕番剧或进行视频压制时，ASS 字幕往往依赖大量特殊样式字体。如果为了几集动画而在系统中永久安装成百上千个字体，不仅会导致系统字体文件夹严重膨胀，还会拖慢设计软件和系统的启动速度。

**FontAutoLoader** 采用 Windows GDI 临时资源映射机制，能够在不修改系统注册表、不产生物理写入残留的前提下，将指定字幕所需的字体临时注册到当前用户会话中。播放器即开即看，关闭软件时自动触发带进度条的平滑释放，实现真正的“即用即载、随走随消”。

项目开源地址：[https://github.com/jiaweiyaya/FontAutoLoader](https://github.com/jiaweiyaya/FontAutoLoader)

---

## 核心特性

### 1. 智能 ASS 字幕字体解析与视图切换
- 支持单选与批量拖入多个 `.ass` 字幕文件。
- 深度提取样式表定义与行内 `\fn` 自定义字体标签。
- 提供 **整体去重视图** 与 **按文件折叠视图**，清晰掌握各个字幕文件的独立字体需求。

### 2. 免安装会话级挂载与安全释放
- 基于 Win32 底层 `AddFontResourceEx` 实现非持久化会话映射，对所有运行中的播放器（如 MPC-HC、PotPlayer、mpv 等）全局即时生效。
- 软件拦截退出信号，关闭窗口时自动触发带进度条的平滑卸载流程，避免临时资源锁死。
- 支持在挂载/卸载操作中随时暂停并安全打断未执行任务。

### 3. 系统核心字体安全保护
- 内置 Windows 字体风险分析引擎：
  - **核心必需字体 (红色警示)**：严格保护 `Segoe UI`、`微软雅黑`、`宋体` 等系统渲染基石，阻止误删导致的系统界面崩溃。
  - **默认预装字体 (黄色警示)**：标记常用系统推荐字体并提供风险弹窗。
  - **扩展用户字体 (安全级别)**：支持自由查看、搜索、快速跳转与卸载删除。

### 4. 高性能本地字体索引库
- 内置 SQLite 高速缓存引擎，支持多目录全量递归扫描与元数据解析（支持 TTF、OTF、TTC 等常见格式）。
- 支持海量字体的分页浏览（单页 50 项）与多维度模糊检索（按字体名称、家族名或文件路径搜索）。
- 检索结果与当前临时挂载状态实时双向同步，已安装在系统的字体可一键定位高亮。

### 5. 极致流畅的高刷新率交互动效
- 界面专为 144Hz / 200Hz+ 高刷显示器深度调优。
- 进度条多段比例追踪、导航指示条无缝平移均采用基于 GPU 合成器的连续阻尼平滑跟踪算法。

---

## 技术架构

- **主程序核心**：C# / .NET (基于 Windows App SDK 与 WinUI 3 框架)
- **底层字体接口**：Windows GDI (`gdi32.dll` / `user32.dll`)
- **数据缓存与检索**：SQLite (`Microsoft.Data.Sqlite`)
- **安装与打包**：自研独立现代 WPF 安装向导 (`install.exe`)，支持自包含与便携解压即用模式

---

## 快速上手

### 便携版（解压即用）
1. 从 [Releases 页面](https://github.com/jiaweiyaya/FontAutoLoader/releases) 下载最新的 `FontAutoLoader-vX.Y.Z-Portable.zip`。
2. 解压到任意目录，直接双击运行 `FontAutoLoader.exe` 即可。

### 安装版
1. 下载 `FontAutoLoader-Setup-vX.Y.Z.zip` 并解压。
2. 运行包内的 `install.exe`，根据向导提示完成环境检测并选择安装目录。
3. 安装程序将自动部署必要组件并创建桌面与开始菜单快捷方式。

---

## 构建与二次开发

### 前置要求
- Windows 10 (1809 及以上) 或 Windows 11
- Visual Studio 2022 (或更新版本)，需勾选以下工作负载：
  - `.NET 桌面开发`（包含 Windows 应用程序打包工具）
  - `Windows 应用程序开发`
- .NET 8.0 SDK 或更高版本

### 编译源码
```powershell
# 克隆仓库
git clone https://github.com/jiaweiyaya/FontAutoLoader.git
cd FontAutoLoader

# 编译并发布主程序 (x64 自包含免依赖输出)
msbuild .\FontAutoLoader\FontAutoLoader.csproj /restore /t:Publish /p:Configuration=Release /p:Platform=x64 /p:SelfContained=true /p:PublishDir="dist\bin\x64\"

# 编译安装器
dotnet publish .\Installer\Installer.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o "dist\"
```

---

## 开源协议

本项目采用 **GNU General Public License v3.0 (GPL-3.0)** 许可协议开源。

有关协议的完整详细内容，请参阅根目录下的 [LICENSE](https://www.gnu.org/licenses/gpl-3.0.txt) 文件。