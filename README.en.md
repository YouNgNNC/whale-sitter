# whale-sitter 🐳

**DeepSeek Harness (dsh) status monitor for Windows** — a small tray tool so you never have to type commands again:

> Open the panel → watch the status light → start/stop with one click. Close the window? It quietly sits in the system tray.

## Features

- 🧰 **One-click install / fix**: detects Node.js / dsh automatically; if missing, one click downloads and installs a portable Node.js (no admin rights) and dsh. When the environment is complete it doubles as "repair & reinstall" (always available via the panel button and tray menu)
- 🩺 **One-click diagnostics**: generates an environment report (versions/paths/port/HTTP/log tail) and copies it to the clipboard — paste it into a GitHub issue for help
- 🧠 **Smart status**: distinguishes "Node.js not found" / "dsh not installed" / "port in use" / "running / stopped" and switches the action button accordingly
- 🟢 **Status light**: polls the dsh web service every 2 s; green = running (shows PID), red = stopped; breathing animation while running
- 🎛️ **One-click switch**: a big button to start/stop (also available in the tray menu)
- 🐳 **System tray**: closing the window minimizes to tray instead of exiting; double-click the whale icon to restore
- 🚀 **Auto-start**: starts the service automatically when the panel opens, if it isn't running
- 🌗 **Follows system light/dark theme** (incl. dark title bar), updates live when the system theme changes
- ⏰ **Auto-start on boot toggle**: on/off from the UI (HKCU registry, no admin needed)
- ⚙️ **Settings**: language (System/中文/English), theme (System/Light/Dark), server port (default 3080)
- 📂 **View log**: opens the dsh service log in Notepad
- 🪟 **Zero dependencies**: compiles with the .NET Framework csc that ships with Windows

## System requirements

- Windows 10/11 (x64)
- Node.js (>= 18) — auto-installed if missing (portable, no admin rights)

## Usage

### Option A: run the binary (recommended)

Download `whale-sitter.exe` from the [Releases page](https://github.com/YouNgNNC/whale-sitter/releases) and double-click it.

### Option B: build from source

```bat
build.bat
```

Produces `whale-sitter.exe` next to the sources. Only needs the csc.exe that ships with .NET Framework on Windows — no Visual Studio required.

> **SmartScreen note**: the exe is not code-signed, so Windows may show a blue "Windows protected your PC" prompt on first run. Click **More info → Run anyway**. This is expected for unsigned open-source tools.

## Behavior

| Action | Behavior |
|---|---|
| Click window ✕ | Minimizes to system tray (with balloon tip), does not exit |
| Double-click tray whale icon | Restores the panel |
| Tray menu "Exit" | Closes the monitor only; **does not stop the dsh service** |
| Change port in Settings | Service restarts on the new port |
| Change language in Settings | UI switches immediately |

## How it works

- Locates the dsh install via `npm prefix -g` (or the bundled portable Node)
- Service process = `node <npm-global>\node_modules\@deepseek-ai\dsh\lib\bin.js web --port <port>`
- Status detection: `netstat` checks whether the configured port is LISTENING
- Stop: `taskkill /F /T` on the listening PID
- Settings are stored in `HKCU\Software\whale-sitter`
- Service log: `%AppData%\npm\dsh-web.log`

## Files

| File | Description |
|---|---|
| `whale-sitter.cs` | C# WinForms source (single file) |
| `whale-sitter.ico` | Tray/window icon (official DeepSeek whale) |
| `build.bat` | One-command build script |
| `docs/` | ROADMAP (planning & decisions) and IMPLEMENTATION (build/release notes) |
| `whale-sitter.exe` | Build artifact (published on Releases) |

## Changelog

- **v2.2.0**: "Install / Fix" always visible (panel button + tray menu; acts as repair/reinstall when the environment is complete); stops the service before installing to avoid file locks; can repair using the system npm
- **v2.1.0**: Settings panel (language/theme/port), bilingual UI, GitHub Actions auto-build Release on tag, CI build check, EN README, issue templates
- **v1.0.0**: Initial release — one-click install, one-click diagnostics, smart status, monitoring & switch, tray, auto-start, system theme, auto-start-on-boot

## License

[MIT](LICENSE)
