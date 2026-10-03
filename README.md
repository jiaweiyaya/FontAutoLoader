# FontAutoLoader

番剧外挂字幕 / 压制专用字体免安装临时加载器。

[![GitHub Repository](https://img.shields.io/badge/GitHub-FontAutoLoader-181717?logo=github)](https://github.com/jiaweiyaya/FontAutoLoader)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011%20x64-0078D4?logo=windows)](https://github.com/jiaweiyaya/FontAutoLoader)
[![Framework](https://img.shields.io/badge/Framework-WinUI%203%20%7C%20.NET%208-512BD4?logo=dotnet)](https://github.com/jiaweiyaya/FontAutoLoader)
[![License](https://img.shields.io/badge/License-GPL%20v3.0-green.svg)](LICENSE.txt)

---

## 为什么制作它？

追番或压制时，一部动画可能随随便便就引用了几十个小众字体。如果每次都双击“安装”塞进 `C:\Windows\Fonts`：

1. 系统字体列表塞进上千个生僻字形，每次打开 Photoshop / Office 都要卡半天找字体；
2. 很多字体用完一次这辈子都不会再开第二次，但注册表和字体缓存已经被污染；
3. 手动卸载极其繁琐，稍不注意甚至会误删系统界面依赖的字形。

**FontAutoLoader** 利用 Windows GDI 的会话级字体加载机制，只把字体临时投射在当前登录会话内存中：**播放器能正常渲染，但不修改系统注册表，不往系统盘写入实体文件**。看完了随时卸载，或者直接退出软件，一切干干净净。

---

## 主要功能

- **ASS 语法精确解析**
  - 支持批量选择字幕文件。
  - 自动提取 `[V4+ Styles]` 样式表及 `Dialogue` 文本行内的 `\fn` 行内重写标签。
  - 支持“整体去重列表”与“按 ASS 文件折叠列表”两种视图切换，各剧集字体需求一目了然。

- **免安装临时挂载**
  - 基于 Windows GDI `AddFontResourceEx` 临时加载，当前会话中的播放器（MPC-HC、PotPlayer、mpv 等）即时生效。
  - 具备广播更新通知，无需重启播放器即可识别新挂载字体。
  - 单个字体可单独启停，批量挂载/卸载支持随时安全的打断暂停。

- **本地字体库智能检索**
  - 添加本地存放字体的文件夹，使用 SQLite 自动递归扫描并提取内部真实字体名称（兼容 TTF、OTF、TTC）。
  - 支持多条件模糊搜索与 50 条/页分页浏览，快速定位本地字体并一键手动挂载。

- **已安装字体分级与防误删保护**
  - 实时识别当前 Windows 系统注册表中已安装的字体。
  - 按照系统风险分为三级：**核心必需（红）**、**默认预装（黄）**、**用户扩展（绿）**。
  - 内置三重安全闸门：如需删除危险级别系统字体，必须在应用设置中手动解除保护并二次弹窗确认，防止误操作破坏系统界面。

- **桌面托盘体验**
  - 无边框暗色亚克力悬浮托盘菜单，支持边缘碰撞检测（贴近屏幕右边缘时自动向左展开）。
  - 托盘二级面板支持 10 条/页分页浏览当前已挂载字体，并配备防误触二次确认卸载按钮。
  - 退出程序时若存在挂载字体，自动弹出平滑进度条完成安全释放后再退出，防止系统资源锁死；支持开机静默入托盘与关窗最小化。

- **未来还会有更多可以加入的计划哦···**
  - 可以和我直接进行沟通或者提 **Issue** 给我哦，我会考虑并适当的采纳的。

---

## 快速使用

1. 打开应用，进入 **目录与索引管理** 标签页，添加你平时收集字体的目录，点击 **立即扫描并重建索引**。
2. 切回 **字幕字体挂载**，点击 **选择 ASS 字幕**（支持多选）。
3. 程序自动对比本地库与系统字体：
   - 绿色标签：系统已安装，无需处理；
   - 紫色/蓝色标签：当前已处于挂载状态；
   - 黄色标签：本地库中未找到对应文件。
4. 点击 **挂载字幕字体**，然后使用其他的播放器直接挂载上字幕然后开播即可。看完后点击 **卸载字幕字体** 或直接退出应用即可。

---

## 编译运行

### 环境依赖
- Windows 10 (1809+) 或 Windows 11
- Visual Studio 2022 或更高（需安装 `.NET 桌面开发`、`Windows 应用程序开发` 工作负载）
- .NET 8.0 SDK

### 源码构建
```powershell
# 克隆仓库
git clone https://github.com/jiaweiyaya/FontAutoLoader.git
cd FontAutoLoader

# 编译并发布独立运行包 (x64)
msbuild .\FontAutoLoader\FontAutoLoader.csproj /restore /t:Publish /p:Configuration=Release /p:Platform=x64 /p:SelfContained=true
```

---

## 开源协议

本项目基于 [GPL-3.0 License](LICENSE.txt) 协议开源。请务必遵守开源协议。

---

## 联系与反馈

- **QQ 交流群**：1074858712（我另一个项目 FlowCourse 的群，欢迎来吹水聊天 / 反馈 Bug）
- **GitHub Issues**：[点我提交 Issue 反馈](https://github.com/jiaweiyaya/FontAutoLoader/issues)
- **开发者邮箱**：[2652520612@qq.com](mailto:2652520612@qq.com)