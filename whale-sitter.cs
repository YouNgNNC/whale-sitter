using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace WhaleSitter
{
    internal static class Program
    {
        public const string Version = "1.0.0";

        [STAThread]
        private static void Main()
        {
            bool createdNew;
            using (Mutex m = new Mutex(true, "WhaleSitter.SingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show("监控台已在运行，请查看系统托盘的鲸鱼图标。", "whale-sitter",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
        }
    }

    internal struct Palette
    {
        public Color WindowBg;
        public Color CardBg;
        public Color Border;
        public Color Text;
        public Color TextDim;
        public Color Accent;
        public Color AccentSoft;
        public Color Success;
        public Color SuccessDim;
        public Color Danger;
        public Color Warn;
        public Color BtnBg;
        public Color BtnText;
        public bool Dark;
    }

    internal class RoundedButton : Button
    {
        private readonly int radius = 10;
        private Color fill = Color.FromArgb(77, 107, 254);
        private Color fillText = Color.White;

        public void SetColors(Color f, Color t)
        {
            fill = f;
            fillText = t;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = RoundedPath(ClientRectangle, radius))
            using (SolidBrush b = new SolidBrush(fill))
            {
                e.Graphics.FillPath(b, path);
            }
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, fillText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        private static GraphicsPath RoundedPath(Rectangle r, int rad)
        {
            GraphicsPath p = new GraphicsPath();
            int d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    internal class CardPanel : Panel
    {
        private Palette pal;

        public void SetPalette(Palette p)
        {
            pal = p;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = new GraphicsPath())
            {
                int d = 16;
                Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
                path.AddArc(r.X, r.Y, d, d, 180, 90);
                path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
                path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
                path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
                using (SolidBrush b = new SolidBrush(pal.CardBg))
                {
                    e.Graphics.FillPath(b, path);
                }
                using (Pen pen = new Pen(pal.Border))
                {
                    e.Graphics.DrawPath(pen, path);
                }
            }
        }
    }

    internal class MainForm : Form
    {
        private const int Port = 3080;
        private static readonly string Url = "http://127.0.0.1:" + Port + "/";
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "whale-sitter";
        private const int WmSettingChange = 0x001A;
        private const int DwmwaUseImmersiveDarkMode = 20;

        private static readonly string LocalDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "whale-sitter");
        private static string PortableNodeDir = FindPortableNodeDir();

        private static string NpmDir
        {
            get { return PortableNodeDir != null ? PortableNodeDir : ResolveNpmPrefix(); }
        }

        private static string DshEntry
        {
            get { return Path.Combine(NpmDir, "node_modules", "@deepseek-ai", "dsh", "lib", "bin.js"); }
        }

        private static string NodeExe
        {
            get { return PortableNodeDir != null ? Path.Combine(PortableNodeDir, "node.exe") : "node.exe"; }
        }

        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "dsh-web.log");
        private static readonly string IcoPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "dsh-web.ico");
        private readonly object logLock = new object();

        private readonly PictureBox whale = new PictureBox();
        private readonly Label title = new Label();
        private readonly Label subtitle = new Label();
        private readonly CardPanel card = new CardPanel();
        private readonly Label dot = new Label();
        private readonly Label status = new Label();
        private readonly Label statusHint = new Label();
        private readonly RoundedButton toggle = new RoundedButton();
        private readonly Button autoStartBtn = new Button();
        private readonly Button openBtn = new Button();
        private readonly Button logBtn = new Button();
        private readonly Button diagBtn = new Button();
        private readonly Label hint = new Label();
        private readonly NotifyIcon tray = new NotifyIcon();
        private readonly System.Windows.Forms.Timer pollTimer = new System.Windows.Forms.Timer();
        private readonly System.Windows.Forms.Timer pulseTimer = new System.Windows.Forms.Timer();
        private Process serverProc;
        private bool realExit;
        private bool starting;
        private bool running;
        private bool pulseOn;
        private bool installInProgress;
        private Palette pal;

        public MainForm()
        {
            InitUi();
            InitTray();
        }

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private static string FindPortableNodeDir()
        {
            try
            {
                string nodeRoot = Path.Combine(LocalDataDir, "node");
                if (Directory.Exists(nodeRoot))
                {
                    foreach (string d in Directory.GetDirectories(nodeRoot))
                    {
                        if (File.Exists(Path.Combine(d, "node.exe"))) return d;
                    }
                }
            }
            catch { }
            return null;
        }

        private static string ResolveNpmPrefix()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("npm", "prefix -g");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                Process p = Process.Start(psi);
                string dir = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit(3000);
                if (dir.Length > 0 && Directory.Exists(dir)) return dir;
            }
            catch { }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm");
        }

        private static bool NodeAvailable()
        {
            return PortableNodeDir != null || NodeVersionText() != "未检测到";
        }

        private static string NodeVersionText()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(NodeExe, "--version");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                Process p = Process.Start(psi);
                string v = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit(3000);
                return v.Length > 0 ? v : "未检测到";
            }
            catch { return "未检测到"; }
        }

        private static bool DshInstalled()
        {
            return File.Exists(DshEntry);
        }

        private static bool SystemUsesLightTheme()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (k != null)
                    {
                        object v = k.GetValue("AppsUseLightTheme");
                        if (v != null) return Convert.ToInt32(v) == 1;
                    }
                }
            }
            catch { }
            return true;
        }

        private static bool AutoStartEnabled()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    return k != null && k.GetValue(RunValueName) != null;
                }
            }
            catch { return false; }
        }

        private void SetAutoStart(bool enabled)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (k == null) return;
                    if (enabled) k.SetValue(RunValueName, "\"" + Application.ExecutablePath + "\"");
                    else k.DeleteValue(RunValueName, false);
                }
            }
            catch { }
            UpdateAutoStartButton();
        }

        private static Palette BuildPalette(bool dark)
        {
            Palette p = new Palette();
            p.Dark = dark;
            if (dark)
            {
                p.WindowBg = Color.FromArgb(30, 31, 36);
                p.CardBg = Color.FromArgb(38, 39, 46);
                p.Border = Color.FromArgb(58, 60, 68);
                p.Text = Color.FromArgb(236, 236, 239);
                p.TextDim = Color.FromArgb(155, 160, 171);
                p.Accent = Color.FromArgb(107, 131, 255);
                p.AccentSoft = Color.FromArgb(60, 68, 120);
                p.Success = Color.FromArgb(88, 200, 130);
                p.SuccessDim = Color.FromArgb(52, 120, 80);
                p.Danger = Color.FromArgb(232, 110, 110);
                p.Warn = Color.FromArgb(240, 170, 70);
                p.BtnBg = Color.FromArgb(64, 65, 74);
                p.BtnText = Color.FromArgb(236, 236, 239);
            }
            else
            {
                p.WindowBg = Color.FromArgb(247, 248, 251);
                p.CardBg = Color.White;
                p.Border = Color.FromArgb(225, 227, 233);
                p.Text = Color.FromArgb(31, 32, 36);
                p.TextDim = Color.FromArgb(110, 116, 128);
                p.Accent = Color.FromArgb(77, 107, 254);
                p.AccentSoft = Color.FromArgb(224, 230, 255);
                p.Success = Color.FromArgb(46, 160, 90);
                p.SuccessDim = Color.FromArgb(226, 246, 233);
                p.Danger = Color.FromArgb(210, 76, 76);
                p.Warn = Color.FromArgb(214, 145, 30);
                p.BtnBg = Color.FromArgb(238, 240, 245);
                p.BtnText = Color.FromArgb(31, 32, 36);
            }
            return p;
        }

        private void InitUi()
        {
            Text = "whale-sitter · DeepSeek Harness 监控台";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            ClientSize = new Size(404, 268);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 10F);
            DoubleBuffered = true;

            whale.SizeMode = PictureBoxSizeMode.Zoom;
            whale.Size = new Size(36, 36);
            whale.Location = new Point(18, 16);
            try
            {
                using (Icon ic = new Icon(IcoPath))
                {
                    whale.Image = ic.ToBitmap();
                    Icon = (Icon)ic.Clone();
                }
            }
            catch { Icon = SystemIcons.Application; }

            title.Text = "whale-sitter";
            title.Font = new Font(Font.FontFamily, 15F, FontStyle.Bold);
            title.Location = new Point(64, 14);
            title.AutoSize = true;

            subtitle.Text = "DeepSeek Harness 监控台 · v" + Program.Version;
            subtitle.Font = new Font(Font.FontFamily, 9F);
            subtitle.Location = new Point(66, 40);
            subtitle.AutoSize = true;

            card.Location = new Point(16, 64);
            card.Size = new Size(372, 62);

            dot.Text = "●";
            dot.Font = new Font(Font.FontFamily, 14F, FontStyle.Bold);
            dot.Location = new Point(24, 19);
            dot.AutoSize = true;

            status.Text = "检测中…";
            status.Font = new Font(Font.FontFamily, 11F, FontStyle.Bold);
            status.Location = new Point(52, 16);
            status.AutoSize = true;

            statusHint.Text = "Web UI  http://127.0.0.1:" + Port;
            statusHint.Font = new Font(Font.FontFamily, 8.5F);
            statusHint.Location = new Point(52, 38);
            statusHint.AutoSize = true;

            card.Controls.Add(dot);
            card.Controls.Add(status);
            card.Controls.Add(statusHint);

            toggle.Location = new Point(16, 136);
            toggle.Size = new Size(372, 54);
            toggle.Font = new Font(Font.FontFamily, 15F, FontStyle.Bold);
            toggle.Cursor = Cursors.Hand;
            toggle.Click += delegate { ToggleMain(); };

            autoStartBtn.Size = new Size(104, 30);
            autoStartBtn.Location = new Point(16, 202);
            autoStartBtn.FlatStyle = FlatStyle.Flat;
            autoStartBtn.FlatAppearance.BorderSize = 0;
            autoStartBtn.Font = new Font(Font.FontFamily, 9F);
            autoStartBtn.Cursor = Cursors.Hand;
            autoStartBtn.Click += delegate { SetAutoStart(!AutoStartEnabled()); };

            openBtn.Size = new Size(80, 30);
            openBtn.Location = new Point(128, 202);
            openBtn.FlatStyle = FlatStyle.Flat;
            openBtn.FlatAppearance.BorderSize = 0;
            openBtn.Font = new Font(Font.FontFamily, 9F);
            openBtn.Cursor = Cursors.Hand;
            openBtn.Text = "打开界面";
            openBtn.Click += delegate { try { Process.Start(Url); } catch { } };

            logBtn.Size = new Size(80, 30);
            logBtn.Location = new Point(216, 202);
            logBtn.FlatStyle = FlatStyle.Flat;
            logBtn.FlatAppearance.BorderSize = 0;
            logBtn.Font = new Font(Font.FontFamily, 9F);
            logBtn.Cursor = Cursors.Hand;
            logBtn.Text = "查看日志";
            logBtn.Click += delegate { OpenLog(); };

            diagBtn.Size = new Size(80, 30);
            diagBtn.Location = new Point(304, 202);
            diagBtn.FlatStyle = FlatStyle.Flat;
            diagBtn.FlatAppearance.BorderSize = 0;
            diagBtn.Font = new Font(Font.FontFamily, 9F);
            diagBtn.Cursor = Cursors.Hand;
            diagBtn.Text = "一键诊断";
            diagBtn.Click += delegate { OpenDiagnostics(); };

            hint.Text = "✕ 关闭 = 最小化到托盘";
            hint.Font = new Font(Font.FontFamily, 8F);
            hint.Location = new Point(16, 240);
            hint.AutoSize = true;

            Controls.Add(whale);
            Controls.Add(title);
            Controls.Add(subtitle);
            Controls.Add(card);
            Controls.Add(toggle);
            Controls.Add(autoStartBtn);
            Controls.Add(openBtn);
            Controls.Add(logBtn);
            Controls.Add(diagBtn);
            Controls.Add(hint);

            ApplyTheme(SystemUsesLightTheme());
        }

        private void InitTray()
        {
            tray.Text = "whale-sitter - 检测中…";
            try { tray.Icon = new Icon(IcoPath); }
            catch { tray.Icon = SystemIcons.Application; }
            tray.Visible = true;
            tray.DoubleClick += delegate { ShowWindow(); };

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("打开监控台", null, delegate { ShowWindow(); });
            menu.Items.Add("打开界面", null, delegate { try { Process.Start(Url); } catch { } });
            menu.Items.Add("查看日志", null, delegate { OpenLog(); });
            menu.Items.Add("一键诊断", null, delegate { OpenDiagnostics(); });
            menu.Items.Add("启动服务", null, delegate { StartServer(); });
            menu.Items.Add("停止服务", null, delegate { StopServer(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, delegate
            {
                realExit = true;
                tray.Visible = false;
                Application.Exit();
            });
            tray.ContextMenuStrip = menu;
        }

        private void OpenLog()
        {
            try
            {
                if (File.Exists(LogPath)) Process.Start("notepad.exe", "\"" + LogPath + "\"");
                else MessageBox.Show("日志文件还不存在：\n" + LogPath, "whale-sitter",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch { }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyTitleBarTheme(pal.Dark);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WmSettingChange)
            {
                bool dark = !SystemUsesLightTheme();
                if (dark != pal.Dark) ApplyTheme(dark);
            }
        }

        private void ApplyTheme(bool dark)
        {
            pal = BuildPalette(dark);
            BackColor = pal.WindowBg;
            ForeColor = pal.Text;

            title.ForeColor = pal.Text;
            subtitle.ForeColor = pal.TextDim;
            statusHint.ForeColor = pal.TextDim;
            hint.ForeColor = pal.TextDim;

            card.SetPalette(pal);

            UpdateStatusUi();
            UpdateAutoStartButton();

            foreach (Control c in new Control[] { autoStartBtn, openBtn, logBtn, diagBtn })
            {
                c.BackColor = pal.BtnBg;
                c.ForeColor = pal.BtnText;
            }

            if (IsHandleCreated) ApplyTitleBarTheme(pal.Dark);
            Invalidate();
        }

        private void ApplyTitleBarTheme(bool dark)
        {
            try
            {
                int v = dark ? 1 : 0;
                DwmSetWindowAttribute(Handle, DwmwaUseImmersiveDarkMode, ref v, 4);
            }
            catch { }
        }

        private void UpdateAutoStartButton()
        {
            bool on = AutoStartEnabled();
            autoStartBtn.Text = "开机自启：" + (on ? "开" : "关");
            autoStartBtn.BackColor = on ? pal.Accent : pal.BtnBg;
            autoStartBtn.ForeColor = on ? Color.White : pal.BtnText;
        }

        private void SetInstallUi(string text)
        {
            statusHint.Text = text;
            statusHint.ForeColor = pal.Warn;
        }

        private void UpdateStatusUi()
        {
            if (installInProgress)
            {
                dot.ForeColor = pal.Warn;
                status.ForeColor = pal.Warn;
                status.Text = "安装中…";
                toggle.SetColors(pal.Warn, Color.White);
                toggle.Text = "安装中，请稍候…";
                return;
            }

            if (starting)
            {
                status.Text = "启动中…";
                dot.ForeColor = pal.Warn;
                status.ForeColor = pal.Warn;
                toggle.SetColors(pal.Warn, Color.White);
                toggle.Text = "启动中…";
                return;
            }

            if (!NodeAvailable())
            {
                dot.ForeColor = pal.Danger;
                status.ForeColor = pal.Danger;
                status.Text = "缺少 Node.js";
                statusHint.Text = "点下方按钮自动安装运行环境";
                toggle.SetColors(pal.Accent, Color.White);
                toggle.Text = "一键安装 Node.js + dsh";
                return;
            }

            if (!DshInstalled())
            {
                dot.ForeColor = pal.Warn;
                status.ForeColor = pal.Warn;
                status.Text = "未安装 dsh";
                statusHint.Text = "点下方按钮一键安装 DeepSeek Harness";
                toggle.SetColors(pal.Accent, Color.White);
                toggle.Text = "一键安装 DeepSeek Harness";
                return;
            }

            statusHint.Text = "Web UI  http://127.0.0.1:" + Port;
            if (running)
            {
                dot.ForeColor = pulseOn ? pal.Success : pal.SuccessDim;
                status.ForeColor = pal.Success;
                status.Text = "运行中 · PID " + runningPidText();
                toggle.SetColors(pal.Success, Color.White);
                toggle.Text = "点击停止服务";
            }
            else
            {
                dot.ForeColor = pal.Danger;
                status.ForeColor = pal.TextDim;
                status.Text = "已停止";
                toggle.SetColors(pal.BtnBg, pal.BtnText);
                toggle.Text = "点击启动服务";
            }
        }

        private string runningPidText()
        {
            int pid = FindPidByPort(Port);
            return pid > 0 ? pid.ToString() : "?";
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            pollTimer.Interval = 2000;
            pollTimer.Tick += delegate { UpdateStatus(); };
            pollTimer.Start();

            pulseTimer.Interval = 600;
            pulseTimer.Tick += delegate
            {
                if (running)
                {
                    pulseOn = !pulseOn;
                    dot.ForeColor = pulseOn ? pal.Success : pal.SuccessDim;
                }
                else
                {
                    pulseOn = false;
                }
            };
            pulseTimer.Start();

            UpdateStatus();
            if (NodeAvailable() && DshInstalled() && !running)
                StartServer();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!realExit)
            {
                e.Cancel = true;
                Hide();
                tray.ShowBalloonTip(1500, "whale-sitter",
                    "已最小化到系统托盘，双击鲸鱼图标即可恢复面板。", ToolTipIcon.Info);
                return;
            }
            pollTimer.Stop();
            pulseTimer.Stop();
            tray.Visible = false;
            base.OnFormClosing(e);
        }

        private void ShowWindow()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        private void ToggleMain()
        {
            if (installInProgress) return;
            if (!NodeAvailable() || !DshInstalled())
            {
                InstallAll();
                return;
            }
            if (running) StopServer();
            else StartServer();
        }

        private async void InstallAll()
        {
            if (installInProgress) return;
            installInProgress = true;
            UpdateStatusUi();
            try
            {
                string nodeDir = PortableNodeDir;
                if (nodeDir == null)
                {
                    SetInstallUi("正在下载 Node.js…");
                    AppendLog("开始安装：下载 Node.js");
                    nodeDir = await InstallNodeAsync();
                }

                SetInstallUi("正在安装 DeepSeek Harness（可能需要几分钟）…");
                AppendLog("开始安装：npm install -g @deepseek-ai/dsh");
                await InstallDshAsync(nodeDir);

                PortableNodeDir = FindPortableNodeDir();
                UpdateStatus();
                SetInstallUi("安装完成，正在启动服务…");
                if (!DshInstalled())
                    throw new Exception("dsh 安装后仍未检测到，请查看日志或使用一键诊断。");
                if (!running) StartServer();
                status.Text = "安装完成";
            }
            catch (Exception ex)
            {
                status.Text = "安装失败";
                statusHint.Text = ex.Message;
                AppendLog("安装失败: " + ex);
            }
            finally
            {
                installInProgress = false;
                UpdateStatusUi();
            }
        }

        private Task<string> InstallNodeAsync()
        {
            return Task.Run(() =>
            {
                string zipUrl = null;
                string fileName = null;

                string[] mirrorRoots = new string[]
                {
                    "https://registry.npmmirror.com/-/binary/node/",
                    "https://nodejs.org/dist/"
                };

                foreach (string root in mirrorRoots)
                {
                    try
                    {
                        using (WebClient wc = new WebClient())
                        {
                            wc.Headers.Add("User-Agent", "whale-sitter");
                            string shas = wc.DownloadString(root + "latest/SHASUMS256.txt");
                            Match m = Regex.Match(shas, @"node-v([0-9]+\.[0-9]+\.[0-9]+)-win-x64\.zip");
                            if (m.Success)
                            {
                                string ver = m.Groups[1].Value;
                                fileName = "node-v" + ver + "-win-x64.zip";
                                zipUrl = root + "v" + ver + "/" + fileName;
                                break;
                            }
                        }
                    }
                    catch { }
                }

                if (zipUrl == null)
                    throw new Exception("无法获取 Node.js 下载地址（网络问题？），请检查网络后重试。");

                string tmpZip = Path.Combine(Path.GetTempPath(), fileName);
                string extractRoot = Path.Combine(LocalDataDir, "node");
                Directory.CreateDirectory(extractRoot);

                using (WebClient wc = new WebClient())
                {
                    wc.Headers.Add("User-Agent", "whale-sitter");
                    wc.DownloadFile(zipUrl, tmpZip);
                }

                ZipFile.ExtractToDirectory(tmpZip, extractRoot);

                string nodeDir = null;
                foreach (string d in Directory.GetDirectories(extractRoot))
                {
                    if (File.Exists(Path.Combine(d, "node.exe"))) nodeDir = d;
                }
                try { File.Delete(tmpZip); } catch { }

                if (nodeDir == null)
                    throw new Exception("Node.js 解压后未找到 node.exe，请重试。");
                return nodeDir;
            });
        }

        private Task InstallDshAsync(string nodeDir)
        {
            return Task.Run(() =>
            {
                string npmCli = Path.Combine(nodeDir, "node_modules", "npm", "bin", "npm-cli.js");
                if (!File.Exists(npmCli))
                    throw new Exception("未找到 npm（" + npmCli + "），安装不完整。");

                ProcessStartInfo psi = new ProcessStartInfo(
                    Path.Combine(nodeDir, "node.exe"),
                    "\"" + npmCli + "\" install -g @deepseek-ai/dsh");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.WorkingDirectory = nodeDir;

                Process p = Process.Start(psi);
                string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                p.WaitForExit();

                lock (logLock)
                {
                    try { File.AppendAllText(LogPath, output + Environment.NewLine); } catch { }
                }
                if (p.ExitCode != 0)
                    throw new Exception("npm 安装失败（exit " + p.ExitCode + "），详见日志。");
            });
        }

        private void StartServer()
        {
            if (running || starting || installInProgress) return;
            if (!NodeAvailable() || !DshInstalled()) return;
            starting = true;
            status.Text = "启动中…";
            toggle.Text = "启动中…";
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(NodeExe, "\"" + DshEntry + "\" web");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.WorkingDirectory = NpmDir;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                serverProc = Process.Start(psi);
                serverProc.OutputDataReceived += delegate(object s, DataReceivedEventArgs a)
                {
                    if (a.Data != null) AppendLog(a.Data);
                };
                serverProc.ErrorDataReceived += delegate(object s, DataReceivedEventArgs a)
                {
                    if (a.Data != null) AppendLog(a.Data);
                };
                serverProc.BeginOutputReadLine();
                serverProc.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                starting = false;
                status.Text = "启动失败：" + ex.Message;
                AppendLog("启动失败: " + ex.Message);
            }
        }

        private void StopServer()
        {
            if (!running && (serverProc == null || serverProc.HasExited)) return;
            status.Text = "正在停止…";
            toggle.Text = "正在停止…";
            int pid = FindPidByPort(Port);
            try
            {
                if (pid > 0)
                {
                    Process.Start(new ProcessStartInfo("taskkill", "/F /T /PID " + pid)
                    { UseShellExecute = false, CreateNoWindow = true });
                }
                else if (serverProc != null && !serverProc.HasExited)
                {
                    Process.Start(new ProcessStartInfo("taskkill", "/F /T /PID " + serverProc.Id)
                    { UseShellExecute = false, CreateNoWindow = true });
                }
            }
            catch { }
        }

        private void UpdateStatus()
        {
            int pid = FindPidByPort(Port);
            running = pid > 0;
            starting = false;
            UpdateStatusUi();
            tray.Text = "whale-sitter - " + status.Text;
        }

        private static int FindPidByPort(int port)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("netstat", "-ano -p tcp");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                Process p = Process.Start(psi);
                string text = p.StandardOutput.ReadToEnd();
                p.WaitForExit(3000);
                string needle = ":" + port;
                foreach (string raw in text.Split(new char[] { '\n' }))
                {
                    if (raw.IndexOf(needle, StringComparison.Ordinal) < 0) continue;
                    if (raw.IndexOf("LISTENING", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    string[] parts = raw.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 5)
                    {
                        int pid;
                        if (int.TryParse(parts[parts.Length - 1], out pid)) return pid;
                    }
                }
            }
            catch { }
            return -1;
        }

        private void OpenDiagnostics()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("whale-sitter v" + Program.Version + " 诊断报告");
            sb.AppendLine("生成时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("OS: " + Environment.OSVersion + " (" + (Environment.Is64BitOperatingSystem ? "x64" : "x86") + ")");
            sb.AppendLine("Node: " + NodeVersionText());
            sb.AppendLine("npm 目录: " + NpmDir);
            sb.AppendLine("dsh 入口: " + (File.Exists(DshEntry) ? DshEntry : "未安装"));
            int pid = FindPidByPort(Port);
            sb.AppendLine("端口 " + Port + ": " + (pid > 0 ? "运行中 (PID " + pid + ")" : "空闲"));
            sb.AppendLine("HTTP " + Url + ": " + HttpStatusText());
            sb.AppendLine("--- dsh-web.log 末尾 ---");
            try
            {
                if (File.Exists(LogPath))
                {
                    string[] lines = File.ReadAllLines(LogPath);
                    for (int i = Math.Max(0, lines.Length - 20); i < lines.Length; i++)
                        sb.AppendLine(lines[i]);
                }
                else sb.AppendLine("(日志文件不存在)");
            }
            catch (Exception ex) { sb.AppendLine("(读取日志失败: " + ex.Message + ")"); }

            ShowReportDialog(sb.ToString());
        }

        private string HttpStatusText()
        {
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(Url);
                req.Method = "GET";
                req.Timeout = 2000;
                using (HttpWebResponse res = (HttpWebResponse)req.GetResponse())
                {
                    return ((int)res.StatusCode).ToString();
                }
            }
            catch (Exception ex) { return "连接失败 (" + ex.Message + ")"; }
        }

        private void ShowReportDialog(string report)
        {
            Form f = new Form();
            f.Text = "一键诊断报告";
            f.StartPosition = FormStartPosition.CenterParent;
            f.ClientSize = new Size(560, 400);
            f.MinimumSize = new Size(420, 300);
            f.Font = new Font("Microsoft YaHei UI", 9F);
            f.BackColor = pal.WindowBg;
            f.ForeColor = pal.Text;

            TextBox box = new TextBox();
            box.Multiline = true;
            box.ReadOnly = true;
            box.ScrollBars = ScrollBars.Both;
            box.Dock = DockStyle.Fill;
            box.Font = new Font("Consolas", 9F);
            box.Text = report;
            box.BackColor = pal.CardBg;
            box.ForeColor = pal.Text;
            box.BorderStyle = BorderStyle.None;

            Button copy = new Button();
            copy.Text = "复制报告";
            copy.FlatStyle = FlatStyle.Flat;
            copy.BackColor = pal.Accent;
            copy.ForeColor = Color.White;
            copy.Dock = DockStyle.Bottom;
            copy.Height = 36;
            copy.Click += delegate
            {
                try { Clipboard.SetText(report); MessageBox.Show("已复制到剪贴板，可直接粘贴到 GitHub issue 求助。", "whale-sitter"); }
                catch { }
            };

            f.Controls.Add(box);
            f.Controls.Add(copy);
            f.ShowDialog(this);
        }

        private void AppendLog(string line)
        {
            lock (logLock)
            {
                try
                {
                    File.AppendAllText(LogPath,
                        DateTime.Now.ToString("HH:mm:ss") + "  " + line + Environment.NewLine);
                }
                catch { }
            }
        }
    }
}
