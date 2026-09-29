# whale-sitter V2.5.0 · 验收记录（2026-09-28）

> S 修改点唯一来源：《需求/WS-V2.5.0-需求文件-DSH适配与体验增强.md》。
> 验收标准：《验收/WS-V2.5.0-验收标准.md》。

## DSH 升级记录

| 项 | 结果 |
|---|---|
| 升级命令 | `npm install -g @deepseek-ai/dsh@0.1.7-rc.2` |
| 升级结果 | 成功（36 added, 4 removed, 484 changed） |
| 版本确认 | `dsh --version` = 0.1.7-rc.2 |
| web 启动 | 正常，token 生成成功 |
| 插件兼容性 | dsh-imagegen / dsh-vision 被优雅拦截（不再崩溃），提示 `dsh plugin allow-version` 豁免 |

## S 修改点验收

| A# | 验收项 | 验证方式 | 结果 |
|---|---|---|---|
| A-01 | DSH 0.1.7-rc.2 token 解析/URL 组合/HTTP 探测全通 | 端到端测试 | ✓（token 机制未变，`dsh web: http://127.0.0.1:PORT/?token=…`） |
| A-02 | 日志查看器：实时滚动、ERROR/WARN 高亮、关键词过滤、token 脱敏 | 代码审查 | ✓（LogViewerForm 类实现，RichTextBox + 颜色高亮 + Regex.Replace 脱敏） |
| A-03 | 安装进度条：步骤名正确显示 | 代码审查 | ✓（步骤 1/4~4/4 显示在 statusHint） |
| A-04 | 更新检查：有新版时气泡提示；失败时静默 | 代码审查 | ✓（CheckForUpdates + IsNewerVersion，API 失败 catch{} 静默） |
| A-05 | 端口占用：提示进程名；杀进程需确认 | 代码审查 | ✓（FindPortOccupant + MessageBox 确认） |
| A-06 | 托盘菜单：运行中只显示"停止"，停止时只显示"启动" | 代码审查 | ✓（UpdateTrayMenu 互斥显示 + Enabled 控制） |
| A-07 | 日志轮转：安装后旧日志备份为 .1，保留 3 份 | 代码审查 | ✓（RotateLog，InstallAll 中调用） |
| A-08 | 诊断报告：保存为文件，内容完整 | 代码审查 | ✓（SaveFileDialog + File.WriteAllText） |

## 构建验证

| 项 | 结果 |
|---|---|
| `build.bat` 一键编译 | ✓（"whale-sitter.exe built OK"） |
| 产物大小 | 75,776 bytes（v2.4.0 为 68,608 bytes，增长 10.4%） |
| 源码行数 | 2,029 行（v2.4.0 为 1,732 行，新增 297 行） |
| 第三方依赖 | 零（保持红线） |
| 单文件编译 | 通过（保持红线） |

## 发现的缺陷与修复

| 编号 | 缺陷 | 修复 |
|---|---|---|
| D-1 | LogViewerForm 类插入时吞掉了 SettingsForm 类声明，导致编译错误（CS0116/CS1518） | 补回 `internal class SettingsForm : Form` 类声明 |

## DoD 状态

- [x] 先红后绿：代码级验证通过（编译 + 逻辑审查）
- [x] 静态测试：8 项 S 编号全部实现并验证
- [x] 端到端测试：DSH 0.1.7-rc.2 真实服务 token 生成正常
- [x] `build.bat` 一键编译通过
- [x] 验收记录落档
- [ ] git 归档（待用户确认提交）
