# whale-sitter 实现文档（IMPLEMENTATION）

> 本文档记录 whale-sitter 的完整实现方式：技术栈、架构、各功能实现细节、构建与发布流程、踩坑记录。
> 对应版本：v1.0.0（2026-08-14）

## 1. 技术栈

- **语言/框架**：C# 5（.NET Framework 4.x）/ WinForms，单文件 `whale-sitter.cs`（约 37 KB）
- **编译器**：Windows 自带 `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`，无需安装 Visual Studio
- **第三方依赖**：零（仅 .NET Framework 自带程序集）
- **图标**：DeepSeek 官方鲸鱼（源自 dsh 仓库 `apps/web/public/favicon.svg`，转透明 PNG 后打包多尺寸 .ico）

## 2. 代码架构

```
Program
 ├─ Version = "1.0.0"
 └─ Main(): 单实例 Mutex（WhaleSitter.SingleInstance）→ Application.Run(MainForm)

MainForm（主窗体 404×268，FixedSingle）
 ├─ 头部：whale 图标(PictureBox) + 标题 + 副标题(v版本号)
 ├─ 状态卡片 CardPanel：状态灯(dot ●) + 状态文字 + 提示行(statusHint)
 ├─ 大按钮 RoundedButton(toggle)：语义随状态变化
 ├─ 操作栏：开机自启 / 打开界面 / 查看日志 / 一键诊断
 └─ 托盘 NotifyIcon：双击恢复、右键菜单（打开/界面/日志/诊断/启停/退出）

辅助类型
 ├─ Palette 结构体：明/暗两套配色（窗口底/卡片/边框/文字/强调/成功/警告/危险/按钮）
 ├─ CardPanel：自绘圆角卡片（GraphicsPath + FillPath + DrawPath）
 └─ RoundedButton：自绘圆角按钮（OnPaint 绘制圆角矩形 + TextRenderer 文字）
```

### 核心逻辑实现表

| 功能 | 实现方式 |
|---|---|
| 状态检测 | 每 2 秒跑 `netstat -ano -p tcp`，解析 `:3080` + `LISTENING` 行的 PID |
| 启动服务 | `node <npm全局目录>\node_modules\@deepseek-ai\dsh\lib\bin.js web`，stdout/stderr 重定向写入日志 |
| 停止服务 | `taskkill /F /T /PID <监听3080的PID>`（找不到则杀自己启动的进程） |
| 自动拉起 | 窗体 OnShown 时若环境齐且未运行则自动 StartServer |
| 系统托盘 | 关闭窗口 → `e.Cancel + Hide()` + 气泡提示；托盘菜单「退出」才真正退出（不停服务） |
| 明暗主题 | 读 `HKCU\...\Themes\Personalize\AppsUseLightTheme`；WM_SETTINGCHANGE 时实时重读重绘；P/Invoke `DwmSetWindowAttribute(attr=20)` 深色标题栏 |
| 开机自启 | 读写 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 值 `whale-sitter`（免管理员） |
| 一键安装 | 见下节 |
| 一键诊断 | 收集系统/Node/npm 目录/dsh 入口/端口/HTTP/日志末尾 → 弹窗文本框 + 复制按钮 |

### 一键安装流程

```
检测：NodeAvailable()（便携目录或 PATH 上有 node）
      DshInstalled()（DshEntry 文件存在）
按钮语义：缺 Node → "一键安装 Node.js + dsh"；缺 dsh → "一键安装 DeepSeek Harness"

InstallAll（async，全程 UI 提示进度）：
 1. 缺 Node 时 InstallNodeAsync：
    a. 依次尝试镜像根 npmmirror / nodejs.org
    b. 下载 <root>latest/SHASUMS256.txt，正则提取 node-v<版本>-win-x64.zip
    c. WebClient 下载 zip → ZipFile 解压到 %LOCALAPPDATA%\whale-sitter\node\
    d. 找到含 node.exe 的子目录作为便携 Node 目录
 2. InstallDshAsync：用便携 node 直接执行
    <nodeDir>\node_modules\npm\bin\npm-cli.js install -g @deepseek-ai/dsh
    （注意：不能直接 Process 跑 npm.cmd——UseShellExecute=false 无法执行 .bat/.cmd）
 3. 刷新路径 → 启动服务
```

关键点：便携 Node 安装后，`NpmDir`/`DshEntry`/`NodeExe` 均改为**动态属性**（优先便携目录，回退系统 npm prefix），保证装完即可用、自包含。

## 3. 构建方法

```bat
build.bat
```

编译命令（等价）：
```
csc /nologo /target:winexe /out:whale-sitter.exe /win32icon:whale-sitter.ico \
    /r:System.Windows.Forms.dll /r:System.Drawing.dll \
    /r:System.IO.Compression.FileSystem.dll \
    whale-sitter.cs
```

**重要**：`whale-sitter.cs` 必须为 **UTF-8 with BOM** 编码。csc 对无 BOM 的源文件按系统 ANSI 代码页（GBK）解析，中文注释/字符串会乱码或编译失败。

## 4. GitHub 发布流程（本机实测）

网络环境：`github.com` 被墙需走 Clash 代理（127.0.0.1:7890）；`api.github.com` **直连可用**。

```powershell
# 1. 登录（注意 NO_PROXY，否则最后一步 graphql 走代理会 TLS 超时）
$env:HTTPS_PROXY = "http://127.0.0.1:7890"
$env:HTTP_PROXY  = "http://127.0.0.1:7890"
$env:NO_PROXY    = "api.github.com"
gh auth login            # GitHub.com → HTTPS → 浏览器设备码

# 2. git 走代理（已配全局 http.proxy https.proxy）

# 3. 建仓推送
gh repo create whale-sitter --public --source . --push

# 4. 压成单提交（v1.0.0，重写历史，仅适用于无协作者新仓库）
git checkout --orphan tmp-final
git add -A
git -c user.name="YouNgNNC" -c user.email="YouNgNNC@users.noreply.github.com" commit -m "v1.0.0: ..."
git branch -M tmp-final main
git push --force-with-lease origin main
```

## 5. 踩坑记录

| 坑 | 解决 |
|---|---|
| GitHub 直连被重置（clone/curl/gh 均超时） | 走 Clash 代理；`api.github.com` 直连（NO_PROXY 排除） |
| `npx @deepseek-ai/dsh` 无输出卡死 | 改用 `npm i -g @deepseek-ai/dsh` |
| 源码 zip 解压无 `.git`，lefthook 报错 | 无碍构建；不装 git hooks |
| cmd 分隔符是 `&`/`&&`，不是 `;`；管道无 `head` | 本机 shell 是 cmd/git-bash 混用，注意区分 |
| csc 中文乱码 | 源文件加 UTF-8 BOM |
| ZipFile 编译不过 | 增加 `/r:System.IO.Compression.FileSystem.dll` |
| Process 跑不了 npm.cmd（UseShellExecute=false） | 用 node 执行 npm-cli.js；**需要跑 npm 命令时经 `cmd.exe /c`**（v2.2.0 曾在 ResolveSystemNpmCliAsync 重新踩坑：`Process.Start("npm")` 直接 Win32Exception，v2.2.1 修复为 `cmd /c where node` 定位 npm-cli.js + `cmd /c npm root -g` 回退） |
| 白底鲸鱼图直接当图标有白方块 | 亮度→alpha + 从四边洪泛区分"外部背景"与"肚皮内部留白"，肚皮保留白色 |
| 重复启动弹"已在运行"框 | 单实例 Mutex 的预期行为（托盘找图标即可） |
| 旧进程占用 exe 无法覆盖构建 | 先 `taskkill /F /IM whale-sitter.exe` 再 build |

## 6. 本机运行验证（v1.0.0）

- 进程：`whale-sitter.exe` 单一实例
- 服务：127.0.0.1:3080 LISTENING，HTTP 200
- 日志：`%AppData%\npm\dsh-web.log` 记录 dsh 启动输出
- 桌面入口：`E:\Desktop\DeepSeek Harness.lnk` → `C:\Users\YouNg_LEGION\whale-sitter\whale-sitter.exe`

## 7. 相关文档

- 规划与决策见 [ROADMAP.md](ROADMAP.md)
- 使用说明见根目录 [README.md](../README.md)
