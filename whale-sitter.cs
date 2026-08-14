using System;
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
        public const string Version = "2.2.2";

        [STAThread]
        private static void Main()
        {
            bool createdNew;
            using (Mutex m = new Mutex(true, "WhaleSitter.SingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show(L.Get("监控台已在运行，请查看系统托盘的鲸鱼图标。",
                        "Monitor already running. See the whale icon in the system tray."),
                        "whale-sitter", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
        }
    }

    internal static class L
    {
        private static bool en;
        public static bool IsEn { get { return en; } }
        public static void Set(bool english) { en = english; }
        public static string Get(string zh, string enText) { return en ? enText : zh; }
    }

    internal static class Settings
    {
        private const string KeyPath = @"Software\whale-sitter";
        public static int Lang;   // 0 auto, 1 zh, 2 en
        public static int Theme;  // 0 auto, 1 light, 2 dark
        public static int Port = 3080;

        public static void Load()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(KeyPath, false))
                {
                    if (k == null) return;
                    object v;
                    v = k.GetValue("Lang"); if (v != null) Lang = Convert.ToInt32(v);
                    v = k.GetValue("Theme"); if (v != null) Theme = Convert.ToInt32(v);
                    v = k.GetValue("Port"); if (v != null) Port = Convert.ToInt32(v);
                }
            }
            catch { }
        }

        public static void Save()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(KeyPath))
                {
                    k.SetValue("Lang", Lang);
                    k.SetValue("Theme", Theme);
                    k.SetValue("Port", Port);
                }
            }
            catch { }
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
        private static readonly string UrlBase = "http://127.0.0.1:";
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
        private readonly Button installBtn = new Button();
        private readonly Button settingsBtn = new Button();
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
        private int lastPort;
        private Palette pal;

        public MainForm()
        {
            Settings.Load();
            ApplyLanguage();
            lastPort = Settings.Port;
            InitUi();
            InitTray();
        }

        private static void ApplyLanguage()
        {
            if (Settings.Lang == 1) L.Set(false);
            else if (Settings.Lang == 2) L.Set(true);
            else
            {
                try { L.Set(System.Globalization.CultureInfo.InstalledUICulture.TwoLetterISOLanguageName != "zh"); }
                catch { L.Set(false); }
            }
        }

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private static Icon LoadAppIcon()
        {
            try
            {
                Icon ic = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (ic != null) return ic;
            }
            catch { }
            return SystemIcons.Application;
        }

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
                ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c npm prefix -g");
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
            return PortableNodeDir != null || NodeVersionText() != L.Get("未检测到", "not found");
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
                return v.Length > 0 ? v : L.Get("未检测到", "not found");
            }
            catch { return L.Get("未检测到", "not found"); }
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

        private bool ResolveDark()
        {
            if (Settings.Theme == 1) return false;
            if (Settings.Theme == 2) return true;
            return !SystemUsesLightTheme();
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
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            ClientSize = new Size(520, 268);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 10F);
            DoubleBuffered = true;

            whale.SizeMode = PictureBoxSizeMode.Zoom;
            whale.Size = new Size(36, 36);
            whale.Location = new Point(18, 16);
            using (Icon appIcon = LoadAppIcon())
            {
                whale.Image = appIcon.ToBitmap();
                Icon = (Icon)appIcon.Clone();
            }

            title.Text = "whale-sitter";
            title.Font = new Font(Font.FontFamily, 15F, FontStyle.Bold);
            title.Location = new Point(64, 14);
            title.AutoSize = true;

            subtitle.Font = new Font(Font.FontFamily, 9F);
            subtitle.Location = new Point(66, 40);
            subtitle.AutoSize = true;

            card.Location = new Point(16, 64);
            card.Size = new Size(448, 62);

            dot.Text = "●";
            dot.Font = new Font(Font.FontFamily, 14F, FontStyle.Bold);
            dot.Location = new Point(24, 19);
            dot.AutoSize = true;

            status.Font = new Font(Font.FontFamily, 11F, FontStyle.Bold);
            status.Location = new Point(52, 16);
            status.AutoSize = true;

            statusHint.Font = new Font(Font.FontFamily, 8.5F);
            statusHint.Location = new Point(52, 38);
            statusHint.AutoSize = true;

            card.Controls.Add(dot);
            card.Controls.Add(status);
            card.Controls.Add(statusHint);

            toggle.Location = new Point(16, 136);
            toggle.Size = new Size(448, 54);
            toggle.Font = new Font(Font.FontFamily, 15F, FontStyle.Bold);
            toggle.Cursor = Cursors.Hand;
            toggle.Click += delegate { ToggleMain(); };

            autoStartBtn.Size = new Size(96, 30);
            autoStartBtn.Location = new Point(16, 202);
            autoStartBtn.FlatStyle = FlatStyle.Flat;
            autoStartBtn.FlatAppearance.BorderSize = 0;
            autoStartBtn.Font = new Font(Font.FontFamily, 9F);
            autoStartBtn.Cursor = Cursors.Hand;
            autoStartBtn.Click += delegate { SetAutoStart(!AutoStartEnabled()); };

            openBtn.Size = new Size(72, 30);
            openBtn.Location = new Point(120, 202);
            openBtn.FlatStyle = FlatStyle.Flat;
            openBtn.FlatAppearance.BorderSize = 0;
            openBtn.Font = new Font(Font.FontFamily, 9F);
            openBtn.Cursor = Cursors.Hand;
            openBtn.Click += delegate { try { Process.Start(UrlBase + Settings.Port + "/"); } catch { } };

            logBtn.Size = new Size(72, 30);
            logBtn.Location = new Point(200, 202);
            logBtn.FlatStyle = FlatStyle.Flat;
            logBtn.FlatAppearance.BorderSize = 0;
            logBtn.Font = new Font(Font.FontFamily, 9F);
            logBtn.Cursor = Cursors.Hand;
            logBtn.Click += delegate { OpenLog(); };

            diagBtn.Size = new Size(72, 30);
            diagBtn.Location = new Point(280, 202);
            diagBtn.FlatStyle = FlatStyle.Flat;
            diagBtn.FlatAppearance.BorderSize = 0;
            diagBtn.Font = new Font(Font.FontFamily, 9F);
            diagBtn.Cursor = Cursors.Hand;
            diagBtn.Click += delegate { OpenDiagnostics(); };

            installBtn.Size = new Size(72, 30);
            installBtn.Location = new Point(360, 202);
            installBtn.FlatStyle = FlatStyle.Flat;
            installBtn.FlatAppearance.BorderSize = 0;
            installBtn.Font = new Font(Font.FontFamily, 9F);
            installBtn.Cursor = Cursors.Hand;
            installBtn.Click += delegate { InstallAll(); };

            settingsBtn.Size = new Size(72, 30);
            settingsBtn.Location = new Point(440, 202);
            settingsBtn.FlatStyle = FlatStyle.Flat;
            settingsBtn.FlatAppearance.BorderSize = 0;
            settingsBtn.Font = new Font(Font.FontFamily, 9F);
            settingsBtn.Cursor = Cursors.Hand;
            settingsBtn.Click += delegate { OpenSettings(); };

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
            Controls.Add(installBtn);
            Controls.Add(settingsBtn);
            Controls.Add(hint);

            ApplyStrings();
            ApplyTheme(ResolveDark());
        }

        private void ApplyStrings()
        {
            Text = L.Get("whale-sitter · DeepSeek Harness 监控台", "whale-sitter · DeepSeek Harness Monitor");
            subtitle.Text = L.Get("DeepSeek Harness 监控台 · v", "DeepSeek Harness Monitor · v") + Program.Version;
            openBtn.Text = L.Get("打开界面", "Open UI");
            logBtn.Text = L.Get("查看日志", "View Log");
            diagBtn.Text = L.Get("一键诊断", "Diagnose");
            installBtn.Text = L.Get("安装/修复", "Install/Fix");
            settingsBtn.Text = L.Get("设置", "Settings");
            hint.Text = L.Get("✕ 关闭 = 最小化到托盘", "✕ Close = minimize to tray");
            UpdateAutoStartButton();
            UpdateStatusUi();
            UpdateTrayMenu();
        }

        private ContextMenuStrip trayMenu;

        private void InitTray()
        {
            tray.Text = "whale-sitter";
            tray.Icon = LoadAppIcon();
            tray.Visible = true;
            tray.DoubleClick += delegate { ShowWindow(); };

            trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("", null, delegate { ShowWindow(); });
            trayMenu.Items.Add("", null, delegate { try { Process.Start(UrlBase + Settings.Port + "/"); } catch { } });
            trayMenu.Items.Add("", null, delegate { OpenLog(); });
            trayMenu.Items.Add("", null, delegate { OpenDiagnostics(); });
            trayMenu.Items.Add("", null, delegate { OpenSettings(); });
            trayMenu.Items.Add("", null, delegate { InstallAll(); });
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("", null, delegate { StartServer(); });
            trayMenu.Items.Add("", null, delegate { StopServer(); });
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("", null, delegate
            {
                realExit = true;
                tray.Visible = false;
                Application.Exit();
            });
            tray.ContextMenuStrip = trayMenu;
            UpdateTrayMenu();
        }

        private void UpdateTrayMenu()
        {
            if (trayMenu == null || trayMenu.Items.Count < 11) return;
            trayMenu.Items[0].Text = L.Get("打开监控台", "Open Monitor");
            trayMenu.Items[1].Text = L.Get("打开界面", "Open UI");
            trayMenu.Items[2].Text = L.Get("查看日志", "View Log");
            trayMenu.Items[3].Text = L.Get("一键诊断", "Diagnose");
            trayMenu.Items[4].Text = L.Get("设置", "Settings");
            trayMenu.Items[5].Text = L.Get("一键安装 / 修复环境", "Install / Fix Environment");
            trayMenu.Items[7].Text = L.Get("启动服务", "Start Service");
            trayMenu.Items[8].Text = L.Get("停止服务", "Stop Service");
            trayMenu.Items[10].Text = L.Get("退出", "Exit");
        }

        private void OpenLog()
        {
            try
            {
                if (File.Exists(LogPath)) Process.Start("notepad.exe", "\"" + LogPath + "\"");
                else MessageBox.Show(L.Get("日志文件还不存在：\n", "Log file not found:\n") + LogPath,
                    "whale-sitter", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
                bool dark = ResolveDark();
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

            foreach (Control c in new Control[] { autoStartBtn, openBtn, logBtn, diagBtn, installBtn, settingsBtn })
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
            autoStartBtn.Text = L.Get("开机自启：", "Auto-start: ") + (on ? L.Get("开", "On") : L.Get("关", "Off"));
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
                status.Text = L.Get("安装中…", "Installing…");
                toggle.SetColors(pal.Warn, Color.White);
                toggle.Text = L.Get("安装中，请稍候…", "Installing, please wait…");
                return;
            }

            if (starting)
            {
                status.Text = L.Get("启动中…", "Starting…");
                dot.ForeColor = pal.Warn;
                status.ForeColor = pal.Warn;
                toggle.SetColors(pal.Warn, Color.White);
                toggle.Text = L.Get("启动中…", "Starting…");
                return;
            }

            if (!NodeAvailable())
            {
                dot.ForeColor = pal.Danger;
                status.ForeColor = pal.Danger;
                status.Text = L.Get("缺少 Node.js", "Node.js not found");
                statusHint.Text = L.Get("点下方按钮自动安装运行环境", "Click the button below to auto-install");
                toggle.SetColors(pal.Accent, Color.White);
                toggle.Text = L.Get("一键安装 Node.js + dsh", "Install Node.js + dsh");
                return;
            }

            if (!DshInstalled())
            {
                dot.ForeColor = pal.Warn;
                status.ForeColor = pal.Warn;
                status.Text = L.Get("未安装 dsh", "dsh not installed");
                statusHint.Text = L.Get("点下方按钮一键安装 DeepSeek Harness", "Click below to install DeepSeek Harness");
                toggle.SetColors(pal.Accent, Color.White);
                toggle.Text = L.Get("一键安装 DeepSeek Harness", "Install DeepSeek Harness");
                return;
            }

            statusHint.Text = L.Get("Web UI  http://127.0.0.1:", "Web UI  http://127.0.0.1:") + Settings.Port;
            if (running)
            {
                dot.ForeColor = pulseOn ? pal.Success : pal.SuccessDim;
                status.ForeColor = pal.Success;
                status.Text = L.Get("运行中 · PID ", "Running · PID ") + runningPidText();
                toggle.SetColors(pal.Success, Color.White);
                toggle.Text = L.Get("点击停止服务", "Click to stop");
            }
            else
            {
                dot.ForeColor = pal.Danger;
                status.ForeColor = pal.TextDim;
                status.Text = L.Get("已停止", "Stopped");
                toggle.SetColors(pal.BtnBg, pal.BtnText);
                toggle.Text = L.Get("点击启动服务", "Click to start");
            }
        }

        private string runningPidText()
        {
            int pid = FindPidByPort(Settings.Port);
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
                    L.Get("已最小化到系统托盘，双击鲸鱼图标即可恢复面板。",
                        "Minimized to tray. Double-click the whale icon to restore."), ToolTipIcon.Info);
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
                bool wasRunning = running;
                if (wasRunning)
                {
                    StopServer();
                    await Task.Delay(600);
                }

                string nodeDir = PortableNodeDir;
                if (nodeDir != null)
                {
                    SetInstallUi(L.Get("正在安装/修复 DeepSeek Harness（可能需要几分钟）…",
                        "Installing/repairing DeepSeek Harness (may take a few minutes)…"));
                    AppendLog("一键安装/修复：npm install -g @deepseek-ai/dsh（便携 Node）");
                    await RunNpmInstallAsync(Path.Combine(nodeDir, "node_modules", "npm", "bin", "npm-cli.js"), nodeDir);
                }
                else if (NodeAvailable())
                {
                    SetInstallUi(L.Get("正在安装/修复 DeepSeek Harness（可能需要几分钟）…",
                        "Installing/repairing DeepSeek Harness (may take a few minutes)…"));
                    AppendLog("一键安装/修复：npm install -g @deepseek-ai/dsh（系统 Node）");
                    string npmCli = await ResolveSystemNpmCliAsync();
                    await RunNpmInstallAsync(npmCli, null);
                }
                else
                {
                    SetInstallUi(L.Get("正在下载 Node.js…", "Downloading Node.js…"));
                    AppendLog("一键安装：下载 Node.js");
                    nodeDir = await InstallNodeAsync();
                    SetInstallUi(L.Get("正在安装 DeepSeek Harness（可能需要几分钟）…",
                        "Installing DeepSeek Harness (may take a few minutes)…"));
                    AppendLog("一键安装：npm install -g @deepseek-ai/dsh（便携 Node）");
                    await RunNpmInstallAsync(Path.Combine(nodeDir, "node_modules", "npm", "bin", "npm-cli.js"), nodeDir);
                    PortableNodeDir = FindPortableNodeDir();
                }

                UpdateStatus();
                SetInstallUi(L.Get("安装完成，正在启动服务…", "Installed. Starting service…"));
                if (!DshInstalled())
                    throw new Exception(L.Get("dsh 安装后仍未检测到，请查看日志或使用一键诊断。",
                        "dsh still not detected after install. Check the log or use Diagnose."));
                if (!running) StartServer();
                status.Text = L.Get("安装完成", "Install complete");
            }
            catch (Exception ex)
            {
                status.Text = L.Get("安装失败", "Install failed");
                statusHint.Text = ex.Message;
                AppendLog("安装失败: " + ex);
            }
            finally
            {
                installInProgress = false;
                UpdateStatusUi();
            }
        }

        private Task<string> ResolveSystemNpmCliAsync()
        {
            return Task.Run(() =>
            {
                // 优先：由 node.exe 所在目录定位 npm-cli.js（不依赖 npm 可执行）
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c where node");
                    psi.UseShellExecute = false;
                    psi.CreateNoWindow = true;
                    psi.RedirectStandardOutput = true;
                    Process p = Process.Start(psi);
                    string output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(3000);
                    foreach (string line in output.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string t = line.Trim();
                        if (t.ToLowerInvariant().EndsWith("node.exe"))
                        {
                            string npmCli = Path.Combine(Path.GetDirectoryName(t),
                                "node_modules", "npm", "bin", "npm-cli.js");
                            if (File.Exists(npmCli)) return npmCli;
                        }
                    }
                }
                catch { }

                // 回退：npm root -g（npm 是 .cmd，必须经 cmd.exe 执行）
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c npm root -g");
                    psi.UseShellExecute = false;
                    psi.CreateNoWindow = true;
                    psi.RedirectStandardOutput = true;
                    Process p = Process.Start(psi);
                    string root = p.StandardOutput.ReadToEnd().Trim();
                    p.WaitForExit(3000);
                    string npmCli = Path.Combine(root, "npm", "bin", "npm-cli.js");
                    if (File.Exists(npmCli)) return npmCli;
                }
                catch { }

                throw new Exception(L.Get("未找到 npm，请先安装 Node.js 后再试。",
                    "npm not found. Install Node.js first and retry."));
            });
        }

        private Task RunNpmInstallAsync(string npmCli, string workingDir)
        {
            return Task.Run(() =>
            {
                if (!File.Exists(npmCli))
                    throw new Exception(L.Get("未找到 npm（", "npm not found (") + npmCli + "），安装不完整。");

                ProcessStartInfo psi = new ProcessStartInfo("node.exe", "\"" + npmCli + "\" install -g @deepseek-ai/dsh");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.WorkingDirectory = workingDir ?? NpmDir;

                Process p = Process.Start(psi);
                string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                p.WaitForExit();

                lock (logLock)
                {
                    try { File.AppendAllText(LogPath, output + Environment.NewLine); } catch { }
                }
                if (p.ExitCode != 0)
                    throw new Exception(L.Get("npm 安装失败（exit ", "npm install failed (exit ") +
                        p.ExitCode + "），详见日志。");
            });
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
                    throw new Exception(L.Get("无法获取 Node.js 下载地址（网络问题？），请检查网络后重试。",
                        "Cannot resolve Node.js download URL (network issue?). Check your network and retry."));

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
                    throw new Exception(L.Get("Node.js 解压后未找到 node.exe，请重试。",
                        "node.exe not found after extracting Node.js. Retry."));
                return nodeDir;
            });
        }

        private void StartServer()
        {
            if (running || starting || installInProgress) return;
            if (!NodeAvailable() || !DshInstalled()) return;
            starting = true;
            status.Text = L.Get("启动中…", "Starting…");
            toggle.Text = L.Get("启动中…", "Starting…");
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(NodeExe,
                    "\"" + DshEntry + "\" web --port " + Settings.Port);
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
                status.Text = L.Get("启动失败：", "Start failed: ") + ex.Message;
                AppendLog("启动失败: " + ex.Message);
            }
        }

        private void StopServer()
        {
            if (!running && (serverProc == null || serverProc.HasExited)) return;
            status.Text = L.Get("正在停止…", "Stopping…");
            toggle.Text = L.Get("正在停止…", "Stopping…");
            int pid = FindPidByPort(Settings.Port);
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
            int pid = FindPidByPort(Settings.Port);
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

        private void OpenSettings()
        {
            using (SettingsForm f = new SettingsForm(pal))
            {
                if (f.ShowDialog(this) == DialogResult.OK)
                {
                    bool langChanged = f.Lang != Settings.Lang;
                    int oldPort = Settings.Port;
                    bool portChanged = f.Port != oldPort;
                    Settings.Lang = f.Lang;
                    Settings.Theme = f.Theme;
                    Settings.Port = f.Port;
                    Settings.Save();

                    if (langChanged)
                    {
                        ApplyLanguage();
                        ApplyStrings();
                    }

                    ApplyTheme(ResolveDark());

                    if (portChanged)
                    {
                        lastPort = Settings.Port;
                        bool wasRunning = running;
                        if (wasRunning) StopServer();
                        SetInstallUi(L.Get("端口已修改，服务将重启。", "Port changed, service will restart."));
                        UpdateStatusUi();
                        if (wasRunning) StartServer();
                    }
                }
            }
        }

        private void OpenDiagnostics()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("whale-sitter v" + Program.Version + L.Get(" 诊断报告", " Diagnostics Report"));
            sb.AppendLine(L.Get("生成时间", "Generated") + ": " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("OS: " + Environment.OSVersion + " (" + (Environment.Is64BitOperatingSystem ? "x64" : "x86") + ")");
            sb.AppendLine("Node: " + NodeVersionText());
            sb.AppendLine(L.Get("npm 目录", "npm dir") + ": " + NpmDir);
            sb.AppendLine(L.Get("dsh 入口", "dsh entry") + ": " + (File.Exists(DshEntry) ? DshEntry : L.Get("未安装", "not installed")));
            int pid = FindPidByPort(Settings.Port);
            sb.AppendLine(L.Get("端口 ", "Port ") + Settings.Port + ": " + (pid > 0 ? L.Get("运行中 (PID ", "Running (PID ") + pid + ")" : L.Get("空闲", "free")));
            sb.AppendLine("HTTP " + UrlBase + Settings.Port + "/: " + HttpStatusText());
            sb.AppendLine(L.Get("--- dsh-web.log 末尾 ---", "--- dsh-web.log tail ---"));
            try
            {
                if (File.Exists(LogPath))
                {
                    string[] lines = File.ReadAllLines(LogPath);
                    for (int i = Math.Max(0, lines.Length - 20); i < lines.Length; i++)
                        sb.AppendLine(lines[i]);
                }
                else sb.AppendLine(L.Get("(日志文件不存在)", "(log file missing)"));
            }
            catch (Exception ex) { sb.AppendLine(L.Get("(读取日志失败: ", "(failed to read log: ") + ex.Message + ")"); }

            ShowReportDialog(sb.ToString());
        }

        private string HttpStatusText()
        {
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(UrlBase + Settings.Port + "/");
                req.Method = "GET";
                req.Timeout = 2000;
                using (HttpWebResponse res = (HttpWebResponse)req.GetResponse())
                {
                    return ((int)res.StatusCode).ToString();
                }
            }
            catch (Exception ex) { return L.Get("连接失败 (", "Connection failed (") + ex.Message + ")"; }
        }

        private void ShowReportDialog(string report)
        {
            Form f = new Form();
            f.Text = L.Get("一键诊断报告", "Diagnostics Report");
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
            copy.Text = L.Get("复制报告", "Copy Report");
            copy.FlatStyle = FlatStyle.Flat;
            copy.BackColor = pal.Accent;
            copy.ForeColor = Color.White;
            copy.Dock = DockStyle.Bottom;
            copy.Height = 36;
            copy.Click += delegate
            {
                try
                {
                    Clipboard.SetText(report);
                    MessageBox.Show(L.Get("已复制到剪贴板，可直接粘贴到 GitHub issue 求助。",
                        "Copied to clipboard. Paste into a GitHub issue for help."), "whale-sitter");
                }
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

    internal class SettingsForm : Form
    {
        public int Lang;
        public int Theme;
        public int Port;

        private readonly ComboBox langBox = new ComboBox();
        private readonly ComboBox themeBox = new ComboBox();
        private readonly NumericUpDown portBox = new NumericUpDown();
        private Palette pal;

        public SettingsForm(Palette p)
        {
            pal = p;
            Lang = Settings.Lang;
            Theme = Settings.Theme;
            Port = Settings.Port;
            InitUi();
        }

        private void InitUi()
        {
            Text = L.Get("设置", "Settings");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(340, 190);
            BackColor = pal.WindowBg;
            ForeColor = pal.Text;
            Font = new Font("Microsoft YaHei UI", 10F);

            AddRow(0, L.Get("语言", "Language"), langBox,
                new string[] { L.Get("跟随系统", "System"), L.Get("中文", "Chinese"), "English" }, Lang);
            AddRow(1, L.Get("主题", "Theme"), themeBox,
                new string[] { L.Get("跟随系统", "System"), L.Get("浅色", "Light"), L.Get("深色", "Dark") }, Theme);

            Label portLabel = new Label();
            portLabel.Text = L.Get("服务端口", "Server port");
            portLabel.Location = new Point(24, 96);
            portLabel.AutoSize = true;

            portBox.Location = new Point(150, 92);
            portBox.Size = new Size(120, 24);
            portBox.Minimum = 1024;
            portBox.Maximum = 65535;
            portBox.Value = Math.Max(1024, Math.Min(65535, Port));

            Button ok = new Button();
            ok.Text = L.Get("确定", "OK");
            ok.FlatStyle = FlatStyle.Flat;
            ok.BackColor = pal.Accent;
            ok.ForeColor = Color.White;
            ok.Size = new Size(120, 32);
            ok.Location = new Point(90, 140);
            ok.Click += delegate
            {
                Lang = langBox.SelectedIndex < 0 ? 0 : langBox.SelectedIndex;
                Theme = themeBox.SelectedIndex < 0 ? 0 : themeBox.SelectedIndex;
                Port = (int)portBox.Value;
                DialogResult = DialogResult.OK;
            };

            Button cancel = new Button();
            cancel.Text = L.Get("取消", "Cancel");
            cancel.FlatStyle = FlatStyle.Flat;
            cancel.BackColor = pal.BtnBg;
            cancel.ForeColor = pal.BtnText;
            cancel.Size = new Size(120, 32);
            cancel.Location = new Point(220, 140);
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; };

            Controls.Add(portLabel);
            Controls.Add(portBox);
            Controls.Add(ok);
            Controls.Add(cancel);
        }

        private void AddRow(int row, string label, ComboBox box, string[] items, int selected)
        {
            Label l = new Label();
            l.Text = label;
            l.Location = new Point(24, 16 + row * 44);
            l.AutoSize = true;

            box.Items.AddRange(items);
            box.SelectedIndex = Math.Max(0, Math.Min(items.Length - 1, selected));
            box.Location = new Point(150, 12 + row * 44);
            box.Size = new Size(150, 24);
            box.DropDownStyle = ComboBoxStyle.DropDownList;

            Controls.Add(l);
            Controls.Add(box);
        }
    }
}
