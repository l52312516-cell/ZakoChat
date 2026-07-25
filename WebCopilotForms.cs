using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ZakoChat
{
    internal static class CopilotSurfaceFactory
    {
        public static IQuickPromptSurface CreateQuickPrompt(AppSettings settings)
        {
            bool shouldUseWebView = ShouldUseWebView(settings);
            if (shouldUseWebView && WebView2RuntimeBootstrap.TryPrepare())
            {
                try
                {
                    return new WebQuickPromptForm(settings);
                }
                catch (Exception ex)
                {
                    CrashLog.Write(ex);
                    WebView2RuntimeBootstrap.WriteStatus("快速小窗 WebView2 窗体创建失败：" + ex.Message);
                }
            }
            if (shouldUseWebView)
                WebView2RuntimeBootstrap.WriteStatus("快速小窗回退原生界面：" + WebView2RuntimeBootstrap.LastStatus);
            return new QuickPromptForm(settings);
        }

        public static ISidebarSurface CreateSidebar(AppSettings settings, Action<string> onSend, Action onStop, Action onSettings, Action onHide)
        {
            bool shouldUseWebView = ShouldUseWebView(settings);
            if (shouldUseWebView && WebView2RuntimeBootstrap.TryPrepare())
            {
                try
                {
                    return new WebSidebarForm(settings, onSend, onStop, onSettings, onHide);
                }
                catch (Exception ex)
                {
                    CrashLog.Write(ex);
                    WebView2RuntimeBootstrap.WriteStatus("完整侧栏 WebView2 窗体创建失败：" + ex.Message);
                }
            }
            if (shouldUseWebView)
                WebView2RuntimeBootstrap.WriteStatus("完整侧栏回退原生界面：" + WebView2RuntimeBootstrap.LastStatus);
            return new SidebarForm(settings, onSend, onStop, onSettings, onHide);
        }

        private static bool ShouldUseWebView(AppSettings settings)
        {
            if (settings == null || settings.Appearance == null) return true;
            settings.Appearance.Normalize();
            return settings.Appearance.RenderMode != UiRenderMode.Native;
        }

    }

    internal static class WebView2RuntimeBootstrap
    {
        private static bool _prepared;
        private static bool _failed;
        private static bool _resolveAttached;
        private static Assembly _coreAssembly;
        private static Assembly _winFormsAssembly;

        public static string LastStatus { get; private set; }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        public static bool TryPrepare()
        {
            if (_prepared) return true;
            if (_failed) return false;

            try
            {
                if (!_resolveAttached)
                {
                    AppDomain.CurrentDomain.AssemblyResolve += ResolveWebView2Assembly;
                    _resolveAttached = true;
                }
                string runtimeDir = Path.Combine(AppInfo.AppDataDir, "runtime");
                Directory.CreateDirectory(runtimeDir);
                ExtractResource("ZakoChat.Resources.WebView2Loader.dll", Path.Combine(runtimeDir, "WebView2Loader.dll"));
                SetDllDirectory(runtimeDir);
                _coreAssembly = _coreAssembly ?? LoadResourceAssembly("ZakoChat.Resources.Microsoft.Web.WebView2.Core.dll");
                _winFormsAssembly = _winFormsAssembly ?? LoadResourceAssembly("ZakoChat.Resources.Microsoft.Web.WebView2.WinForms.dll");
                if (_coreAssembly == null || _winFormsAssembly == null)
                    throw new FileNotFoundException("WebView2 托管程序集未嵌入到程序资源中。");

                Type environmentType = _coreAssembly.GetType("Microsoft.Web.WebView2.Core.CoreWebView2Environment", true);
                MethodInfo versionMethod = environmentType.GetMethod(
                    "GetAvailableBrowserVersionString",
                    new Type[] { typeof(string) });
                if (versionMethod == null)
                    throw new MissingMethodException("WebView2 Runtime 检测 API 不可用。");
                string version = Convert.ToString(versionMethod.Invoke(null, new object[] { null }));
                _prepared = !string.IsNullOrEmpty(version);
                _failed = !_prepared;
                LastStatus = _prepared ? "WebView2 高级界面可用：" + version : "未检测到 WebView2 Runtime，已回退原生界面。";
                return _prepared;
            }
            catch (Exception ex)
            {
                _failed = true;
                LastStatus = "WebView2 初始化失败，已回退原生界面：" + ex.Message;
                CrashLog.Write(ex);
                WriteStatus(LastStatus);
                return false;
            }
        }

        public static void WriteStatus(string message)
        {
            try
            {
                if (!Directory.Exists(AppInfo.AppDataDir))
                    Directory.CreateDirectory(AppInfo.AppDataDir);
                File.AppendAllText(
                    Path.Combine(AppInfo.AppDataDir, "webview.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + (message ?? string.Empty) + Environment.NewLine,
                    System.Text.Encoding.UTF8);
            }
            catch { }
        }

        private static Assembly ResolveWebView2Assembly(object sender, ResolveEventArgs args)
        {
            AssemblyName name = new AssemblyName(args.Name);
            if (name.Name == "Microsoft.Web.WebView2.Core")
                return _coreAssembly ?? (_coreAssembly = LoadResourceAssembly("ZakoChat.Resources.Microsoft.Web.WebView2.Core.dll"));
            if (name.Name == "Microsoft.Web.WebView2.WinForms")
                return _winFormsAssembly ?? (_winFormsAssembly = LoadResourceAssembly("ZakoChat.Resources.Microsoft.Web.WebView2.WinForms.dll"));
            return null;
        }

        private static Assembly LoadResourceAssembly(string resourceName)
        {
            Assembly current = Assembly.GetExecutingAssembly();
            using (Stream stream = current.GetManifestResourceStream(resourceName))
            {
                if (stream == null) return null;
                byte[] data = new byte[stream.Length];
                int offset = 0;
                while (offset < data.Length)
                {
                    int read = stream.Read(data, offset, data.Length - offset);
                    if (read <= 0) break;
                    offset += read;
                }
                return Assembly.Load(data);
            }
        }

        private static void ExtractResource(string resourceName, string path)
        {
            if (File.Exists(path)) return;
            Assembly current = Assembly.GetExecutingAssembly();
            using (Stream stream = current.GetManifestResourceStream(resourceName))
            {
                if (stream == null) return;
                using (FileStream file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
                {
                    byte[] buffer = new byte[81920];
                    while (true)
                    {
                        int read = stream.Read(buffer, 0, buffer.Length);
                        if (read <= 0) break;
                        file.Write(buffer, 0, read);
                    }
                }
            }
        }
    }

    public sealed class WebQuickPromptForm : Form, IQuickPromptSurface
    {
        private readonly AppSettings _settings;
        private readonly JavaScriptSerializer _serializer;
        private readonly Timer _animationTimer;
        private readonly Timer _webStartTimer;
        private WebView2 _web;
        private Rectangle _shownBounds;
        private Rectangle _hiddenBounds;
        private DateTime _animationStart;
        private bool _targetVisible;
        private bool _ready;
        private bool _webStartScheduled;
        private bool _dragging;
        private Point _lastDragPoint;
        private ThemePalette _theme;
        private string _providerId;
        private string _providerName;
        private string _modelName;
        private string _memoryText;
        private bool _busy;

        public event Action<string> SendRequested;
        public event EventHandler ExpandRequested;
        public event EventHandler SettingsRequested;
        public event EventHandler HideRequested;

        public WebQuickPromptForm(AppSettings settings)
        {
            _settings = settings;
            _settings.Normalize();
            _serializer = new JavaScriptSerializer();
            _theme = ThemePalette.Resolve(_settings);
            _providerId = string.Empty;
            _providerName = string.Empty;
            _modelName = string.Empty;
            _memoryText = string.Empty;

            Text = AppInfo.AppName;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = _settings.Window.TopMost;
            Size = new Size(760, 154);
            MinimumSize = new Size(560, 136);
            BackColor = _theme.WindowBottom;
            KeyPreview = true;

            _animationTimer = new Timer();
            _animationTimer.Interval = 15;
            _animationTimer.Tick += OnAnimationTick;
            _webStartTimer = new Timer();
            _webStartTimer.Interval = 50;
            _webStartTimer.Tick += delegate
            {
                _webStartTimer.Stop();
                if (!IsDisposed && IsHandleCreated) InitializeWeb();
            };
            Resize += delegate { if (_web != null) _web.Bounds = WebViewBounds(); ApplyRoundedCorners(); };
            ApplySettings();
        }

        public void SetIcon(Icon icon)
        {
            Icon = icon;
        }

        public void ApplySettings()
        {
            _settings.Normalize();
            _theme = ThemePalette.Resolve(_settings);
            TopMost = _settings.Window.TopMost;
            BackColor = _theme.WindowBottom;
            PostState();
        }

        public void SetStatus(string providerId, string providerName, string modelName, string memoryText)
        {
            _providerId = providerId ?? string.Empty;
            _providerName = providerName ?? string.Empty;
            _modelName = modelName ?? string.Empty;
            _memoryText = memoryText ?? string.Empty;
            PostState();
        }

        public void SetBusy(bool busy)
        {
            _busy = busy;
            PostState();
        }

        public void ShowQuickAnimated()
        {
            PositionQuick();
            _animationTimer.Stop();
            if (!_settings.Appearance.AnimationEnabled || _settings.Appearance.ReducedMotion)
            {
                Bounds = _shownBounds;
                Opacity = _settings.Window.OpacityPercent / 100.0;
                Show();
                EnsureWebStarted();
                Activate();
                FocusInput();
                return;
            }

            Bounds = _hiddenBounds;
            Opacity = 0.08;
            _targetVisible = true;
            _animationStart = DateTime.UtcNow;
            Show();
            EnsureWebStarted();
            _animationTimer.Start();
            Activate();
            FocusInput();
        }

        public void HideQuickAnimated()
        {
            if (!Visible) return;
            PositionQuick();
            _animationTimer.Stop();
            if (!_settings.Appearance.AnimationEnabled || _settings.Appearance.ReducedMotion)
            {
                Hide();
                MemoryTrimmer.TrimCurrentProcess();
                return;
            }
            _targetVisible = false;
            _animationStart = DateTime.UtcNow;
            _animationTimer.Start();
        }

        public void HideQuick()
        {
            _animationTimer.Stop();
            Hide();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyRoundedCorners();
            if (_webStartScheduled && _web == null)
                QueueWebStart();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_animationTimer != null) _animationTimer.Dispose();
                if (_webStartTimer != null) _webStartTimer.Dispose();
                if (_web != null) _web.Dispose();
            }
            base.Dispose(disposing);
        }

        private void EnsureWebStarted()
        {
            if (_webStartScheduled || _web != null) return;
            _webStartScheduled = true;
            QueueWebStart();
        }

        private void QueueWebStart()
        {
            if (!IsHandleCreated || IsDisposed) return;
            try
            {
                BeginInvoke(new MethodInvoker(delegate
                {
                    if (!IsDisposed && _web == null) InitializeWeb();
                }));
            }
            catch
            {
                _webStartTimer.Start();
            }
        }

        private async void InitializeWeb()
        {
            try
            {
                _web = new WebView2();
                _web.Dock = DockStyle.None;
                _web.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
                _web.Bounds = WebViewBounds();
                _web.AllowExternalDrop = false;
                _web.DefaultBackgroundColor = _theme.WindowBottom;
                Controls.Add(_web);
                _web.CoreWebView2InitializationCompleted += OnWebInitialized;
                Directory.CreateDirectory(AppInfo.WebView2UserDataDir);
                CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(
                    null,
                    AppInfo.WebView2UserDataDir,
                    null);
                await _web.EnsureCoreWebView2Async(environment);
            }
            catch (Exception ex)
            {
                CrashLog.Write(ex);
                if (_web != null)
                {
                    Controls.Remove(_web);
                    _web.Dispose();
                    _web = null;
                }
            }
        }

        private void OnWebInitialized(object sender, CoreWebView2InitializationCompletedEventArgs e)
        {
            if (!e.IsSuccess || _web == null || _web.CoreWebView2 == null)
            {
                CrashLog.Write(e.InitializationException ?? new InvalidOperationException("WebView2 快速提问界面初始化失败。"));
                return;
            }
            _web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            _web.CoreWebView2.Settings.AreDevToolsEnabled = false;
            _web.CoreWebView2.WebMessageReceived += OnWebMessage;
            _web.NavigateToString(WebSafeTemplates.BuildQuickHtml());
        }

        private void OnWebMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            Dictionary<string, object> msg = Parse(e.WebMessageAsJson);
            string action = GetString(msg, "action");
            if (action == "ready")
            {
                _ready = true;
                PostState();
            }
            else if (action == "send")
            {
                string text = GetString(msg, "text").Trim();
                if (text.Length > 0 && SendRequested != null) SendRequested(text);
            }
            else if (action == "expand")
            {
                if (ExpandRequested != null) ExpandRequested(this, EventArgs.Empty);
            }
            else if (action == "settings")
            {
                if (SettingsRequested != null) SettingsRequested(this, EventArgs.Empty);
            }
            else if (action == "hide")
            {
                if (HideRequested != null) HideRequested(this, EventArgs.Empty);
            }
            else if (action == "dragStart")
            {
                _dragging = true;
                _lastDragPoint = new Point(GetInt(msg, "screenX"), GetInt(msg, "screenY"));
            }
            else if (action == "dragMove" && _dragging)
            {
                Point p = new Point(GetInt(msg, "screenX"), GetInt(msg, "screenY"));
                Location = new Point(Left + p.X - _lastDragPoint.X, Top + p.Y - _lastDragPoint.Y);
                _lastDragPoint = p;
            }
            else if (action == "dragEnd")
            {
                _dragging = false;
            }
        }

        private void PostState()
        {
            if (!_ready || _web == null || _web.CoreWebView2 == null) return;
            Dictionary<string, object> state = BaseState("quickState");
            state["providerId"] = _providerId;
            state["provider"] = string.IsNullOrEmpty(_providerName) ? "未配置服务商" : _providerName;
            state["model"] = string.IsNullOrEmpty(_modelName) ? "未选择模型" : _modelName;
            state["memory"] = _memoryText ?? string.Empty;
            state["busy"] = _busy;
            _web.CoreWebView2.PostWebMessageAsJson(_serializer.Serialize(state));
        }

        private Dictionary<string, object> BaseState(string type)
        {
            Dictionary<string, object> state = new Dictionary<string, object>();
            state["type"] = type;
            state["appName"] = AppInfo.AppName;
            state["theme"] = ThemeObject(_theme, _settings);
            return state;
        }

        private void FocusInput()
        {
            try
            {
                if (_web != null && _web.CoreWebView2 != null)
                    _web.CoreWebView2.ExecuteScriptAsync("window.zakoFocus && window.zakoFocus();");
            }
            catch { }
        }

        private void PositionQuick()
        {
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            int width = Math.Min(760, Math.Max(560, area.Width - 48));
            int height = 154;
            int x = area.Left + (area.Width - width) / 2;
            int y = area.Top + area.Height - height - Math.Max(36, area.Height / 9);
            if (_settings.Copilot.QuickPromptX >= 0 && _settings.Copilot.QuickPromptY >= 0)
            {
                Rectangle saved = new Rectangle(_settings.Copilot.QuickPromptX, _settings.Copilot.QuickPromptY, width, height);
                if (area.IntersectsWith(saved))
                {
                    x = Math.Max(area.Left, Math.Min(area.Right - width, saved.X));
                    y = Math.Max(area.Top, Math.Min(area.Bottom - height, saved.Y));
                }
            }
            _shownBounds = new Rectangle(x, y, width, height);
            _hiddenBounds = new Rectangle(x, y + 26, width, height);
        }

        private void OnAnimationTick(object sender, EventArgs e)
        {
            int duration = Math.Max(45, 167 * 100 / Math.Max(60, _settings.Appearance.AnimationSpeedPercent));
            double t = (DateTime.UtcNow - _animationStart).TotalMilliseconds / duration;
            if (t >= 1)
            {
                _animationTimer.Stop();
                if (_targetVisible)
                {
                    Bounds = _shownBounds;
                    Opacity = _settings.Window.OpacityPercent / 100.0;
                    if (!Visible) Show();
                }
                else
                {
                    Hide();
                    MemoryTrimmer.TrimCurrentProcess();
                }
                return;
            }
            double eased = UiDrawing.EaseOutCubic(t);
            Rectangle from = _targetVisible ? _hiddenBounds : _shownBounds;
            Rectangle to = _targetVisible ? _shownBounds : _hiddenBounds;
            Bounds = new Rectangle((int)(from.X + (to.X - from.X) * eased), (int)(from.Y + (to.Y - from.Y) * eased), to.Width, to.Height);
            double maxOpacity = _settings.Window.OpacityPercent / 100.0;
            Opacity = _targetVisible ? 0.08 + (maxOpacity - 0.08) * eased : maxOpacity - (maxOpacity - 0.08) * eased;
        }

        private void ApplyRoundedCorners()
        {
            try
            {
                Region old = Region;
                IntPtr rgn = NativeUi.CreateRoundRectRgn(0, 0, Width + 1, Height + 1, 38, 38);
                Region = Region.FromHrgn(rgn);
                NativeUi.DeleteObject(rgn);
                if (old != null) old.Dispose();
                int preference = NativeUi.DWMWCP_ROUND;
                NativeUi.DwmSetWindowAttribute(Handle, NativeUi.DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
            }
            catch { }
        }

        private Rectangle WebViewBounds()
        {
            int inset = 2;
            return new Rectangle(
                inset,
                inset,
                Math.Max(1, ClientSize.Width - inset * 2),
                Math.Max(1, ClientSize.Height - inset * 2));
        }

        private Dictionary<string, object> Parse(string json)
        {
            try { return _serializer.Deserialize<Dictionary<string, object>>(json) ?? new Dictionary<string, object>(); }
            catch { return new Dictionary<string, object>(); }
        }

        private static string GetString(Dictionary<string, object> dict, string key)
        {
            return dict.ContainsKey(key) && dict[key] != null ? Convert.ToString(dict[key]) : string.Empty;
        }

        private static int GetInt(Dictionary<string, object> dict, string key)
        {
            try { return dict.ContainsKey(key) ? Convert.ToInt32(dict[key]) : 0; }
            catch { return 0; }
        }

        internal static Dictionary<string, object> ThemeObject(ThemePalette theme, AppSettings settings)
        {
            Dictionary<string, object> t = new Dictionary<string, object>();
            t["light"] = theme.IsLight;
            t["accent"] = Hex(theme.Accent);
            t["accentText"] = Hex(theme.AccentText);
            t["text"] = Hex(theme.Text);
            t["subText"] = Hex(theme.SubText);
            t["mutedText"] = Hex(theme.MutedText);
            t["surface"] = Hex(theme.Surface);
            t["surfaceAlt"] = Hex(theme.SurfaceAlt);
            t["windowTop"] = Hex(theme.WindowTop);
            t["windowBottom"] = Hex(theme.WindowBottom);
            t["messageBack"] = Hex(theme.MessageBack);
            t["inputBack"] = Hex(theme.InputBack);
            t["border"] = Hex(theme.Border);
            t["userBubble"] = Hex(theme.UserBubble);
            t["assistantBubble"] = Hex(theme.AssistantBubble);
            t["opacity"] = settings.Window.OpacityPercent;
            return t;
        }

        internal static string Hex(Color color)
        {
            return "#" + color.R.ToString("X2") + color.G.ToString("X2") + color.B.ToString("X2");
        }
    }

    public sealed class WebSidebarForm : Form, ISidebarSurface
    {
        private sealed class BubbleHandle
        {
            public string Id;
            public ChatMessage Message;
        }

        private readonly AppSettings _settings;
        private readonly Action<string> _onSend;
        private readonly Action _onStop;
        private readonly Action _onSettings;
        private readonly Action _onHide;
        private readonly JavaScriptSerializer _serializer;
        private readonly Timer _slideTimer;
        private readonly Timer _webStartTimer;
        private WebView2 _web;
        private ThemePalette _theme;
        private Rectangle _shownBounds;
        private Rectangle _hiddenBounds;
        private DateTime _slideStart;
        private bool _slideTargetVisible;
        private bool _ready;
        private bool _webStartScheduled;
        private bool _busy;
        private bool _imageBusy;
        private string _pendingImagePath;
        private int _messageId;
        private string _providerId;
        private string _provider;
        private string _model;
        private string _memory;
        private string _selectedSessionId;
        private List<BubbleHandle> _messages;
        private List<ChatSession> _sessions;

        public event EventHandler SidebarStateChanged;
        public event EventHandler NewChatRequested;
        public event Action<string> SessionSelected;
        public event Action<string> SessionDeleteRequested;
        public event EventHandler EdgeToggleRequested;
        public event Action<string> ImageGenerationRequested;
        public event Action<string, string> VisionSendRequested;
        public SidebarState State { get; private set; }

        public WebSidebarForm(AppSettings settings, Action<string> onSend, Action onStop, Action onSettings, Action onHide)
        {
            _settings = settings;
            _settings.Normalize();
            _onSend = onSend;
            _onStop = onStop;
            _onSettings = onSettings;
            _onHide = onHide;
            _serializer = new JavaScriptSerializer();
            _theme = ThemePalette.Resolve(_settings);
            _messages = new List<BubbleHandle>();
            _sessions = new List<ChatSession>();
            _providerId = string.Empty;
            _provider = string.Empty;
            _model = string.Empty;
            _memory = string.Empty;
            _selectedSessionId = string.Empty;
            _pendingImagePath = string.Empty;
            State = SidebarState.Hidden;

            Text = AppInfo.AppName;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = _settings.Window.TopMost;
            MinimumSize = new Size(360, 520);
            BackColor = _theme.WindowBottom;
            KeyPreview = true;

            _slideTimer = new Timer();
            _slideTimer.Interval = 15;
            _slideTimer.Tick += OnSlideTick;
            _webStartTimer = new Timer();
            _webStartTimer.Interval = 50;
            _webStartTimer.Tick += delegate
            {
                _webStartTimer.Stop();
                if (!IsDisposed && IsHandleCreated) InitializeWeb();
            };
            Resize += delegate { if (_web != null) _web.Bounds = WebViewBounds(); ApplyRoundedCorners(); };
            ApplySettings();
        }

        public void SetIcon(Icon icon)
        {
            Icon = icon;
        }

        public void ApplySettings()
        {
            _settings.Normalize();
            _theme = ThemePalette.Resolve(_settings);
            TopMost = _settings.Window.TopMost;
            Opacity = _settings.Window.OpacityPercent / 100.0;
            BackColor = _theme.WindowBottom;
            PositionAtEdge(State == SidebarState.Shown);
            PostState();
        }

        public void SetProviderStatus(string providerId, string provider, string model, string memory)
        {
            _providerId = providerId ?? string.Empty;
            _provider = provider ?? string.Empty;
            _model = model ?? string.Empty;
            _memory = memory ?? string.Empty;
            PostState();
        }

        public void SetBusy(bool busy)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<bool>(SetBusy), busy);
                return;
            }
            _busy = busy;
            PostState();
        }

        public void SetImageBusy(bool busy)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<bool>(SetImageBusy), busy);
                return;
            }
            _imageBusy = busy;
            PostState();
        }

        public object AddMessage(ChatMessage message)
        {
            if (InvokeRequired)
                return Invoke(new Func<ChatMessage, object>(AddMessage), message);
            BubbleHandle handle = new BubbleHandle();
            handle.Id = "m" + (++_messageId).ToString();
            handle.Message = message;
            _messages.Add(handle);
            PostState();
            return handle;
        }

        public void UpdateBubble(object bubble, string text, bool append)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<object, string, bool>(UpdateBubble), bubble, text, append);
                return;
            }
            BubbleHandle handle = bubble as BubbleHandle;
            if (handle == null || handle.Message == null) return;
            if (append) handle.Message.Content += text;
            else handle.Message.Content = text;
            PostState();
        }

        public void LoadMessages(IEnumerable<ChatMessage> messages)
        {
            _messages.Clear();
            if (messages != null)
            {
                foreach (ChatMessage message in messages)
                {
                    BubbleHandle handle = new BubbleHandle();
                    handle.Id = "m" + (++_messageId).ToString();
                    handle.Message = message;
                    _messages.Add(handle);
                }
            }
            PostState();
        }

        public void LoadSessions(IList<ChatSession> sessions, string selectedId)
        {
            _sessions.Clear();
            if (sessions != null)
            {
                foreach (ChatSession session in sessions)
                    if (session != null) _sessions.Add(session);
            }
            _selectedSessionId = selectedId ?? string.Empty;
            PostState();
        }

        public void ShowSidebarAnimated()
        {
            if (State == SidebarState.Shown || State == SidebarState.Showing) return;
            PositionAtEdge(false);
            _slideTimer.Stop();
            if (!_settings.Appearance.AnimationEnabled || _settings.Appearance.ReducedMotion)
            {
                Bounds = _shownBounds;
                Opacity = _settings.Window.OpacityPercent / 100.0;
                Show();
                EnsureWebStarted();
                Activate();
                SetState(SidebarState.Shown);
                return;
            }
            Bounds = _hiddenBounds;
            Opacity = 0.08;
            _slideTargetVisible = true;
            _slideStart = DateTime.UtcNow;
            Show();
            EnsureWebStarted();
            SetState(SidebarState.Showing);
            _slideTimer.Start();
            Activate();
        }

        public void HideSidebarAnimated()
        {
            if (State == SidebarState.Hidden || State == SidebarState.Hiding) return;
            PositionAtEdge(false);
            _slideTimer.Stop();
            if (!_settings.Appearance.AnimationEnabled || _settings.Appearance.ReducedMotion)
            {
                Hide();
                SetState(SidebarState.Hidden);
                return;
            }
            _slideTargetVisible = false;
            _slideStart = DateTime.UtcNow;
            SetState(SidebarState.Hiding);
            _slideTimer.Start();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyRoundedCorners();
            if (_webStartScheduled && _web == null)
                QueueWebStart();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_slideTimer != null) _slideTimer.Dispose();
                if (_webStartTimer != null) _webStartTimer.Dispose();
                if (_web != null) _web.Dispose();
            }
            base.Dispose(disposing);
        }

        private void EnsureWebStarted()
        {
            if (_webStartScheduled || _web != null) return;
            _webStartScheduled = true;
            QueueWebStart();
        }

        private void QueueWebStart()
        {
            if (!IsHandleCreated || IsDisposed) return;
            try
            {
                BeginInvoke(new MethodInvoker(delegate
                {
                    if (!IsDisposed && _web == null) InitializeWeb();
                }));
            }
            catch
            {
                _webStartTimer.Start();
            }
        }

        private async void InitializeWeb()
        {
            try
            {
                _web = new WebView2();
                _web.Dock = DockStyle.None;
                _web.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
                _web.Bounds = WebViewBounds();
                _web.AllowExternalDrop = false;
                _web.DefaultBackgroundColor = _theme.WindowBottom;
                Controls.Add(_web);
                _web.CoreWebView2InitializationCompleted += OnWebInitialized;
                Directory.CreateDirectory(AppInfo.WebView2UserDataDir);
                CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(
                    null,
                    AppInfo.WebView2UserDataDir,
                    null);
                await _web.EnsureCoreWebView2Async(environment);
            }
            catch (Exception ex)
            {
                CrashLog.Write(ex);
                if (_web != null)
                {
                    Controls.Remove(_web);
                    _web.Dispose();
                    _web = null;
                }
            }
        }

        private void OnWebInitialized(object sender, CoreWebView2InitializationCompletedEventArgs e)
        {
            if (!e.IsSuccess || _web == null || _web.CoreWebView2 == null)
            {
                CrashLog.Write(e.InitializationException ?? new InvalidOperationException("WebView2 完整侧栏界面初始化失败。"));
                return;
            }
            _web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            _web.CoreWebView2.Settings.AreDevToolsEnabled = false;
            _web.CoreWebView2.WebMessageReceived += OnWebMessage;
            _web.NavigateToString(WebSafeTemplates.BuildSidebarHtml());
        }

        private void OnWebMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            Dictionary<string, object> msg = Parse(e.WebMessageAsJson);
            string action = GetString(msg, "action");
            if (action == "ready")
            {
                _ready = true;
                PostState();
            }
            else if (action == "send")
            {
                string text = GetString(msg, "text").Trim();
                if (text.Length > 0 && _pendingImagePath.Length > 0 && VisionSendRequested != null)
                {
                    string image = _pendingImagePath;
                    _pendingImagePath = string.Empty;
                    VisionSendRequested(text, image);
                    PostState();
                }
                else if (text.Length > 0 && _onSend != null)
                {
                    _onSend(text);
                }
            }
            else if (action == "stop")
            {
                if (_onStop != null) _onStop();
            }
            else if (action == "settings")
            {
                if (_onSettings != null) _onSettings();
            }
            else if (action == "hide")
            {
                if (_onHide != null) _onHide();
            }
            else if (action == "newChat")
            {
                if (NewChatRequested != null) NewChatRequested(this, EventArgs.Empty);
            }
            else if (action == "selectSession")
            {
                string id = GetString(msg, "id");
                if (id.Length > 0 && SessionSelected != null) SessionSelected(id);
            }
            else if (action == "deleteSession")
            {
                string id = GetString(msg, "id");
                if (id.Length > 0 && SessionDeleteRequested != null) SessionDeleteRequested(id);
            }
            else if (action == "toggleEdge")
            {
                if (EdgeToggleRequested != null) EdgeToggleRequested(this, EventArgs.Empty);
            }
            else if (action == "generateImage")
            {
                string prompt = GetString(msg, "text").Trim();
                if (prompt.Length > 0 && ImageGenerationRequested != null) ImageGenerationRequested(prompt);
            }
            else if (action == "uploadImage")
            {
                SelectPendingImage();
            }
            else if (action == "removeAttachment")
            {
                _pendingImagePath = string.Empty;
                PostState();
            }
        }

        private void SelectPendingImage()
        {
            try
            {
                using (OpenFileDialog dialog = new OpenFileDialog())
                {
                    dialog.Title = "选择要发送给视觉模型的图片";
                    dialog.Filter = "图片文件|*.png;*.jpg;*.jpeg;*.webp;*.gif;*.bmp|所有文件|*.*";
                    dialog.Multiselect = false;
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        long maxBytes = Math.Max(1, _settings.Chat.MaxUploadImageMb) * 1024L * 1024L;
                        FileInfo info = new FileInfo(dialog.FileName);
                        if (info.Exists && info.Length <= maxBytes)
                            _pendingImagePath = dialog.FileName;
                        else
                            MessageBox.Show("图片超过 " + _settings.Chat.MaxUploadImageMb + " MB，请在设置中调整限制或选择更小的图片。", AppInfo.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        PostState();
                    }
                }
            }
            catch (Exception ex)
            {
                CrashLog.Write(ex);
            }
        }

        private void PostState()
        {
            if (!_ready || _web == null || _web.CoreWebView2 == null) return;
            Dictionary<string, object> state = new Dictionary<string, object>();
            state["type"] = "sidebarState";
            state["appName"] = AppInfo.AppName;
            state["theme"] = WebQuickPromptForm.ThemeObject(_theme, _settings);
            state["providerId"] = _providerId;
            state["provider"] = string.IsNullOrEmpty(_provider) ? "未配置服务商" : _provider;
            state["model"] = string.IsNullOrEmpty(_model) ? "未选择模型" : _model;
            state["memory"] = _memory ?? string.Empty;
            state["busy"] = _busy;
            state["imageBusy"] = _imageBusy;
            state["edge"] = _settings.Window.Edge == SidebarEdge.Right ? "right" : "left";
            state["pendingImagePath"] = _pendingImagePath;
            state["pendingImageName"] = string.IsNullOrEmpty(_pendingImagePath) ? string.Empty : Path.GetFileName(_pendingImagePath);
            state["messages"] = MessageObjects();
            state["sessions"] = SessionObjects();
            state["selectedSessionId"] = _selectedSessionId;
            _web.CoreWebView2.PostWebMessageAsJson(_serializer.Serialize(state));
        }

        private List<object> MessageObjects()
        {
            List<object> list = new List<object>();
            foreach (BubbleHandle handle in _messages)
            {
                Dictionary<string, object> m = new Dictionary<string, object>();
                m["id"] = handle.Id;
                m["role"] = handle.Message == null ? "assistant" : handle.Message.Role;
                m["content"] = handle.Message == null ? string.Empty : (handle.Message.Content ?? string.Empty);
                m["messageType"] = handle.Message == null ? "text" : (handle.Message.MessageType ?? "text");
                m["imagePath"] = handle.Message == null ? string.Empty : (handle.Message.ImagePath ?? string.Empty);
                m["imagePrompt"] = handle.Message == null ? string.Empty : (handle.Message.ImagePrompt ?? string.Empty);
                m["imageModelId"] = handle.Message == null ? string.Empty : (handle.Message.ImageModelId ?? string.Empty);
                m["imageSize"] = handle.Message == null ? string.Empty : (handle.Message.ImageSize ?? string.Empty);
                m["attachmentPath"] = handle.Message == null ? string.Empty : (handle.Message.AttachmentPath ?? string.Empty);
                m["attachmentName"] = handle.Message == null ? string.Empty : (handle.Message.AttachmentName ?? string.Empty);
                list.Add(m);
            }
            return list;
        }

        private List<object> SessionObjects()
        {
            List<object> list = new List<object>();
            foreach (ChatSession session in _sessions)
            {
                Dictionary<string, object> s = new Dictionary<string, object>();
                s["id"] = session.Id ?? string.Empty;
                s["title"] = string.IsNullOrEmpty(session.Title) ? "新对话" : session.Title;
                s["updatedAt"] = session.UpdatedAt.ToLocalTime().ToString("MM-dd HH:mm");
                list.Add(s);
            }
            return list;
        }

        private void OnSlideTick(object sender, EventArgs e)
        {
            int duration = Math.Max(45, 167 * 100 / Math.Max(60, _settings.Appearance.AnimationSpeedPercent));
            double t = (DateTime.UtcNow - _slideStart).TotalMilliseconds / duration;
            if (t >= 1)
            {
                _slideTimer.Stop();
                if (_slideTargetVisible)
                {
                    Bounds = _shownBounds;
                    Opacity = _settings.Window.OpacityPercent / 100.0;
                    SetState(SidebarState.Shown);
                }
                else
                {
                    Bounds = _hiddenBounds;
                    Hide();
                    SetState(SidebarState.Hidden);
                    MemoryTrimmer.TrimCurrentProcess();
                }
                return;
            }
            double eased = UiDrawing.EaseOutCubic(t);
            Rectangle from = _slideTargetVisible ? _hiddenBounds : _shownBounds;
            Rectangle to = _slideTargetVisible ? _shownBounds : _hiddenBounds;
            Bounds = new Rectangle((int)(from.X + (to.X - from.X) * eased), to.Y, to.Width, to.Height);
            double maxOpacity = _settings.Window.OpacityPercent / 100.0;
            Opacity = _slideTargetVisible ? 0.08 + (maxOpacity - 0.08) * eased : maxOpacity - (maxOpacity - 0.08) * eased;
        }

        private void SetState(SidebarState state)
        {
            if (State == state) return;
            State = state;
            if (SidebarStateChanged != null) SidebarStateChanged(this, EventArgs.Empty);
        }

        private void PositionAtEdge(bool apply)
        {
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            int width = Math.Min(_settings.Window.Width, Math.Max(360, area.Width - 24));
            _shownBounds = new Rectangle(
                _settings.Window.Edge == SidebarEdge.Right ? area.Right - width - 8 : area.Left + 8,
                area.Top + 8,
                width,
                area.Height - 16);
            _hiddenBounds = new Rectangle(
                _settings.Window.Edge == SidebarEdge.Right ? area.Right - 10 : area.Left - width + 10,
                _shownBounds.Y,
                width,
                _shownBounds.Height);
            if (apply) Bounds = _shownBounds;
        }

        private void ApplyRoundedCorners()
        {
            try
            {
                Region old = Region;
                IntPtr rgn = NativeUi.CreateRoundRectRgn(0, 0, Width + 1, Height + 1, 32, 32);
                Region = Region.FromHrgn(rgn);
                NativeUi.DeleteObject(rgn);
                if (old != null) old.Dispose();
                int preference = NativeUi.DWMWCP_ROUND;
                NativeUi.DwmSetWindowAttribute(Handle, NativeUi.DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
            }
            catch { }
        }

        private Rectangle WebViewBounds()
        {
            int inset = 2;
            return new Rectangle(
                inset,
                inset,
                Math.Max(1, ClientSize.Width - inset * 2),
                Math.Max(1, ClientSize.Height - inset * 2));
        }

        private Dictionary<string, object> Parse(string json)
        {
            try { return _serializer.Deserialize<Dictionary<string, object>>(json) ?? new Dictionary<string, object>(); }
            catch { return new Dictionary<string, object>(); }
        }

        private static string GetString(Dictionary<string, object> dict, string key)
        {
            return dict.ContainsKey(key) && dict[key] != null ? Convert.ToString(dict[key]) : string.Empty;
        }
    }

    internal static class WebCopilotHtml
    {
        private const string SidebarPatchCss2 = "";

        public static string BuildQuickHtml()
        {
            return @"<!doctype html><html><head><meta charset=""utf-8""><style>" + SharedCss2 + QuickCss2 + @"</style></head><body><main class=""quick-shell"" id=""drag""><header><div class=""brand""><span class=""zako-mark""></span><div><b id=""app"">Zako Chat</b><small id=""status""></small></div></div><nav><button title=""展开侧栏"" data-action=""expand"">" + UiIcons.Expand + @"</button><button title=""设置"" data-action=""settings"">" + UiIcons.Settings + @"</button><button title=""隐藏"" data-action=""hide"">" + UiIcons.Close + @"</button></nav></header><section class=""composer""><textarea id=""input"" placeholder=""向 Zako 提问...""></textarea><button class=""send"" id=""send"" title=""发送"">" + UiIcons.Send + @"</button></section></main><script>" + QuickScript2 + @"</script></body></html>";
        }

        public static string BuildSidebarHtml()
        {
            return @"<!doctype html><html><head><meta charset=""utf-8""><style>" + SharedCss2 + SidebarCss2 + SidebarPatchCss2 + @"</style></head><body><div class=""app""><aside><div class=""side-title""><span class=""zako-mark small""></span><div><b>Zako</b><small>历史记录</small></div></div><button class=""new"" id=""newChat"">" + UiIcons.Add + @"<span>新建对话</span></button><div class=""sessions"" id=""sessions""></div></aside><main><header><div class=""head-copy""><h1 id=""appName"">Zako Chat</h1><p id=""status""></p></div><div class=""mode-switch"" role=""tablist""><button id=""modeText"" class=""active"">文字对话</button><button id=""modeImage"">图片生成</button></div><div class=""tools""><button id=""toggleEdge"" title=""切换左右侧"">" + UiIcons.Side + @"</button><button id=""settings"" title=""设置"">" + UiIcons.Settings + @"</button><button id=""quickClose"" class=""close-fast"" title=""快捷关闭"">" + UiIcons.Close + @"</button></div></header><section class=""messages"" id=""messages""></section><footer><div class=""attachment"" id=""attachment""></div><textarea id=""input"" placeholder=""输入消息，Ctrl+Enter 发送""></textarea><button id=""upload"" title=""上传图片给视觉模型"">" + UiIcons.Image + @"</button><button id=""stop"" title=""停止"">" + UiIcons.Stop + @"</button><button id=""send"" class=""send"" title=""发送"">" + UiIcons.Send + @"</button></footer></main></div><script>" + SidebarScript2 + @"</script></body></html>";
        }

        private const string SharedCss2 = @"*{box-sizing:border-box}html,body{margin:0;width:100%;height:100%;overflow:hidden}body{font-family:'Segoe UI Variable Text','Segoe UI Variable','Segoe UI','Microsoft YaHei UI',sans-serif;color:var(--text);background:var(--windowBottom);--glass:rgba(255,255,255,.72);--glass2:rgba(255,255,255,.48);--line:rgba(0,0,0,.09);--hover:rgba(0,0,0,.055);--press:rgba(0,0,0,.09);--shadow:rgba(0,0,0,.18);--prompt:rgba(255,255,255,.84)}body[data-theme='dark']{--glass:rgba(42,45,54,.86);--glass2:rgba(34,37,45,.72);--line:rgba(255,255,255,.11);--hover:rgba(255,255,255,.09);--press:rgba(255,255,255,.14);--shadow:rgba(0,0,0,.42);--prompt:rgba(48,51,60,.9)}button,textarea{font:inherit}button{border:0;color:var(--text);cursor:pointer;background:transparent}.ui-icon{width:19px;height:19px;display:block;overflow:visible;flex:0 0 auto}.zako-mark{width:30px;height:30px;border-radius:12px;background:conic-gradient(from 215deg,var(--accent),#72f0cf,#8aa8ff,var(--accent));box-shadow:inset 0 1px 0 rgba(255,255,255,.65),0 0 24px rgba(90,160,255,.24)}.zako-mark.small{width:26px;height:26px;border-radius:10px}nav button,.tools button{width:36px;height:36px;display:grid;place-items:center;border-radius:12px;border:1px solid transparent;transition:background .12s ease,border-color .12s ease,transform .12s ease}nav button:hover,.tools button:hover{background:var(--hover);border-color:var(--line)}nav button:active,.tools button:active{background:var(--press);transform:scale(.96)}textarea{resize:none;outline:none;color:var(--text);background:transparent;border:0}textarea::placeholder{color:var(--muted)}";

        private const string QuickCss2 = @".quick-shell{width:100vw;height:100vh;padding:13px 14px 14px;border:1px solid var(--line);border-radius:28px;background:linear-gradient(145deg,var(--glass),var(--glass2));box-shadow:inset 0 1px 0 rgba(255,255,255,.2),0 22px 52px var(--shadow);backdrop-filter:blur(30px) saturate(150%)}header{height:38px;display:flex;align-items:center;justify-content:space-between;user-select:none}header,.brand{display:flex;align-items:center;gap:11px}.brand b{display:block;font-size:14px;font-weight:650;letter-spacing:0}.brand small{display:block;max-width:480px;font-size:11px;color:var(--sub);white-space:nowrap;overflow:hidden;text-overflow:ellipsis}nav{display:flex;gap:6px}.composer{height:76px;margin-top:10px;padding:12px 12px 12px 16px;border-radius:24px;background:var(--prompt);border:1px solid var(--line);display:grid;grid-template-columns:1fr 40px;gap:12px;box-shadow:inset 0 1px 0 rgba(255,255,255,.18)}.composer:focus-within{border-color:var(--accent);box-shadow:0 0 0 3px rgba(96,165,250,.16),inset 0 1px 0 rgba(255,255,255,.18)}#input{height:50px;line-height:25px;font-size:15px}.send{width:40px;height:40px;border-radius:15px;align-self:end;display:grid;place-items:center;background:var(--accent);color:var(--accentText);box-shadow:0 8px 22px rgba(70,120,240,.22);transition:transform .12s ease,filter .12s ease}.send:hover{filter:brightness(1.06)}.send:active{transform:scale(.96)}.send:disabled{opacity:.5}";

        private const string SidebarCss2 = @".app{height:100vh;display:grid;grid-template-columns:204px 1fr;background:linear-gradient(145deg,var(--windowTop),var(--windowBottom));border:1px solid var(--line);border-radius:22px;overflow:hidden;box-shadow:inset 0 1px 0 rgba(255,255,255,.16)}aside{padding:16px 10px;border-right:1px solid var(--line);background:var(--glass2);backdrop-filter:blur(24px) saturate(140%)}.side-title{height:38px;display:flex;align-items:center;gap:10px;margin:0 6px 14px}.side-title b{font-size:14px}.side-title small{display:block;color:var(--sub);font-size:11px}.new{width:100%;height:38px;border-radius:12px;background:var(--hover);display:flex;align-items:center;gap:10px;padding:0 12px;border:1px solid var(--line)}.new:hover{background:var(--press)}.sessions{margin-top:12px;height:calc(100% - 68px);overflow:auto}.session{position:relative;width:100%;min-height:48px;margin:4px 0;padding:8px 36px 8px 10px;border-radius:13px;color:var(--sub);cursor:pointer;transition:background .12s ease,color .12s ease}.session:hover{background:var(--hover);color:var(--text)}.session.active{background:rgba(96,165,250,.18);color:var(--text)}.session b{display:block;font-size:12px;font-weight:520;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}.session small{font-size:10px;color:var(--muted)}.del{position:absolute;right:6px;top:7px;width:30px;height:30px;display:grid;place-items:center;border-radius:10px;opacity:0}.session:hover .del{opacity:1}.del:hover{background:var(--press)}main{min-width:0;display:grid;grid-template-rows:70px 1fr 118px}header{padding:0 18px;display:flex;align-items:center;justify-content:space-between;border-bottom:1px solid var(--line);background:var(--glass)}.head-copy{min-width:0}h1{margin:0;font-size:18px;font-weight:650}p{margin:3px 0 0;color:var(--sub);font-size:12px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}.tools{display:flex;gap:8px}.messages{padding:18px;overflow:auto;background:rgba(0,0,0,.018)}body[data-theme='dark'] .messages{background:rgba(0,0,0,.08)}.msg{max-width:92%;margin:0 0 12px;padding:12px 14px;border-radius:18px;line-height:1.55;font-size:13.5px;white-space:pre-wrap;word-break:break-word;border:1px solid var(--line);box-shadow:inset 0 1px 0 rgba(255,255,255,.08);animation:rise .083s ease-out both}.msg.user{margin-left:auto;background:var(--accent);color:var(--accentText);border-color:transparent}.msg.assistant{background:var(--prompt)}.image-card{white-space:normal}.image-card img{display:block;max-width:100%;max-height:360px;border-radius:14px;margin-top:10px;border:1px solid var(--line);object-fit:contain;background:#111}.image-meta{margin-top:8px;font-size:11px;color:var(--muted)}.empty{height:100%;display:grid;place-items:center;color:var(--muted);text-align:center}footer{padding:14px 18px;background:var(--glass);border-top:1px solid var(--line);display:grid;grid-template-columns:1fr 38px 38px 38px;gap:10px}footer textarea{height:90px;padding:13px 14px;border-radius:18px;background:var(--prompt);border:1px solid var(--line)}footer textarea:focus{border-color:var(--accent);box-shadow:0 0 0 3px rgba(96,165,250,.16)}footer button{width:38px;height:38px;align-self:end;border-radius:13px;background:var(--hover);display:grid;place-items:center}footer button:hover{background:var(--press)}footer .send{background:var(--accent);color:var(--accentText)}@keyframes rise{from{opacity:.35;transform:translateY(6px)}to{opacity:1;transform:none}}";

        private static readonly string QuickScript2 = @"const $=s=>document.querySelector(s);let state=null;function post(m){chrome.webview.postMessage(m)}function css(t){document.body.dataset.theme=t.light?'light':'dark';const r=document.documentElement.style;r.setProperty('--accent',t.accent);r.setProperty('--accentText',t.accentText);r.setProperty('--text',t.text);r.setProperty('--sub',t.subText);r.setProperty('--muted',t.mutedText);r.setProperty('--windowTop',t.windowTop);r.setProperty('--windowBottom',t.windowBottom)}function render(s){state=s;css(s.theme);$('#app').textContent=s.appName;$('#status').textContent=(s.busy?'正在生成回复':'快速提问')+' · '+s.provider+' · '+s.model+(s.memory?' · '+s.memory:'');$('#send').disabled=!!s.busy}function send(){const i=$('#input');const v=i.value.trim();if(!v)return;post({action:'send',text:v});i.value=''}window.zakoFocus=()=>$('#input').focus();$('#send').onclick=send;$('#input').addEventListener('keydown',e=>{if(e.key==='Enter'&&(e.ctrlKey||(!e.shiftKey&&!e.altKey))){e.preventDefault();send()}if(e.key==='Escape')post({action:'hide'})});document.querySelectorAll('[data-action]').forEach(b=>b.onclick=()=>post({action:b.dataset.action}));let drag=false;$('#drag').addEventListener('pointerdown',e=>{if(e.target.closest('button,textarea'))return;drag=true;post({action:'dragStart',screenX:Math.round(e.screenX),screenY:Math.round(e.screenY)})});addEventListener('pointermove',e=>{if(drag)post({action:'dragMove',screenX:Math.round(e.screenX),screenY:Math.round(e.screenY)})});addEventListener('pointerup',()=>{if(drag){drag=false;post({action:'dragEnd'})}});chrome.webview.addEventListener('message',e=>{if(e.data.type==='quickState')render(e.data)});post({action:'ready'});";

        private static readonly string SidebarScript2 = @"const $=s=>document.querySelector(s);function post(m){chrome.webview.postMessage(m)}function esc(s){return String(s||'').replace(/[&<>]/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;'}[c]))}function css(t){document.body.dataset.theme=t.light?'light':'dark';const r=document.documentElement.style;r.setProperty('--accent',t.accent);r.setProperty('--accentText',t.accentText);r.setProperty('--text',t.text);r.setProperty('--sub',t.subText);r.setProperty('--muted',t.mutedText);r.setProperty('--windowTop',t.windowTop);r.setProperty('--windowBottom',t.windowBottom)}function imgPath(p){return p?('file:///'+String(p).replace(/\\/g,'/')):''}function msg(m){if(m.messageType==='image'&&m.imagePath){return `<div class=""msg ${m.role==='user'?'user':'assistant'} image-card""><div>${esc(m.content)||'图片已生成'}</div><img src=""${esc(imgPath(m.imagePath))}""><div class=""image-meta"">${esc(m.imageModelId||'图片模型')} · ${esc(m.imageSize||'默认尺寸')}</div></div>`}return `<div class=""msg ${m.role==='user'?'user':'assistant'}"">${esc(m.content)||'…'}</div>`}function render(s){css(s.theme);$('#appName').textContent=s.appName;$('#status').textContent=(s.busy?'正在生成回复':(s.imageBusy?'正在生成图片':'已就绪'))+' · '+s.provider+' · '+s.model+(s.memory?' · '+s.memory:'');$('#toggleEdge').title=s.edge==='right'?'切换到左侧':'切换到右侧';$('#sessions').innerHTML=(s.sessions||[]).map(x=>`<div class=""session ${x.id===s.selectedSessionId?'active':''}"" data-id=""${esc(x.id)}""><b>${esc(x.title)}</b><small>${esc(x.updatedAt)}</small><button class=""del"" data-id=""${esc(x.id)}"" title=""删除"">" + UiIcons.Clear.Replace("\r", "").Replace("\n", "") + @"</button></div>`).join('');$('#messages').innerHTML=(s.messages||[]).length?(s.messages||[]).map(msg).join(''):'<div class=""empty"">开始一次新的 Zako 对话</div>';$('#send').disabled=!!s.busy;$('#image').disabled=!!s.busy||!!s.imageBusy;$('#stop').disabled=!s.busy&&!s.imageBusy;$('#messages').scrollTop=$('#messages').scrollHeight}function send(){const i=$('#input');const v=i.value.trim();if(!v)return;post({action:'send',text:v});i.value=''}function image(){const i=$('#input');const v=i.value.trim();if(!v)return;post({action:'generateImage',text:v});i.value=''}$('#send').onclick=send;$('#image').onclick=image;$('#stop').onclick=()=>post({action:'stop'});$('#settings').onclick=()=>post({action:'settings'});$('#hide').onclick=()=>post({action:'hide'});$('#toggleEdge').onclick=()=>post({action:'toggleEdge'});$('#newChat').onclick=()=>post({action:'newChat'});$('#sessions').onclick=e=>{const del=e.target.closest('.del');if(del){e.stopPropagation();post({action:'deleteSession',id:del.dataset.id});return}const s=e.target.closest('.session');if(s)post({action:'selectSession',id:s.dataset.id})};$('#input').addEventListener('keydown',e=>{if(e.key==='Enter'&&e.ctrlKey){e.preventDefault();send()}});chrome.webview.addEventListener('message',e=>{if(e.data.type==='sidebarState')render(e.data)});post({action:'ready'});";

        public const string QuickHtml = @"<!doctype html><html><head><meta charset=""utf-8""><style>" + SharedCss + QuickCss + @"</style></head><body class=""quick""><main class=""quick-shell"" id=""drag""><header><div class=""brand""><span class=""zako-mark""></span><div><b id=""app"">Zako Chat</b><small id=""status""></small></div></div><nav><button title=""展开"" data-action=""expand"">" + IconExpand + @"</button><button title=""设置"" data-action=""settings"">" + IconSettings + @"</button><button title=""隐藏"" data-action=""hide"">" + IconClose + @"</button></nav></header><section class=""composer""><textarea id=""input"" placeholder=""向 Zako 提问...""></textarea><button class=""send"" id=""send"" title=""发送"">" + IconSend + @"</button></section></main><script>" + QuickScript + @"</script></body></html>";

        public const string SidebarHtml = @"<!doctype html><html><head><meta charset=""utf-8""><style>" + SharedCss + SidebarCss + @"</style></head><body class=""sidebar""><div class=""app""><aside><div class=""side-title""><span class=""zako-mark small""></span><div><b>Zako</b><small>历史记录</small></div></div><button class=""new"" id=""newChat"">" + IconNew + @"<span>新建对话</span></button><div class=""sessions"" id=""sessions""></div></aside><main><header><div><h1 id=""appName"">Zako Chat</h1><p id=""status""></p></div><div class=""tools""><button id=""settings"" title=""设置"">" + IconSettings + @"</button><button id=""hide"" title=""隐藏"">" + IconClose + @"</button></div></header><section class=""messages"" id=""messages""></section><footer><textarea id=""input"" placeholder=""输入消息，Ctrl+Enter 发送""></textarea><button id=""stop"" title=""停止"">" + IconStop + @"</button><button id=""send"" class=""send"" title=""发送"">" + IconSend + @"</button></footer></main></div><script>" + SidebarScript + @"</script></body></html>";

        private const string SharedCss = @"
*{box-sizing:border-box}html,body{margin:0;width:100%;height:100%;overflow:hidden}body{font-family:'Segoe UI Variable Text','Segoe UI Variable','Segoe UI','Microsoft YaHei UI',sans-serif;color:var(--text);background:transparent;--glass:rgba(255,255,255,.66);--glass2:rgba(255,255,255,.42);--line:rgba(0,0,0,.09);--hover:rgba(0,0,0,.055);--press:rgba(0,0,0,.09);--shadow:rgba(0,0,0,.18);--prompt:rgba(255,255,255,.78)}body[data-theme='dark']{--glass:rgba(42,45,54,.72);--glass2:rgba(34,37,45,.56);--line:rgba(255,255,255,.105);--hover:rgba(255,255,255,.09);--press:rgba(255,255,255,.13);--shadow:rgba(0,0,0,.38);--prompt:rgba(48,51,60,.78)}button,textarea{font:inherit}button{border:0;color:var(--text);cursor:pointer}.ui-icon{width:18px;height:18px;display:block}.zako-mark{width:30px;height:30px;border-radius:12px;background:conic-gradient(from 215deg,var(--accent),#72f0cf,#8aa8ff,var(--accent));box-shadow:inset 0 1px 0 rgba(255,255,255,.65),0 0 24px rgba(90,160,255,.24)}.zako-mark.small{width:26px;height:26px;border-radius:10px}nav button,.tools button{width:34px;height:34px;display:grid;place-items:center;border-radius:12px;background:transparent;border:1px solid transparent;transition:background .12s ease,border-color .12s ease,transform .12s ease}nav button:hover,.tools button:hover{background:var(--hover);border-color:var(--line)}nav button:active,.tools button:active{background:var(--press);transform:scale(.96)}textarea{resize:none;outline:none;color:var(--text);background:transparent;border:0}textarea::placeholder{color:var(--muted)}";

        private const string QuickCss = @"
.quick-shell{width:100vw;height:100vh;padding:13px 14px 14px;border:1px solid var(--line);border-radius:26px;background:linear-gradient(145deg,var(--glass),var(--glass2));box-shadow:inset 0 1px 0 rgba(255,255,255,.2),0 22px 52px var(--shadow);backdrop-filter:blur(30px) saturate(150%)}header{height:38px;display:flex;align-items:center;justify-content:space-between;user-select:none}header,.brand{display:flex;align-items:center;gap:11px}.brand b{display:block;font-size:14px;font-weight:650;letter-spacing:0}.brand small{display:block;max-width:480px;font-size:11px;color:var(--sub);white-space:nowrap;overflow:hidden;text-overflow:ellipsis}nav{display:flex;gap:6px}.composer{height:76px;margin-top:10px;padding:12px 12px 12px 16px;border-radius:24px;background:var(--prompt);border:1px solid var(--line);display:grid;grid-template-columns:1fr 40px;gap:12px;box-shadow:inset 0 1px 0 rgba(255,255,255,.18)}.composer:focus-within{border-color:var(--accent);box-shadow:0 0 0 3px rgba(96,165,250,.16),inset 0 1px 0 rgba(255,255,255,.18)}#input{height:50px;line-height:25px;font-size:15px}.send{width:40px;height:40px;border-radius:15px;align-self:end;display:grid;place-items:center;background:var(--accent);color:var(--accentText);box-shadow:0 8px 22px rgba(70,120,240,.22);transition:transform .12s ease,filter .12s ease}.send:hover{filter:brightness(1.06)}.send:active{transform:scale(.96)}.send:disabled{opacity:.5}";

        private const string SidebarCss = @"
.app{height:100vh;display:grid;grid-template-columns:196px 1fr;background:linear-gradient(145deg,var(--windowTop),var(--windowBottom));border:1px solid var(--line);border-radius:20px;overflow:hidden;box-shadow:inset 0 1px 0 rgba(255,255,255,.16)}aside{padding:16px 10px;border-right:1px solid var(--line);background:var(--glass2);backdrop-filter:blur(24px) saturate(140%)}.side-title{height:38px;display:flex;align-items:center;gap:10px;margin:0 6px 14px}.side-title b{font-size:14px}.side-title small{display:block;color:var(--sub);font-size:11px}.new{width:100%;height:38px;border-radius:12px;background:var(--hover);display:flex;align-items:center;gap:10px;padding:0 12px;border:1px solid var(--line)}.new:hover{background:var(--press)}.sessions{margin-top:12px;height:calc(100% - 68px);overflow:auto}.session{width:100%;min-height:44px;margin:3px 0;padding:7px 9px;border-radius:12px;color:var(--sub);cursor:pointer;transition:background .12s ease,color .12s ease}.session:hover{background:var(--hover);color:var(--text)}.session.active{background:rgba(96,165,250,.18);color:var(--text)}.session b{display:block;font-size:12px;font-weight:520;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}.session small{font-size:10px;color:var(--muted)}main{min-width:0;display:grid;grid-template-rows:70px 1fr 118px}header{padding:0 18px;display:flex;align-items:center;justify-content:space-between;border-bottom:1px solid var(--line);background:var(--glass)}h1{margin:0;font-size:18px;font-weight:650}p{margin:3px 0 0;color:var(--sub);font-size:12px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}.tools{display:flex;gap:8px}.messages{padding:18px;overflow:auto;background:rgba(0,0,0,.018)}body[data-theme='dark'] .messages{background:rgba(0,0,0,.08)}.msg{max-width:90%;margin:0 0 12px;padding:12px 14px;border-radius:18px;line-height:1.55;font-size:13.5px;white-space:pre-wrap;word-break:break-word;border:1px solid var(--line);box-shadow:inset 0 1px 0 rgba(255,255,255,.08);animation:rise .083s ease-out both}.msg.user{margin-left:auto;background:var(--accent);color:var(--accentText);border-color:transparent}.msg.assistant{background:var(--prompt)}.empty{height:100%;display:grid;place-items:center;color:var(--muted);text-align:center}footer{padding:14px 18px;background:var(--glass);border-top:1px solid var(--line);display:grid;grid-template-columns:1fr 38px 38px;gap:10px}footer textarea{height:90px;padding:13px 14px;border-radius:18px;background:var(--prompt);border:1px solid var(--line)}footer textarea:focus{border-color:var(--accent);box-shadow:0 0 0 3px rgba(96,165,250,.16)}footer button{width:38px;height:38px;align-self:end;border-radius:13px;background:var(--hover);display:grid;place-items:center}footer button:hover{background:var(--press)}footer .send{background:var(--accent);color:var(--accentText)}@keyframes rise{from{opacity:.35;transform:translateY(6px)}to{opacity:1;transform:none}}";

        private const string QuickScript = @"
const $=s=>document.querySelector(s);let state=null;function post(m){chrome.webview.postMessage(m)}function css(t){document.body.dataset.theme=t.light?'light':'dark';const r=document.documentElement.style;r.setProperty('--accent',t.accent);r.setProperty('--accentText',t.accentText);r.setProperty('--text',t.text);r.setProperty('--sub',t.subText);r.setProperty('--muted',t.mutedText);r.setProperty('--windowTop',t.windowTop);r.setProperty('--windowBottom',t.windowBottom)}function render(s){state=s;css(s.theme);$('#app').textContent=s.appName;$('#status').textContent=(s.busy?'正在生成回复':'快速提问')+' · '+s.provider+' · '+s.model+(s.memory?' · '+s.memory:'');$('#send').disabled=!!s.busy}function send(){const i=$('#input');const v=i.value.trim();if(!v)return;post({action:'send',text:v});i.value=''}window.zakoFocus=()=>$('#input').focus();$('#send').onclick=send;$('#input').addEventListener('keydown',e=>{if(e.key==='Enter'&&(e.ctrlKey||(!e.shiftKey&&!e.altKey))){e.preventDefault();send()}if(e.key==='Escape')post({action:'hide'})});document.querySelectorAll('[data-action]').forEach(b=>b.onclick=()=>post({action:b.dataset.action}));let drag=false;$('#drag').addEventListener('pointerdown',e=>{if(e.target.closest('button,textarea'))return;drag=true;post({action:'dragStart',screenX:Math.round(e.screenX),screenY:Math.round(e.screenY)})});addEventListener('pointermove',e=>{if(drag)post({action:'dragMove',screenX:Math.round(e.screenX),screenY:Math.round(e.screenY)})});addEventListener('pointerup',()=>{if(drag){drag=false;post({action:'dragEnd'})}});chrome.webview.addEventListener('message',e=>{if(e.data.type==='quickState')render(e.data)});post({action:'ready'});";

        private const string SidebarScript = @"
const $=s=>document.querySelector(s);function post(m){chrome.webview.postMessage(m)}function esc(s){return String(s||'').replace(/[&<>]/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;'}[c]))}function css(t){document.body.dataset.theme=t.light?'light':'dark';const r=document.documentElement.style;r.setProperty('--accent',t.accent);r.setProperty('--accentText',t.accentText);r.setProperty('--text',t.text);r.setProperty('--sub',t.subText);r.setProperty('--muted',t.mutedText);r.setProperty('--windowTop',t.windowTop);r.setProperty('--windowBottom',t.windowBottom)}function render(s){css(s.theme);$('#appName').textContent=s.appName;$('#status').textContent=(s.busy?'正在生成回复':'已就绪')+' · '+s.provider+' · '+s.model+(s.memory?' · '+s.memory:'');$('#sessions').innerHTML=(s.sessions||[]).map(x=>`<div class=""session ${x.id===s.selectedSessionId?'active':''}"" data-id=""${esc(x.id)}""><b>${esc(x.title)}</b><small>${esc(x.updatedAt)}</small></div>`).join('');$('#messages').innerHTML=(s.messages||[]).length?(s.messages||[]).map(m=>`<div class=""msg ${m.role==='user'?'user':'assistant'}"">${esc(m.content)||'…'}</div>`).join(''):'<div class=""empty"">开始一次新的 Zako 对话</div>';$('#send').disabled=!!s.busy;$('#stop').disabled=!s.busy;$('#messages').scrollTop=$('#messages').scrollHeight}function send(){const i=$('#input');const v=i.value.trim();if(!v)return;post({action:'send',text:v});i.value=''}$('#send').onclick=send;$('#stop').onclick=()=>post({action:'stop'});$('#settings').onclick=()=>post({action:'settings'});$('#hide').onclick=()=>post({action:'hide'});$('#newChat').onclick=()=>post({action:'newChat'});$('#sessions').onclick=e=>{const s=e.target.closest('.session');if(s)post({action:'selectSession',id:s.dataset.id})};$('#input').addEventListener('keydown',e=>{if(e.key==='Enter'&&e.ctrlKey){e.preventDefault();send()}});chrome.webview.addEventListener('message',e=>{if(e.data.type==='sidebarState')render(e.data)});post({action:'ready'});";

        private const string IconSend = UiIcons.Send;
        private const string IconStop = UiIcons.Stop;
        private const string IconExpand = UiIcons.Expand;
        private const string IconSettings = UiIcons.Settings;
        private const string IconClose = UiIcons.Close;
        private const string IconNew = UiIcons.Add;
    }

    internal static class WebCopilotHtmlV051
    {
        public static string BuildQuickHtml()
        {
            return @"<!doctype html><html><head><meta charset='utf-8'><style>" + SharedCss + QuickCss + @"</style></head><body><main class='quick-shell' id='drag'><header><div class='brand'><span class='zako-mark'></span><div><b id='app'>Zako Chat</b><small id='status'></small></div></div><nav><button title='展开侧栏' data-action='expand'>" + UiIcons.Expand + @"</button><button title='设置' data-action='settings'>" + UiIcons.Settings + @"</button><button title='隐藏' data-action='hide'>" + UiIcons.Close + @"</button></nav></header><section class='composer'><textarea id='input' placeholder='向 Zako 提问...'></textarea><button class='send' id='send' title='发送'>" + UiIcons.Send + @"</button></section></main><script>" + QuickScript + @"</script></body></html>";
        }

        public static string BuildSidebarHtml()
        {
            return @"<!doctype html><html><head><meta charset='utf-8'><style>" + SharedCss + SidebarCss + @"</style></head><body><div class='app'><aside><div class='side-title'><span class='zako-mark small'></span><div><b>Zako</b><small>历史记录</small></div></div><button class='new' id='newChat'>" + UiIcons.Add + @"<span>新建对话</span></button><div class='sessions' id='sessions'></div></aside><main><header><div class='head-copy'><h1 id='appName'>Zako Chat</h1><p id='status'></p></div><div class='mode-switch'><button id='modeText' class='active'>文字对话</button><button id='modeImage'>图片生成</button></div><div class='tools'><button id='toggleEdge' title='切换左右侧'>" + UiIcons.Side + @"</button><button id='settings' title='设置'>" + UiIcons.Settings + @"</button><button id='quickClose' class='close-fast' title='隐藏到托盘'>" + UiIcons.Close + @"</button></div></header><section class='messages' id='messages'></section><footer><div class='attachment' id='attachment'></div><textarea id='input' placeholder='输入消息，Ctrl+Enter 发送'></textarea><button id='upload' title='上传图片给视觉模型'>" + UiIcons.Image + @"</button><button id='stop' title='停止'>" + UiIcons.Stop + @"</button><button id='send' class='send' title='发送'>" + UiIcons.Send + @"</button></footer></main></div><script>" + SidebarScript + @"</script></body></html>";
        }

        private const string SharedCss = @"*{box-sizing:border-box}html,body{margin:0;width:100%;height:100%;overflow:hidden}body{padding:1px;font-family:'Segoe UI Variable Text','Segoe UI Variable','Segoe UI','Microsoft YaHei UI',sans-serif;color:var(--text);background:var(--windowBottom);--glass:rgba(255,255,255,.74);--glass2:rgba(255,255,255,.48);--line:rgba(0,0,0,.09);--hover:rgba(0,0,0,.055);--press:rgba(0,0,0,.09);--shadow:rgba(0,0,0,.18);--prompt:rgba(255,255,255,.9);--danger:#d13438}body[data-theme='dark']{--glass:rgba(42,45,54,.88);--glass2:rgba(34,37,45,.74);--line:rgba(255,255,255,.11);--hover:rgba(255,255,255,.09);--press:rgba(255,255,255,.14);--shadow:rgba(0,0,0,.42);--prompt:rgba(48,51,60,.94);--danger:#ff8a8a}button,textarea{font:inherit}button{border:0;color:var(--text);cursor:pointer;background:transparent}.ui-icon{width:20px;height:20px;display:block;overflow:visible;flex:0 0 auto}button .ui-icon{margin:auto}.zako-mark{width:30px;height:30px;border-radius:12px;background:conic-gradient(from 215deg,var(--accent),#72f0cf,#8aa8ff,var(--accent));box-shadow:inset 0 1px 0 rgba(255,255,255,.65),0 0 24px rgba(90,160,255,.24)}.zako-mark.small{width:26px;height:26px;border-radius:10px}nav button,.tools button{width:40px;height:40px;display:grid;place-items:center;border-radius:13px;border:1px solid transparent;transition:background .12s ease,border-color .12s ease,transform .12s ease}nav button:hover,.tools button:hover{background:var(--hover);border-color:var(--line)}nav button:active,.tools button:active{background:var(--press);transform:scale(.96)}textarea{resize:none;outline:none;color:var(--text);background:transparent;border:0}textarea::placeholder{color:var(--muted)}";

        private const string QuickCss = @".quick-shell{width:calc(100vw - 2px);height:calc(100vh - 2px);padding:13px 14px 14px;border:1px solid var(--line);border-radius:28px;background:linear-gradient(145deg,var(--glass),var(--glass2));box-shadow:inset 0 1px 0 rgba(255,255,255,.2),0 22px 52px var(--shadow);backdrop-filter:blur(30px) saturate(150%)}header{height:38px;display:flex;align-items:center;justify-content:space-between;user-select:none}header,.brand{display:flex;align-items:center;gap:11px}.brand b{display:block;font-size:14px;font-weight:650}.brand small{display:block;max-width:480px;font-size:11px;color:var(--sub);white-space:nowrap;overflow:hidden;text-overflow:ellipsis}nav{display:flex;gap:6px}.composer{height:76px;margin-top:10px;padding:12px 12px 12px 16px;border-radius:24px;background:var(--prompt);border:1px solid var(--line);display:grid;grid-template-columns:1fr 40px;gap:12px;box-shadow:inset 0 1px 0 rgba(255,255,255,.18)}.composer:focus-within{border-color:var(--accent);box-shadow:0 0 0 3px rgba(96,165,250,.16),inset 0 1px 0 rgba(255,255,255,.18)}#input{height:50px;line-height:25px;font-size:15px}.send{width:40px;height:40px;border-radius:15px;align-self:end;display:grid;place-items:center;background:var(--accent);color:var(--accentText);box-shadow:0 8px 22px rgba(70,120,240,.22);transition:transform .12s ease,filter .12s ease}.send:hover{filter:brightness(1.06)}.send:active{transform:scale(.96)}.send:disabled{opacity:.5}";

        private const string SidebarCss = @".app{width:calc(100vw - 2px);height:calc(100vh - 2px);display:grid;grid-template-columns:214px minmax(0,1fr);background:linear-gradient(145deg,var(--windowTop),var(--windowBottom));border:1px solid var(--line);border-radius:23px;overflow:hidden;box-shadow:inset 0 1px 0 rgba(255,255,255,.16),0 18px 48px var(--shadow)}aside{min-width:0;padding:16px 10px;border-right:1px solid var(--line);background:var(--glass2);backdrop-filter:blur(24px) saturate(140%)}.side-title{height:38px;display:flex;align-items:center;gap:10px;margin:0 6px 14px}.side-title b{font-size:14px}.side-title small{display:block;color:var(--sub);font-size:11px}.new{width:100%;height:40px;border-radius:13px;background:var(--hover);display:flex;align-items:center;gap:10px;padding:0 12px;border:1px solid var(--line)}.new:hover{background:var(--press)}.sessions{margin-top:12px;height:calc(100% - 70px);overflow:auto;padding-right:2px}.session{position:relative;width:100%;min-height:50px;margin:4px 0;padding:8px 40px 8px 10px;border-radius:13px;color:var(--sub);cursor:pointer;transition:background .12s ease,color .12s ease}.session:hover{background:var(--hover);color:var(--text)}.session.active{background:rgba(96,165,250,.18);color:var(--text)}.session b{display:block;font-size:12px;font-weight:520;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}.session small{font-size:10px;color:var(--muted)}.del{position:absolute;right:6px;top:8px;width:32px;height:32px;display:grid;place-items:center;border-radius:10px;opacity:0}.session:hover .del{opacity:1}.del:hover{background:var(--press);color:var(--danger)}main{min-width:0;display:grid;grid-template-rows:76px minmax(0,1fr) auto}header{min-width:0;padding:0 18px;display:grid;grid-template-columns:minmax(140px,1fr) auto auto;gap:14px;align-items:center;border-bottom:1px solid var(--line);background:var(--glass)}.head-copy{min-width:0}h1{margin:0;font-size:18px;font-weight:650}p{margin:3px 0 0;color:var(--sub);font-size:12px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}.mode-switch{height:38px;padding:3px;display:flex;gap:3px;border:1px solid var(--line);border-radius:15px;background:var(--hover)}.mode-switch button{height:30px;min-width:76px;padding:0 12px;border-radius:12px;font-size:12px;color:var(--sub)}.mode-switch button.active{background:var(--prompt);color:var(--text);box-shadow:0 1px 8px rgba(0,0,0,.08)}.tools{display:flex;gap:8px}.close-fast:hover{color:var(--danger)}.messages{min-height:0;padding:18px;overflow:auto;background:rgba(0,0,0,.018)}body[data-theme='dark'] .messages{background:rgba(0,0,0,.08)}.msg{max-width:92%;margin:0 0 12px;padding:12px 14px;border-radius:18px;line-height:1.55;font-size:13.5px;white-space:pre-wrap;word-break:break-word;border:1px solid var(--line);box-shadow:inset 0 1px 0 rgba(255,255,255,.08);animation:rise .083s ease-out both}.msg.user{margin-left:auto;background:var(--accent);color:var(--accentText);border-color:transparent}.msg.assistant{background:var(--prompt)}.image-card,.vision-card{white-space:normal}.image-card img,.vision-card img{display:block;max-width:100%;max-height:380px;border-radius:14px;margin-top:10px;border:1px solid var(--line);object-fit:contain;background:#111}.image-meta,.attach-meta{margin-top:8px;font-size:11px;color:var(--muted)}.msg.user .attach-meta{color:rgba(255,255,255,.78)}.empty{height:100%;display:grid;place-items:center;color:var(--muted);text-align:center}footer{position:relative;padding:14px 18px;background:var(--glass);border-top:1px solid var(--line);display:grid;grid-template-columns:minmax(0,1fr) 40px 40px 44px;grid-template-rows:auto 94px;gap:10px}.attachment{grid-column:1 / -1;min-height:0}.attachment:not(:empty){margin-bottom:2px}.attach-chip{display:flex;align-items:center;gap:8px;width:max-content;max-width:100%;padding:6px 8px;border:1px solid var(--line);border-radius:12px;background:var(--prompt);font-size:12px;color:var(--sub)}.attach-chip b{max-width:360px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;color:var(--text);font-weight:520}.attach-chip button{width:26px;height:26px;border-radius:9px;display:grid;place-items:center;background:var(--hover)}footer textarea{grid-column:1;grid-row:2;height:94px;min-width:0;padding:13px 14px;border-radius:19px;background:var(--prompt);border:1px solid var(--line);line-height:1.5}footer textarea:focus{border-color:var(--accent);box-shadow:0 0 0 3px rgba(96,165,250,.16)}footer>button{grid-row:2;width:40px;height:40px;align-self:end;border-radius:14px;background:var(--hover);display:grid;place-items:center}footer>button:hover{background:var(--press)}footer .send{width:44px;height:44px;border-radius:16px;background:var(--accent);color:var(--accentText);box-shadow:0 8px 22px rgba(70,120,240,.22)}footer.image-mode{grid-template-columns:minmax(0,1fr) 40px 44px}footer.image-mode #upload{display:none}button:disabled{opacity:.45;cursor:default}@keyframes rise{from{opacity:.35;transform:translateY(6px)}to{opacity:1;transform:none}}@media (max-width:760px){.app{grid-template-columns:0 minmax(0,1fr)}aside{display:none}header{grid-template-columns:minmax(120px,1fr) auto auto}.mode-switch button{min-width:64px;padding:0 8px}}";

        private static readonly string QuickScript = @"const $=s=>document.querySelector(s);let state=null;function post(m){chrome.webview.postMessage(m)}function css(t){document.body.dataset.theme=t.light?'light':'dark';const r=document.documentElement.style;r.setProperty('--accent',t.accent);r.setProperty('--accentText',t.accentText);r.setProperty('--text',t.text);r.setProperty('--sub',t.subText);r.setProperty('--muted',t.mutedText);r.setProperty('--windowTop',t.windowTop);r.setProperty('--windowBottom',t.windowBottom)}function render(s){state=s;css(s.theme);$('#app').textContent=s.appName;$('#status').textContent=(s.busy?'正在生成回复':'快速提问')+' · '+s.provider+' · '+s.model+(s.memory?' · '+s.memory:'');$('#send').disabled=!!s.busy}function send(){const i=$('#input');const v=i.value.trim();if(!v)return;post({action:'send',text:v});i.value=''}window.zakoFocus=()=>$('#input').focus();$('#send').onclick=send;document.querySelector('[title=设置]').onclick=()=>post({action:'settings'});$('#input').addEventListener('keydown',e=>{if(e.key==='Enter'&&(e.ctrlKey||(!e.shiftKey&&!e.altKey))){e.preventDefault();send()}if(e.key==='Escape')post({action:'hide'})});document.querySelectorAll('[data-action]').forEach(b=>b.onclick=()=>post({action:b.dataset.action}));let drag=false;$('#drag').addEventListener('pointerdown',e=>{if(e.target.closest('button,textarea'))return;drag=true;post({action:'dragStart',screenX:Math.round(e.screenX),screenY:Math.round(e.screenY)})});addEventListener('pointermove',e=>{if(drag)post({action:'dragMove',screenX:Math.round(e.screenX),screenY:Math.round(e.screenY)})});addEventListener('pointerup',()=>{if(drag){drag=false;post({action:'dragEnd'})}});chrome.webview.addEventListener('message',e=>{if(e.data.type==='quickState')render(e.data)});post({action:'ready'});";

        private static readonly string SidebarScript = @"const $=s=>document.querySelector(s);let mode='text';let state={};function post(m){chrome.webview.postMessage(m)}function esc(s){return String(s||'').replace(/[&<>]/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;'}[c]))}function css(t){document.body.dataset.theme=t.light?'light':'dark';const r=document.documentElement.style;r.setProperty('--accent',t.accent);r.setProperty('--accentText',t.accentText);r.setProperty('--text',t.text);r.setProperty('--sub',t.subText);r.setProperty('--muted',t.mutedText);r.setProperty('--windowTop',t.windowTop);r.setProperty('--windowBottom',t.windowBottom)}function fileUrl(p){return p?('file:///'+String(p).replace(/\\/g,'/')):''}function imageSrc(m){return m.imagePath?fileUrl(m.imagePath):(m.sourceUrl||m.dataUrl||'')}function messageHtml(m){const role=m.role==='user'?'user':'assistant';if(m.messageType==='image'){const src=imageSrc(m);return `<div class='msg ${role} image-card'><div>${esc(m.content)||'图片生成完成'}</div>${src?`<img src='${esc(src)}'>`:`<div class='image-meta'>预览已过期</div>`}<div class='image-meta'>${esc(m.imageModelId||'图片模型')} · ${esc(m.imageSize||'默认尺寸')}</div></div>`}if(m.messageType==='vision'){const src=fileUrl(m.attachmentPath);return `<div class='msg ${role} vision-card'><div>${esc(m.content)||'图文消息'}</div>${src?`<img src='${esc(src)}'>`:''}<div class='attach-meta'>视觉输入 · ${esc(m.attachmentName||'本地图片')}</div></div>`}return `<div class='msg ${role}'>${esc(m.content)||'…'}</div>`}function setMode(next){mode=next;$('#modeText').classList.toggle('active',mode==='text');$('#modeImage').classList.toggle('active',mode==='image');document.querySelector('footer').classList.toggle('image-mode',mode==='image');$('#input').placeholder=mode==='image'?'描述要生成的图片':'输入消息，Ctrl+Enter 发送';$('#upload').disabled=mode!=='text'||!!state.busy||!!state.imageBusy;$('#send').title=mode==='image'?'生成图片':'发送'}function renderAttachment(s){const box=$('#attachment');if(!s.pendingImageName){box.innerHTML='';return}box.innerHTML=`<div class='attach-chip'>" + UiIcons.Image.Replace("\r", "").Replace("\n", "") + @"<b>${esc(s.pendingImageName)}</b><span>将随下一条消息发送</span><button id='removeAttachment' title='移除'>" + UiIcons.Close.Replace("\r", "").Replace("\n", "") + @"</button></div>`;$('#removeAttachment').onclick=()=>post({action:'removeAttachment'})}function render(s){state=s;css(s.theme);$('#appName').textContent=s.appName;$('#status').textContent=(s.busy?'正在生成回复':(s.imageBusy?'正在生成图片':'已就绪'))+' · '+s.provider+' · '+s.model+(s.memory?' · '+s.memory:'');$('#toggleEdge').title=s.edge==='right'?'切换到左侧':'切换到右侧';$('#sessions').innerHTML=(s.sessions||[]).map(x=>`<div class='session ${x.id===s.selectedSessionId?'active':''}' data-id='${esc(x.id)}'><b>${esc(x.title)}</b><small>${esc(x.updatedAt)}</small><button class='del' data-id='${esc(x.id)}' title='删除'>" + UiIcons.Clear.Replace("\r", "").Replace("\n", "") + @"</button></div>`).join('');$('#messages').innerHTML=(s.messages||[]).length?(s.messages||[]).map(messageHtml).join(''):'<div class='empty'>开始一次新的 Zako 对话</div>';renderAttachment(s);$('#send').disabled=mode==='image'?!!s.imageBusy:!!s.busy;$('#stop').disabled=!s.busy&&!s.imageBusy;setMode(mode);$('#messages').scrollTop=$('#messages').scrollHeight}function send(){const i=$('#input');const v=i.value.trim();if(!v)return;if(mode==='image')post({action:'generateImage',text:v});else post({action:'send',text:v});i.value=''}$('#send').onclick=send;$('#upload').onclick=()=>post({action:'uploadImage'});$('#stop').onclick=()=>post({action:'stop'});$('#settings').onclick=()=>post({action:'settings'});$('#quickClose').onclick=()=>post({action:'hide'});$('#toggleEdge').onclick=()=>post({action:'toggleEdge'});$('#newChat').onclick=()=>post({action:'newChat'});$('#modeText').onclick=()=>setMode('text');$('#modeImage').onclick=()=>setMode('image');$('#sessions').onclick=e=>{const del=e.target.closest('.del');if(del){e.stopPropagation();post({action:'deleteSession',id:del.dataset.id});return}const row=e.target.closest('.session');if(row)post({action:'selectSession',id:row.dataset.id})};$('#input').addEventListener('keydown',e=>{if(e.key==='Enter'&&e.ctrlKey){e.preventDefault();send()}if(e.key==='Escape')post({action:'hide'})});chrome.webview.addEventListener('message',e=>{if(e.data.type==='sidebarState')render(e.data)});setMode('text');post({action:'ready'});";
    }
}
