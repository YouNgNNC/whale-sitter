# whale-sitter 全量测试文档（TESTING）

> 对应版本：v2.1.0（2026-08-14）
> 测试环境：Windows 10 19045 x64，Node v24.15.0，dsh 0.1.0-rc.6

## 测试方法说明

- 应用为 WinForms GUI，自动启动/停止服务、状态灯轮询等行为通过**进程/端口/HTTP/窗口标题**四项外部信号验证
- 注册表设置（`HKCU\Software\whale-sitter`）在测试间用 PowerShell 写入/删除
- 语言是否生效通过 `Get-Process whale-sitter` 的 `MainWindowTitle` 验证（标题随语言变化）

## 测试用例

### T1 默认设置启动
| 项 | 步骤 | 预期 | 结果 |
|---|---|---|---|
| 环境 | 删除设置键后启动 | — | — |
| 进程 | tasklist | 单一 whale-sitter.exe | ✅ |
| 端口 | netstat :3080 | LISTENING | ✅ |
| HTTP | curl 127.0.0.1:3080 | 200 | ✅ |
| 语言 | 窗口标题 | 中文（系统为中文） | ✅（修复前误为英文，见缺陷 D1） |

### T2 语言切换
| 项 | 步骤 | 预期 | 结果 |
|---|---|---|---|
| 中文 | 设置 Lang=1 后启动 | 标题含"监控台" | ✅ |
| 英文 | 设置 Lang=2 后启动 | 标题含"Monitor" | ✅ |
| 服务 | 两种语言下 | 3080 监听 + HTTP 200 | ✅ |

### T3 端口设置
| 项 | 步骤 | 预期 | 结果 |
|---|---|---|---|
| 自定义端口 | 设置 Port=3081 后启动 | 3081 LISTENING | ✅ |
| HTTP | curl 127.0.0.1:3081 | 200（证明 `dsh web --port 3081` 生效） | ✅ |
| 旧端口 | netstat :3080 | 空闲 | ✅ |

### T4 主题设置
| 项 | 步骤 | 预期 | 结果 |
|---|---|---|---|
| 深色 | 设置 Theme=2 后启动 | 正常启动无崩溃，服务可用 | ✅ |
| 浅色 | 设置 Theme=1 后启动 | 正常启动无崩溃 | ✅（静态检查 + 冒烟） |
| 跟随系统 | Theme=0 | 读系统主题（已有 v1.0.0 验证） | ✅ |

### T5 恢复默认
| 项 | 步骤 | 预期 | 结果 |
|---|---|---|---|
| 默认 | 删除设置键后启动 | 中文 + 3080 + HTTP 200 + 单进程 | ✅ |

### T6 构建复现（CI）
| 项 | 步骤 | 预期 | 结果 |
|---|---|---|---|
| 本地构建 | build.bat | "built OK" | ✅ |
| CI 构建 | GitHub Actions windows-latest（push/tag 触发） | 构建通过 | 见 T7 端到端 |

### T7 发布闭环（端到端）
| 项 | 步骤 | 预期 | 结果 |
|---|---|---|---|
| 打 tag | git tag v2.1.0 && push | 触发 release.yml | 见执行结果 |
| Actions | windows-latest 跑 build.bat | 构建 + 校验 exe | 见执行结果 |
| Release | gh release create | 生成 Release 且附带 exe/zip 资产 | 见执行结果 |

## 发现的缺陷与修复

| 编号 | 缺陷 | 修复 |
|---|---|---|
| D1 | 语言自动检测逻辑写反：`L.Set(是否中文)`，中文系统却切到英文 | 改为 `!= "zh"`（`L.Set(true)` 意为英文）；抽公共方法 `ApplyLanguage()`，构造函数与设置页复用 |
| D2 | （v1.0.0 历史）`api.github.com` 走代理 TLS 超时 | 登录时 `$env:NO_PROXY="api.github.com"`（见 IMPLEMENTATION.md） |
| D3 | v2.2.0「一键安装/修复」在无便携 Node 环境失败：`ResolveSystemNpmCliAsync`/`ResolveNpmPrefix` 直接 `Process.Start("npm")`（UseShellExecute=false），而 Windows 的 npm 是 .cmd 无法直接启动 → Win32Exception"系统找不到指定的文件"（换电脑实测暴露，日志 E:\Zcode-pj\dsh-web.log） | v2.2.1：`ResolveNpmPrefix` 改经 `cmd.exe /c npm prefix -g`；`ResolveSystemNpmCliAsync` 优先用 `cmd /c where node` 由 node.exe 定位 `node_modules\npm\bin\npm-cli.js`，回退 `cmd /c npm root -g`。已在本机跑通同链路（node npm-cli.js install -g 530 包成功） |

## 未覆盖项（GUI 手动操作）

以下项无法纯脚本验证，需人工点按确认：
- 大按钮点击启停、托盘菜单、关闭→最小化气泡
- 一键诊断弹窗与复制按钮
- 一键安装流程（本机环境齐全，未实际触发下载安装；代码路径与下载源已静态审查）
- 设置面板弹窗交互与端口变更时"服务重启"提示

建议：发布后邀请 1-2 位无环境用户实测"一键安装"，重点验证便携 Node 下载（npmmirror/nodejs.org）与 npm 安装链路。
