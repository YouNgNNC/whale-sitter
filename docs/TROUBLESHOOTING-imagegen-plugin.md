# dsh 状态无法启用 · 排查记录

> 问题：whale-sitter 显示「已停止」，点启动后状态无法变为运行中，dsh web 界面打不开。
> 日期：2026-08-15 ｜ 环境：Windows 10 19045 x64 ｜ dsh 0.1.0-rc.6 ｜ whale-sitter v2.2.2

## 1. 现象

- whale-sitter 进程正常，但 3080 端口无监听，HTTP 访问 000
- 状态灯始终「已停止」，点击大按钮启动后数秒内又回到「已停止」
- `%AppData%\npm\dsh-web.log` 里出现 dsh 启动崩溃堆栈

## 2. 核心原因

**dsh 的 web profile（`%USERPROFILE%\.dsh\profiles\web`）中安装了一个与当前 dsh 版本不兼容的本地插件 `dsh-imagegen` v0.1.1**（来源 `file:E:/Deepseek-Harness/01.todo/dsh-imagegen-plugin`），该插件通过两处配置被加载：

```jsonc
// package.json
"dependencies": {
  "dsh-imagegen": "file:E:/Deepseek-Harness/01.todo/dsh-imagegen-plugin"
}
```

```yaml
# cordis.patch.yml
- insert:
    - id: dsh-imagegen
      name: dsh-imagegen
```

插件初始化时访问 `tools` 属性但尚未注入，抛错 `cannot get property "tools" without inject` → dsh 插件树加载失败 → 进程直接退出 → 端口永远起不来。

> 结论：这是 dsh 侧插件兼容性问题，不是 whale-sitter 的缺陷。

## 3. 分析过程

### 3.1 症状确认（排除 whale-sitter 自身问题）

```
tasklist | grep whale-sitter   → 进程在跑
netstat | grep :3080           → 无监听
curl http://127.0.0.1:3080/    → 000
```

### 3.2 看日志定位崩溃点

日志末尾出现 dsh 启动崩溃：

```
Error: dsh: plugin tree failed to load: failed to apply loader entry include (cordis:include):
failed to apply loader entry dsh-imagegen (dsh-imagegen): cannot get property "tools" without inject
    at Object.apply [as callback] (file:///C:/Users/YouNg_LEGION/.dsh/profiles/web/node_modules/dsh-imagegen/lib/index.js:121:40)
...
Node.js v24.15.0
```

关键信息：崩溃发生在 **`~/.dsh/profiles/web/` 下的 `dsh-imagegen` 插件**。

### 3.3 手动复现（100% 复现，确认非偶发）

```bash
timeout 20 node "<npm全局>/node_modules/@deepseek-ai/dsh/lib/bin.js" web --port 3080
```

输出与日志完全一致的崩溃堆栈 → 每次启动必崩。

### 3.4 检查 profile 配置定位挂载点

```bash
cat ~/.dsh/profiles/web/package.json     # 发现 file: 依赖 dsh-imagegen
cat ~/.dsh/profiles/web/cordis.patch.yml # 发现 insert dsh-imagegen
```

确认插件由「package.json 依赖 + cordis.patch.yml insert」两处加载。

## 4. 解决办法

**思路：备份 → 停用插件 → 验证启动 → 恢复服务。**

```bash
PROFILE=~/.dsh/profiles/web

# 1) 备份（可恢复）
cp $PROFILE/package.json      $PROFILE/package.json.bak
cp $PROFILE/cordis.patch.yml  $PROFILE/cordis.patch.yml.bak

# 2) 从依赖中移除 dsh-imagegen（注意：不要用 PowerShell Set-Content -Encoding UTF8 写，
#    会带 BOM 导致 dsh JSON.parse 报 "Unexpected token"，须用 UTF8Encoding($false)）
#    （把 dependencies 改为 {}）

# 3) 清空 patch 中的 insert
printf '# User-owned web profile patch.\n[]\n' > $PROFILE/cordis.patch.yml

# 4) 插件目录停用（保留备份，改名即可）
mv $PROFILE/node_modules/dsh-imagegen $PROFILE/node_modules/dsh-imagegen.disabled
```

### 验证

```bash
# 手动启动：应输出 dsh web: http://127.0.0.1:3080 且不再崩溃
timeout 18 node "<npm全局>/node_modules/@deepseek-ai/dsh/lib/bin.js" web --port 3080
```

重启 whale-sitter 后：3080 监听 ✅ HTTP 200 ✅ 状态「运行中」✅

## 5. 恢复方法

若修好了 `dsh-imagegen` 插件（或确认兼容）想恢复：

```bash
PROFILE=~/.dsh/profiles/web
cp $PROFILE/package.json.bak      $PROFILE/package.json
cp $PROFILE/cordis.patch.yml.bak  $PROFILE/cordis.patch.yml
mv $PROFILE/node_modules/dsh-imagegen.disabled $PROFILE/node_modules/dsh-imagegen
```

## 6. 经验与预防

1. **遇到"状态无法启用"，先看日志**（whale-sitter 的「查看日志」按钮）——崩溃原因会直接写在里面。
2. **dsh profile 里的第三方/本地插件是启动崩溃的高发点**；插件的挂载点有两处：`package.json` 的 `dependencies` 和 `cordis.patch.yml` 的 `insert`，排查时都要看。
3. **编辑 profile 的 JSON 文件时避免写入 BOM**（PowerShell 5.1 `Set-Content -Encoding UTF8` 会带 BOM），用无 BOM UTF-8 写入。
4. 此问题已同步进 whale-sitter README「常见问题」，方便其他用户自查。
