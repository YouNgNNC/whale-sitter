# 🐳 whale-sitter

**DeepSeek Harness (dsh) 运行状态监控台** —— 一个 Windows 托盘小工具，让你不用再敲命令行：

> 打开面板 → 看状态灯 → 一键开关服务。关掉窗口？它乖乖缩到托盘里等你。

[English](README.en.md) | 简体中文

## 功能

- 🧰 **一键安装**：自动检测 Node.js 和 dsh，缺失时点一下即可自动下载安装（便携版 Node，免管理员权限）
- 🩺 **一键诊断**：一键生成环境报告（版本/路径/端口/HTTP/日志末尾），复制后可直接贴到 GitHub issue 求助
- 🧠 **智能状态**：自动区分「缺少 Node.js」「未安装 dsh」「端口被占用」「运行中/已停止」，给出对应操作
- 🟢 **状态灯**：每 2 秒自动检测 dsh web 服务，绿色 = 运行中（显示 PID），红色 = 已停止；运行时有呼吸动画
- 🎛️ **一键开关**：一个大按钮，运行中点击停止，停止时点击启动（托盘菜单里也能开关）
- 🐳 **系统托盘**：关闭窗口不退出，最小化到托盘（鲸鱼图标）；双击图标恢复面板；托盘菜单可启动/停止/退出
- 🚀 **自动拉起**：打开监控台时若服务未运行，自动帮你启动
- 🌗 **跟随系统明暗主题**：自动适配 Windows 浅色/深色模式（含深色标题栏），切换系统主题时实时更新
- ⚙️ **设置面板**：语言（跟随系统/中文/English）、主题（跟随系统/浅色/深色）、服务端口（默认 3080）
- ⏰ **开机自启开关**：界面上一键开启/关闭开机自启（写入注册表 HKCU，免管理员权限）
- 📂 **查看日志**：一键用记事本打开 dsh 服务日志
- 📋 **日志**：dsh 服务输出写入 `%AppData%\npm\dsh-web.log`
- 🪟 **零依赖**：仅用 Windows 自带的 .NET Framework 编译，无第三方库

## 界面预览

<!-- 截图占位：把运行界面截图保存为 docs/screenshot.png 后，把下面这行取消注释即可显示
![screenshot](docs/screenshot.png)
-->

## 环境要求

- Windows 10/11（64 位）
- Node.js（>= 18）
- 已全局安装 DeepSeek Harness：`npm install -g @deepseek-ai/dsh`

## 使用方法

### 方式一：直接运行（推荐）

从 [Releases 页面](https://github.com/YouNgNNC/whale-sitter/releases) 下载 `whale-sitter.exe`，双击运行。

> **SmartScreen 提示说明**：exe 未做代码签名，Windows 首次运行可能弹出蓝色"Windows 已保护你的电脑"提示——点 **更多信息 → 仍要运行** 即可。开源免费工具不签名属正常现象。

### 方式二：从源码构建

```bat
build.bat
```

构建产物为当前目录下的 `whale-sitter.exe`。源码仅需 Windows 自带的 csc（.NET Framework 4.x），无需安装 Visual Studio。

## 行为说明

| 操作 | 行为 |
|---|---|
| 点击窗口 ✕ | 不退出，最小化到系统托盘（有气泡提示） |
| 双击托盘鲸鱼图标 | 恢复面板窗口 |
| 托盘菜单「退出」 | 只关闭监控台，**不会停止 dsh 服务** |
| 窗口按钮/托盘菜单开关服务 | 立即生效，状态灯 2 秒内刷新 |

## 工作原理

- 通过 `npm prefix -g` 自动定位 dsh 安装目录（找不到时回退到 `%AppData%\npm`）
- 服务进程 = `node <npm-global>\node_modules\@deepseek-ai\dsh\lib\bin.js web`
- 状态检测：`netstat` 检查端口 3080 是否有 LISTENING 监听
- 停止服务：`taskkill /F /T` 终止监听 3080 的进程树
- 服务默认端口 **3080**，Web UI 地址 http://127.0.0.1:3080

## 文件说明

| 文件 | 说明 |
|---|---|
| `whale-sitter.cs` | C# WinForms 源码（单文件） |
| `whale-sitter.ico` | 托盘/窗口图标（DeepSeek 官方鲸鱼） |
| `build.bat` | 一键编译脚本 |
| `whale-sitter.exe` | 构建产物（发布版见 Releases） |

## 更新日志

- **v2.1.0**：设置面板（语言/主题/端口）、中英双语界面、GitHub Actions 打 tag 自动构建 Release、CI 构建检查、英文 README、issue 模板
- **v1.0.0**：首个正式版本 —— 一键安装（Node/dsh 自动检测与安装）、一键诊断、智能状态、状态监控与一键开关、系统托盘、自动拉起、跟随系统明暗主题、开机自启、打开界面/查看日志

## License

[MIT](LICENSE)
