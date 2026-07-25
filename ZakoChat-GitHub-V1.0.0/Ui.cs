using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace ZakoChat
{
    internal static class NativeUi
    {
        [DllImport("gdi32.dll")]
        internal static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);

        [DllImport("gdi32.dll")]
        internal static extern bool DeleteObject(IntPtr hObject);

        [DllImport("dwmapi.dll", PreserveSig = true)]
        internal static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        internal const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        internal const int DWMWCP_ROUND = 2;
    }

    public sealed class ThemePalette
    {
        public bool IsLight;
        public Color Accent;
        public Color AccentText;
        public Color WindowTop;
        public Color WindowBottom;
        public Color Surface;
        public Color SurfaceAlt;
        public Color Header;
        public Color MessageBack;
        public Color InputBack;
        public Color Text;
        public Color SubText;
        public Color MutedText;
        public Color Border;
        public Color UserBubble;
        public Color AssistantBubble;
        public Color Danger;

        public static ThemePalette Resolve(AppSettings settings)
        {
            if (settings == null) settings = AppSettings.CreateDefault();
            settings.Normalize();

            bool light = settings.Appearance.Theme == ThemeMode.Light ||
                (settings.Appearance.Theme == ThemeMode.FollowSystem && SystemTheme.IsLight());
            Color accent = settings.Appearance.UseSystemAccent ? SystemTheme.AccentColor() : settings.Appearance.AccentColor;
            accent = NormalizeAccent(accent, light);

            ThemePalette palette = new ThemePalette();
            palette.IsLight = light;
            palette.Accent = accent;
            palette.AccentText = Color.White;
            palette.Danger = Color.FromArgb(196, 43, 28);

            if (light)
            {
                palette.WindowTop = Color.FromArgb(249, 250, 252);
                palette.WindowBottom = Color.FromArgb(234, 238, 244);
                palette.Surface = Color.FromArgb(255, 255, 255);
                palette.SurfaceAlt = Color.FromArgb(244, 246, 250);
                palette.Header = Color.FromArgb(250, 251, 253);
                palette.MessageBack = Color.FromArgb(246, 248, 252);
                palette.InputBack = Color.FromArgb(255, 255, 255);
                palette.Text = Color.FromArgb(25, 28, 33);
                palette.SubText = Color.FromArgb(76, 84, 96);
                palette.MutedText = Color.FromArgb(112, 121, 134);
                palette.Border = Color.FromArgb(210, 215, 224);
                palette.UserBubble = accent;
                palette.AssistantBubble = Color.FromArgb(255, 255, 255);
            }
            else
            {
                palette.WindowTop = Color.FromArgb(38, 40, 46);
                palette.WindowBottom = Color.FromArgb(28, 30, 36);
                palette.Surface = Color.FromArgb(43, 46, 54);
                palette.SurfaceAlt = Color.FromArgb(35, 38, 46);
                palette.Header = Color.FromArgb(40, 43, 51);
                palette.MessageBack = Color.FromArgb(31, 34, 41);
                palette.InputBack = Color.FromArgb(45, 48, 56);
                palette.Text = Color.FromArgb(241, 243, 247);
                palette.SubText = Color.FromArgb(177, 184, 196);
                palette.MutedText = Color.FromArgb(139, 148, 162);
                palette.Border = Color.FromArgb(68, 74, 86);
                palette.UserBubble = accent;
                palette.AssistantBubble = Color.FromArgb(45, 49, 58);
            }
            return palette;
        }

        private static Color NormalizeAccent(Color color, bool light)
        {
            if (color.GetBrightness() < 0.16f)
                return Color.FromArgb(0, 120, 215);
            if (light && color.GetBrightness() > 0.78f)
                return Color.FromArgb(0, 120, 215);
            if (!light && color.GetBrightness() > 0.82f)
                return UiDrawing.Blend(color, Color.FromArgb(0, 120, 215), 0.35f);
            return color;
        }
    }

    internal static class UiDrawing
    {
        public static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            int safeRadius = Math.Max(1, Math.Min(radius, Math.Min(r.Width, r.Height) / 2));
            int d = safeRadius * 2;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static Color WithAlpha(Color color, int alpha)
        {
            return Color.FromArgb(Math.Max(0, Math.Min(255, alpha)), color.R, color.G, color.B);
        }

        public static double EaseOutCubic(double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            double p = t - 1;
            return p * p * p + 1;
        }

        public static Color Blend(Color a, Color b, float amount)
        {
            amount = Math.Max(0f, Math.Min(1f, amount));
            int r = (int)(a.R + (b.R - a.R) * amount);
            int g = (int)(a.G + (b.G - a.G) * amount);
            int bl = (int)(a.B + (b.B - a.B) * amount);
            return Color.FromArgb(r, g, bl);
        }

        public static void FillRoundedRectangle(Graphics g, Brush brush, Rectangle rect, int radius)
        {
            using (GraphicsPath path = RoundRect(rect, radius))
            {
                g.FillPath(brush, path);
            }
        }

        public static void DrawRoundedRectangle(Graphics g, Pen pen, Rectangle rect, int radius)
        {
            using (GraphicsPath path = RoundRect(rect, radius))
            {
                g.DrawPath(pen, path);
            }
        }
    }

    internal static class ProviderBadgeRenderer
    {
        public static void Draw(Graphics g, string providerId, string displayName, Rectangle rect)
        {
            if (g == null || rect.Width <= 0 || rect.Height <= 0) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (ProviderIcons.TryDraw(g, providerId, rect))
                return;

            Color color = GetColor(providerId);
            using (SolidBrush brush = new SolidBrush(color))
            using (Pen pen = new Pen(UiDrawing.WithAlpha(Color.White, 110), 1))
            {
                UiDrawing.FillRoundedRectangle(g, brush, rect, Math.Max(4, rect.Width / 3));
                UiDrawing.DrawRoundedRectangle(g, pen, new Rectangle(rect.X, rect.Y, rect.Width - 1, rect.Height - 1), Math.Max(4, rect.Width / 3));
            }

            string text = BuildText(providerId, displayName);
            float size = text.Length > 1 ? Math.Max(6.2f, rect.Width * 0.34f) : Math.Max(7.2f, rect.Width * 0.48f);
            using (Font font = new Font("Microsoft YaHei UI", size, FontStyle.Bold))
            using (StringFormat format = new StringFormat())
            using (SolidBrush brush = new SolidBrush(Color.White))
            {
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                g.DrawString(text, font, brush, rect, format);
            }
        }

        private static Color GetColor(string providerId)
        {
            if (providerId == "openai") return Color.FromArgb(33, 93, 80);
            if (providerId == "gemini") return Color.FromArgb(66, 133, 244);
            if (providerId == "deepseek") return Color.FromArgb(48, 92, 214);
            if (providerId == "bigmodel") return Color.FromArgb(99, 88, 220);
            if (providerId == "siliconflow") return Color.FromArgb(0, 145, 118);
            if (providerId == "moonshot") return Color.FromArgb(92, 77, 198);
            if (providerId == "openrouter") return Color.FromArgb(42, 47, 60);
            if (providerId == "oneapi") return Color.FromArgb(82, 98, 118);
            if (providerId == "custom") return Color.FromArgb(0, 120, 215);
            return Color.FromArgb(0, 120, 215);
        }

        private static string BuildText(string providerId, string displayName)
        {
            if (providerId == "openai") return "O";
            if (providerId == "gemini") return "G";
            if (providerId == "deepseek") return "D";
            if (providerId == "bigmodel") return "智";
            if (providerId == "siliconflow") return "硅";
            if (providerId == "moonshot") return "K";
            if (providerId == "openrouter") return "R";
            if (providerId == "oneapi") return "API";
            if (providerId == "custom") return "自";
            if (string.IsNullOrEmpty(displayName)) return "?";
            return displayName.Trim().Substring(0, 1).ToUpperInvariant();
        }
    }

    public sealed class SidebarForm : Form, ISidebarSurface
    {
        private readonly AppSettings _settings;
        private readonly Action<string> _onSend;
        private readonly Action _onStop;
        private readonly Action _onSettings;
        private readonly Action _onHide;
        private readonly System.Windows.Forms.Timer _slideTimer;
        private readonly System.Windows.Forms.Timer _messageTimer;
        private readonly HeaderPanel _header;
        private readonly Panel _bodyPanel;
        private readonly ConversationListControl _historyList;
        private readonly MessageFlowPanel _messagePanel;
        private readonly Panel _composer;
        private readonly TextBox _input;
        private readonly Button _sendButton;
        private readonly Button _stopButton;
        private readonly ToolTip _tips;
        private readonly List<BubbleControl> _bubbles;
        private ThemePalette _theme;
        private Rectangle _shownBounds;
        private Rectangle _hiddenBounds;
        private DateTime _slideStart;
        private bool _slideTargetVisible;
        private int _slideDurationMs;

        public event EventHandler SidebarStateChanged;
        public event EventHandler NewChatRequested;
        public event Action<string> SessionSelected;
        public event Action<string> SessionDeleteRequested;
        public event EventHandler EdgeToggleRequested;
        public event Action<string> ImageGenerationRequested;
        public event Action<string, string> VisionSendRequested;
        public SidebarState State { get; private set; }

        public SidebarForm(AppSettings settings, Action<string> onSend, Action onStop, Action onSettings, Action onHide)
        {
            _settings = settings;
            _onSend = onSend;
            _onStop = onStop;
            _onSettings = onSettings;
            _onHide = onHide;
            _bubbles = new List<BubbleControl>();
            _theme = ThemePalette.Resolve(_settings);
            State = SidebarState.Hidden;

            Text = AppInfo.AppName;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = _settings.Window.TopMost;
            MinimumSize = new Size(320, 480);
            Font = new Font("Microsoft YaHei UI", 9f);
            KeyPreview = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            _header = new HeaderPanel();
            _header.Dock = DockStyle.Top;
            _header.Height = 64;
            _header.SettingsClicked += delegate { if (_onSettings != null) _onSettings(); };
            _header.HideClicked += delegate { if (_onHide != null) _onHide(); };

            _bodyPanel = new Panel();
            _bodyPanel.Dock = DockStyle.Fill;
            _bodyPanel.BackColor = _theme.MessageBack;

            _historyList = new ConversationListControl();
            _historyList.Dock = DockStyle.Left;
            _historyList.Width = 180;
            _historyList.NewChatRequested += delegate { if (NewChatRequested != null) NewChatRequested(this, EventArgs.Empty); };
            _historyList.SessionSelected += delegate(string id) { if (SessionSelected != null) SessionSelected(id); };

            _messagePanel = new MessageFlowPanel();
            _messagePanel.Dock = DockStyle.Fill;
            _messagePanel.FlowDirection = FlowDirection.TopDown;
            _messagePanel.WrapContents = false;
            _messagePanel.AutoScroll = true;
            _messagePanel.Padding = new Padding(18, 14, 18, 14);

            _composer = new Panel();
            _composer.Dock = DockStyle.Bottom;
            _composer.Height = 116;
            _composer.Paint += OnComposerPaint;
            _composer.Resize += delegate { LayoutComposer(); };

            _input = new TextBox();
            _input.Multiline = true;
            _input.BorderStyle = BorderStyle.None;
            _input.ScrollBars = ScrollBars.Vertical;
            _input.AcceptsReturn = true;
            _input.Font = new Font("Microsoft YaHei UI", 9.5f);
            _input.KeyDown += OnInputKeyDown;

            _sendButton = CreateButton("发送");
            _sendButton.Click += delegate { SendFromInput(); };
            _stopButton = CreateButton("停止");
            _stopButton.Enabled = false;
            _stopButton.Click += delegate { if (_onStop != null) _onStop(); };
            _tips = new ToolTip();
            _tips.SetToolTip(_sendButton, "发送");
            _tips.SetToolTip(_stopButton, "停止生成");

            _composer.Controls.Add(_input);
            _composer.Controls.Add(_stopButton);
            _composer.Controls.Add(_sendButton);

            _bodyPanel.Controls.Add(_messagePanel);
            _bodyPanel.Controls.Add(_historyList);
            Controls.Add(_bodyPanel);
            Controls.Add(_composer);
            Controls.Add(_header);

            _slideTimer = new System.Windows.Forms.Timer();
            _slideTimer.Interval = 15;
            _slideTimer.Tick += OnSlideTick;

            _messageTimer = new System.Windows.Forms.Timer();
            _messageTimer.Interval = 15;
            _messageTimer.Tick += OnMessageTick;

            Resize += delegate { RelayoutBubbles(); ApplyRoundedCorners(); Invalidate(); };
            VisibleChanged += delegate { if (!Visible) _messageTimer.Stop(); };
            ApplySettings();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyRoundedCorners();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using (LinearGradientBrush brush = new LinearGradientBrush(ClientRectangle, _theme.WindowTop, _theme.WindowBottom, LinearGradientMode.Vertical))
                e.Graphics.FillRectangle(brush, ClientRectangle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen border = new Pen(_theme.Border))
                e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        }

        public void ApplySettings()
        {
            _settings.Normalize();
            _theme = ThemePalette.Resolve(_settings);
            TopMost = _settings.Window.TopMost;
            Opacity = _settings.Window.OpacityPercent / 100.0;
            BackColor = _theme.WindowBottom;
            _header.SetTheme(_theme);
            _bodyPanel.BackColor = _theme.MessageBack;
            _historyList.SetTheme(_theme);
            _messagePanel.SetTheme(_theme);
            _composer.BackColor = _theme.WindowBottom;
            _input.BackColor = _theme.InputBack;
            _input.ForeColor = _theme.Text;
            ApplyButtonStyle(_sendButton, true);
            ApplyButtonStyle(_stopButton, false);
            foreach (BubbleControl bubble in _bubbles) bubble.SetTheme(_theme);
            PositionAtEdge(State == SidebarState.Shown);
            LayoutComposer();
            RelayoutBubbles();
            Invalidate(true);
        }

        public void SetIcon(Icon icon)
        {
            Icon = icon;
        }

        public void SetProviderStatus(string providerId, string provider, string model, string memory)
        {
            _header.SetStatus(providerId, provider, model, memory);
        }

        public void SetBusy(bool busy)
        {
            if (InvokeRequired)
            {
                SafeBeginInvoke(new Action<bool>(SetBusy), busy);
                return;
            }
            _sendButton.Enabled = !busy;
            _stopButton.Enabled = busy;
            _header.SetBusy(busy);
        }

        public void SetImageBusy(bool busy)
        {
        }

        public object AddMessage(ChatMessage message)
        {
            if (InvokeRequired)
                return Invoke(new Func<ChatMessage, object>(AddMessage), message);

            BubbleControl bubble = new BubbleControl(message, _theme);
            bubble.Width = BubbleWidth();
            bubble.Margin = new Padding(0, 3, 0, 10);
            bubble.Reveal = _settings.Appearance.ReducedMotion || !_settings.Appearance.AnimationEnabled ? 1f : 0f;
            bubble.OffsetY = bubble.Reveal >= 1f ? 0 : 10;
            _bubbles.Add(bubble);
            _messagePanel.Controls.Add(bubble);
            bubble.UpdatePreferredHeight();
            ScrollToBottom();
            if (bubble.Reveal < 1f && Visible) _messageTimer.Start();
            return bubble;
        }

        public void UpdateBubble(object bubbleHandle, string text, bool append)
        {
            if (InvokeRequired)
            {
                SafeBeginInvoke(new Action<object, string, bool>(UpdateBubble), bubbleHandle, text, append);
                return;
            }
            BubbleControl bubble = bubbleHandle as BubbleControl;
            if (bubble == null) return;
            if (append) bubble.Message.Content += text;
            else bubble.Message.Content = text;
            bubble.UpdatePreferredHeight();
            ScrollToBottom();
        }

        public void LoadMessages(IEnumerable<ChatMessage> messages)
        {
            _messagePanel.Controls.Clear();
            _bubbles.Clear();
            if (messages != null)
            {
                foreach (ChatMessage message in messages)
                {
                    BubbleControl bubble = new BubbleControl(message, _theme);
                    bubble.Width = BubbleWidth();
                    bubble.Margin = new Padding(0, 3, 0, 10);
                    bubble.Reveal = 1f;
                    _bubbles.Add(bubble);
                    _messagePanel.Controls.Add(bubble);
                    bubble.UpdatePreferredHeight();
                }
            }
            ScrollToBottom();
        }

        public void LoadSessions(IList<ChatSession> sessions, string selectedId)
        {
            _historyList.SetSessions(sessions, selectedId);
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
                Activate();
                SetState(SidebarState.Shown);
                return;
            }

            Bounds = _hiddenBounds;
            Opacity = 0.08;
            _slideTargetVisible = true;
            _slideDurationMs = ScaleDuration(167);
            _slideStart = DateTime.UtcNow;
            Show();
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
            _slideDurationMs = ScaleDuration(167);
            _slideStart = DateTime.UtcNow;
            SetState(SidebarState.Hiding);
            _slideTimer.Start();
        }

        private void OnSlideTick(object sender, EventArgs e)
        {
            double t = (DateTime.UtcNow - _slideStart).TotalMilliseconds / Math.Max(1, _slideDurationMs);
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
            int x = (int)(from.X + (to.X - from.X) * eased);
            Bounds = new Rectangle(x, to.Y, to.Width, to.Height);
            double maxOpacity = _settings.Window.OpacityPercent / 100.0;
            Opacity = _slideTargetVisible ? (0.08 + (maxOpacity - 0.08) * eased) : (maxOpacity - (maxOpacity - 0.08) * eased);
        }

        private void OnMessageTick(object sender, EventArgs e)
        {
            bool active = false;
            float step = Math.Max(0.16f, 15f / Math.Max(45f, ScaleDuration(83)));
            foreach (BubbleControl bubble in _bubbles)
            {
                if (bubble.Reveal < 1f)
                {
                    bubble.Reveal = Math.Min(1f, bubble.Reveal + step);
                    bubble.OffsetY = (int)(10 * (1f - bubble.Reveal));
                    bubble.Invalidate();
                    active = true;
                }
            }
            if (!active) _messageTimer.Stop();
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
            int width = Math.Min(_settings.Window.Width, Math.Max(320, area.Width - 24));
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

        private int ScaleDuration(int baseMs)
        {
            return Math.Max(45, baseMs * 100 / Math.Max(60, _settings.Appearance.AnimationSpeedPercent));
        }

        private void OnComposerPaint(object sender, PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(_theme.WindowBottom);
            Rectangle shell = new Rectangle(14, 10, _composer.Width - 28, _composer.Height - 20);
            using (SolidBrush brush = new SolidBrush(_theme.InputBack))
            using (Pen pen = new Pen(_theme.Border))
            {
                UiDrawing.FillRoundedRectangle(e.Graphics, brush, shell, 18);
                UiDrawing.DrawRoundedRectangle(e.Graphics, pen, new Rectangle(shell.X, shell.Y, shell.Width - 1, shell.Height - 1), 18);
            }
        }

        private void LayoutComposer()
        {
            int left = 30;
            int right = _composer.Width - 30;
            int buttonY = _composer.Height - 48;
            _input.Location = new Point(left, 24);
            _input.Size = new Size(Math.Max(80, right - left - 88), 58);
            _sendButton.Size = new Size(56, 34);
            _stopButton.Size = new Size(56, 34);
            _sendButton.Location = new Point(_composer.Width - 96, buttonY);
            _stopButton.Location = new Point(_composer.Width - 160, buttonY);
            ApplyButtonRegion(_sendButton, 18);
            ApplyButtonRegion(_stopButton, 18);
            _composer.Invalidate();
        }

        private Button CreateButton(string text)
        {
            Button button = new Button();
            button.Text = text;
            button.FlatStyle = FlatStyle.Flat;
            button.Font = new Font("Microsoft YaHei UI", 8.8f, FontStyle.Bold);
            button.TabStop = false;
            return button;
        }

        private void ApplyButtonStyle(Button button, bool primary)
        {
            button.FlatAppearance.BorderSize = 1;
            if (primary)
            {
                button.BackColor = _theme.Accent;
                button.ForeColor = _theme.AccentText;
                button.FlatAppearance.BorderColor = _theme.Accent;
                button.FlatAppearance.MouseOverBackColor = UiDrawing.Blend(_theme.Accent, Color.White, 0.12f);
                button.FlatAppearance.MouseDownBackColor = UiDrawing.Blend(_theme.Accent, Color.Black, 0.12f);
            }
            else
            {
                button.BackColor = _theme.SurfaceAlt;
                button.ForeColor = _theme.Text;
                button.FlatAppearance.BorderColor = _theme.Border;
                button.FlatAppearance.MouseOverBackColor = UiDrawing.WithAlpha(_theme.Accent, 32);
                button.FlatAppearance.MouseDownBackColor = UiDrawing.WithAlpha(_theme.Accent, 48);
            }
        }

        private void ApplyButtonRegion(Button button, int radius)
        {
            try
            {
                Region old = button.Region;
                IntPtr rgn = NativeUi.CreateRoundRectRgn(0, 0, button.Width + 1, button.Height + 1, radius * 2, radius * 2);
                button.Region = Region.FromHrgn(rgn);
                NativeUi.DeleteObject(rgn);
                if (old != null) old.Dispose();
            }
            catch { }
        }

        private void OnInputKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && e.Control)
            {
                e.SuppressKeyPress = true;
                SendFromInput();
            }
        }

        private void SendFromInput()
        {
            string text = _input.Text.Trim();
            if (text.Length == 0) return;
            _input.Clear();
            if (_onSend != null) _onSend(text);
        }

        private void RelayoutBubbles()
        {
            int width = BubbleWidth();
            foreach (BubbleControl bubble in _bubbles)
            {
                bubble.Width = width;
                bubble.UpdatePreferredHeight();
            }
        }

        private int BubbleWidth()
        {
            int scrollbar = SystemInformation.VerticalScrollBarWidth;
            return Math.Max(120, _messagePanel.ClientSize.Width - _messagePanel.Padding.Horizontal - scrollbar - 8);
        }

        private void ScrollToBottom()
        {
            try
            {
                ScrollToBottomNow();
                if (IsHandleCreated && !IsDisposed)
                    BeginInvoke(new Action(ScrollToBottomNow));
            }
            catch { }
        }

        private void ScrollToBottomNow()
        {
            try
            {
                _messagePanel.PerformLayout();
                if (_bubbles.Count > 0)
                    _messagePanel.ScrollControlIntoView(_bubbles[_bubbles.Count - 1]);
                _messagePanel.AutoScrollPosition = new Point(0, Math.Max(0, _messagePanel.VerticalScroll.Maximum));
            }
            catch { }
        }

        private void ApplyRoundedCorners()
        {
            try
            {
                Region old = Region;
                IntPtr rgn = NativeUi.CreateRoundRectRgn(0, 0, Width + 1, Height + 1, 22, 22);
                Region = Region.FromHrgn(rgn);
                NativeUi.DeleteObject(rgn);
                if (old != null) old.Dispose();
                int preference = NativeUi.DWMWCP_ROUND;
                NativeUi.DwmSetWindowAttribute(Handle, NativeUi.DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
            }
            catch { }
        }

        private void SafeBeginInvoke(Delegate method, params object[] args)
        {
            try
            {
                if (!IsDisposed && IsHandleCreated) BeginInvoke(method, args);
            }
            catch { }
        }
    }

    public sealed class HeaderPanel : Control
    {
        private readonly Button _settingsButton;
        private readonly Button _hideButton;
        private readonly ToolTip _tips;
        private ThemePalette _theme;
        private string _providerId;
        private string _provider;
        private string _model;
        private string _memory;
        private bool _busy;

        public event EventHandler SettingsClicked;
        public event EventHandler HideClicked;

        public HeaderPanel()
        {
            _theme = ThemePalette.Resolve(AppSettings.CreateDefault());
            _providerId = string.Empty;
            _provider = string.Empty;
            _model = string.Empty;
            _memory = string.Empty;
            Height = 64;
            Font = new Font("Microsoft YaHei UI", 9f);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);

            _settingsButton = MakeHeaderButton("设置");
            _hideButton = MakeHeaderButton("隐藏");
            _tips = new ToolTip();
            _tips.SetToolTip(_settingsButton, "设置");
            _tips.SetToolTip(_hideButton, "隐藏侧栏");
            _settingsButton.Click += delegate { if (SettingsClicked != null) SettingsClicked(this, EventArgs.Empty); };
            _hideButton.Click += delegate { if (HideClicked != null) HideClicked(this, EventArgs.Empty); };
            Controls.Add(_settingsButton);
            Controls.Add(_hideButton);
            Resize += delegate { LayoutButtons(); };
            LayoutButtons();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using (SolidBrush brush = new SolidBrush(_theme.Header))
                e.Graphics.FillRectangle(brush, ClientRectangle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            using (Font titleFont = new Font("Microsoft YaHei UI", 11.5f, FontStyle.Bold))
            using (SolidBrush titleBrush = new SolidBrush(_theme.Text))
            {
                e.Graphics.DrawString(AppInfo.AppName, titleFont, titleBrush, new PointF(16, 10));
            }

            string status = _busy ? "正在生成回复" : "随时待命";
            string providerText = string.IsNullOrEmpty(_provider) ? "未配置服务商" : _provider;
            string modelText = string.IsNullOrEmpty(_model) ? "未选择模型" : _model;
            Rectangle badge = new Rectangle(16, 38, 18, 18);
            ProviderBadgeRenderer.Draw(e.Graphics, _providerId, providerText, badge);

            string line = status + " · " + providerText + " · " + modelText;
            if (!string.IsNullOrEmpty(_memory)) line += " · " + _memory;
            Rectangle textRect = new Rectangle(40, 34, Math.Max(40, Width - 136), 26);
            TextRenderer.DrawText(e.Graphics, line, Font, textRect, _theme.SubText, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            using (Pen pen = new Pen(_theme.Border))
                e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
        }

        public void SetTheme(ThemePalette theme)
        {
            _theme = theme;
            BackColor = _theme.Header;
            ApplyHeaderButton(_settingsButton);
            ApplyHeaderButton(_hideButton);
            Invalidate();
        }

        public void SetStatus(string providerId, string provider, string model, string memory)
        {
            _providerId = providerId ?? string.Empty;
            _provider = provider ?? string.Empty;
            _model = model ?? string.Empty;
            _memory = memory ?? string.Empty;
            Invalidate();
        }

        public void SetBusy(bool busy)
        {
            _busy = busy;
            Invalidate();
        }

        private Button MakeHeaderButton(string text)
        {
            Button button = new Button();
            button.Text = text;
            button.FlatStyle = FlatStyle.Flat;
            button.Font = new Font("Microsoft YaHei UI", 8.5f);
            button.TabStop = false;
            button.Size = new Size(48, 28);
            return button;
        }

        private void ApplyHeaderButton(Button button)
        {
            button.BackColor = _theme.SurfaceAlt;
            button.ForeColor = _theme.Text;
            button.FlatAppearance.BorderColor = _theme.Border;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.MouseOverBackColor = UiDrawing.WithAlpha(_theme.Accent, 32);
            button.FlatAppearance.MouseDownBackColor = UiDrawing.WithAlpha(_theme.Accent, 48);
            try
            {
                Region old = button.Region;
                IntPtr rgn = NativeUi.CreateRoundRectRgn(0, 0, button.Width + 1, button.Height + 1, 16, 16);
                button.Region = Region.FromHrgn(rgn);
                NativeUi.DeleteObject(rgn);
                if (old != null) old.Dispose();
            }
            catch { }
        }

        private void LayoutButtons()
        {
            _hideButton.Location = new Point(Math.Max(0, Width - 64), 10);
            _settingsButton.Location = new Point(Math.Max(0, Width - 118), 10);
        }
    }

    public sealed class MessageFlowPanel : FlowLayoutPanel
    {
        private ThemePalette _theme;

        public MessageFlowPanel()
        {
            _theme = ThemePalette.Resolve(AppSettings.CreateDefault());
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = _theme.MessageBack;
            AutoScroll = true;
            HorizontalScroll.Enabled = false;
            HorizontalScroll.Visible = false;
        }

        public void SetTheme(ThemePalette theme)
        {
            _theme = theme;
            BackColor = _theme.MessageBack;
            Invalidate(true);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using (SolidBrush brush = new SolidBrush(_theme.MessageBack))
                e.Graphics.FillRectangle(brush, ClientRectangle);
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            HorizontalScroll.Enabled = false;
            HorizontalScroll.Visible = false;
        }
    }

    public sealed class BubbleControl : Control
    {
        public readonly ChatMessage Message;
        private ThemePalette _theme;
        public float Reveal;
        public int OffsetY;

        public BubbleControl(ChatMessage message, ThemePalette theme)
        {
            Message = message == null ? new ChatMessage("assistant", string.Empty) : message;
            _theme = theme;
            Reveal = 1f;
            OffsetY = 0;
            Font = new Font("Microsoft YaHei UI", 9.2f);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            SetTheme(theme);
        }

        public void SetTheme(ThemePalette theme)
        {
            _theme = theme;
            BackColor = _theme.MessageBack;
            ForeColor = _theme.Text;
            Invalidate();
        }

        public void UpdatePreferredHeight()
        {
            int bubbleWidth = Math.Min(MaxBubbleWidth(), Math.Max(120, Width - 36));
            int textWidth = Math.Max(80, bubbleWidth - 28);
            string text = DisplayText(textWidth);
            SizeF size = MeasureDisplayText(text, textWidth);
            Height = Math.Max(68, (int)Math.Ceiling(size.Height) + 58);
            Invalidate();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using (SolidBrush brush = new SolidBrush(_theme.MessageBack))
                e.Graphics.FillRectangle(brush, ClientRectangle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            bool user = string.Equals(Message.Role, "user", StringComparison.OrdinalIgnoreCase);
            int maxBubbleWidth = MaxBubbleWidth();
            int bubbleWidth = Math.Min(maxBubbleWidth, Math.Max(120, Width - 36));
            int x = user ? Width - bubbleWidth - 8 : 8;
            Rectangle bubble = new Rectangle(x, 18 + OffsetY, bubbleWidth, Math.Max(36, Height - 26));
            int alpha = (int)(255 * Math.Max(0.18f, Math.Min(1f, Reveal)));

            Color fill = user ? _theme.UserBubble : _theme.AssistantBubble;
            Color border = user ? UiDrawing.Blend(_theme.UserBubble, Color.Black, 0.18f) : _theme.Border;
            using (SolidBrush brush = new SolidBrush(UiDrawing.WithAlpha(fill, alpha)))
            using (Pen pen = new Pen(UiDrawing.WithAlpha(border, alpha)))
            {
                UiDrawing.FillRoundedRectangle(e.Graphics, brush, bubble, 13);
                UiDrawing.DrawRoundedRectangle(e.Graphics, pen, new Rectangle(bubble.X, bubble.Y, bubble.Width - 1, bubble.Height - 1), 13);
            }

            string name = user ? "你" : "Zako";
            Rectangle nameRect = new Rectangle(x + 4, 0 + OffsetY, bubbleWidth - 8, 18);
            using (Font nameFont = new Font("Microsoft YaHei UI", 8f))
            {
                TextRenderer.DrawText(e.Graphics, name, nameFont, nameRect, _theme.MutedText, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }

            Color textColor = user ? Color.White : _theme.Text;
            Rectangle textRect = new Rectangle(bubble.X + 14, bubble.Y + 10, bubble.Width - 28, bubble.Height - 18);
            using (SolidBrush textBrush = new SolidBrush(textColor))
            using (StringFormat format = CreateTextFormat())
            {
                e.Graphics.DrawString(DisplayText(textRect.Width), Font, textBrush, textRect, format);
            }
        }

        private int MaxBubbleWidth()
        {
            return Math.Max(180, (int)(Width * 0.92));
        }

        private string DrawText()
        {
            if (!string.IsNullOrEmpty(Message.Content)) return Message.Content;
            if (string.Equals(Message.Role, "assistant", StringComparison.OrdinalIgnoreCase)) return "正在思考...";
            return string.Empty;
        }

        private string DisplayText(int textWidth)
        {
            int maxRun = Math.Max(18, textWidth / 9);
            return BreakLongRuns(DrawText(), maxRun);
        }

        private SizeF MeasureDisplayText(string text, int width)
        {
            using (Bitmap bmp = new Bitmap(1, 1))
            using (Graphics g = Graphics.FromImage(bmp))
            using (StringFormat format = CreateTextFormat())
            {
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                return g.MeasureString(text, Font, Math.Max(80, width), format);
            }
        }

        private static StringFormat CreateTextFormat()
        {
            StringFormat format = new StringFormat();
            format.Alignment = StringAlignment.Near;
            format.LineAlignment = StringAlignment.Near;
            format.Trimming = StringTrimming.None;
            format.FormatFlags = StringFormatFlags.MeasureTrailingSpaces;
            return format;
        }

        private static string BreakLongRuns(string text, int maxRun)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            System.Text.StringBuilder sb = new System.Text.StringBuilder(text.Length + text.Length / Math.Max(1, maxRun));
            int run = 0;
            foreach (char c in text)
            {
                sb.Append(c);
                if (char.IsWhiteSpace(c) || c == '-' || c == '_' || c == '/' || c == '\\' || c == '.' || c == ',' || c == ';' || c == ':' || c == '，' || c == '。')
                {
                    run = 0;
                    continue;
                }

                run++;
                if (run >= maxRun)
                {
                    sb.Append(Environment.NewLine);
                    run = 0;
                }
            }
            return sb.ToString();
        }
    }

    public sealed class SettingsForm : Form, ISettingsSurface
    {
        private readonly AppSettings _settings;
        private readonly SecretStore _secrets;
        private readonly IChatClient _client;
        private ThemePalette _theme;
        private ProviderConfig _currentProvider;
        private PersonaProfile _currentPersona;
        private bool _loading;

        private ComboBox _providerCombo;
        private TextBox _baseUrlBox;
        private TextBox _keyBox;
        private TextBox _modelPathBox;
        private TextBox _chatPathBox;
        private TextBox _headersBox;
        private ComboBox _modelCombo;
        private Button _probeButton;
        private Label _probeLabel;

        private ComboBox _themeCombo;
        private ComboBox _renderModeCombo;
        private CheckBox _systemAccentCheck;
        private CheckBox _acrylicCheck;
        private CheckBox _animationCheck;
        private CheckBox _reducedMotionCheck;
        private NumericUpDown _animationSpeed;
        private NumericUpDown _opacity;

        private ComboBox _displayModeCombo;
        private CheckBox _rememberModeCheck;
        private CheckBox _expandAnimationCheck;
        private CheckBox _hotkeyEnabledCheck;
        private TextBox _hotkeyBox;
        private Label _hotkeyStatusLabel;
        private int _capturedHotkeyModifiers;
        private int _capturedHotkeyKey;

        private CheckBox _streamCheck;
        private CheckBox _historyCheck;
        private NumericUpDown _tempBox;
        private NumericUpDown _maxTokensBox;
        private ComboBox _personaCombo;
        private TextBox _personaPromptBox;

        private CheckBox _topMostCheck;
        private CheckBox _startWithWindowsCheck;
        private ComboBox _edgeCombo;
        private NumericUpDown _widthBox;
        private NumericUpDown _maxConversationsBox;
        private NumericUpDown _maxMessagesBox;

        public event EventHandler SettingsSaved;

        public SettingsForm(AppSettings settings, SecretStore secrets, IChatClient client)
        {
            _settings = settings;
            _secrets = secrets;
            _client = client;
            _settings.Normalize();
            _theme = ThemePalette.Resolve(_settings);

            Text = "Zako Chat 设置";
            Size = new Size(720, 570);
            MinimumSize = new Size(660, 520);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9f);
            BackColor = _theme.WindowBottom;
            ForeColor = _theme.Text;
            FormBorderStyle = FormBorderStyle.Sizable;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);

            TabControl tabs = new TabControl();
            tabs.Dock = DockStyle.Fill;
            tabs.Font = new Font("Microsoft YaHei UI", 9f);
            tabs.TabPages.Add(MakeTab("模型与接口", BuildProviderTab()));
            tabs.TabPages.Add(MakeTab("Copilot 风格", BuildCopilotTab()));
            tabs.TabPages.Add(MakeTab("外观与动画", BuildAppearanceTab()));
            tabs.TabPages.Add(MakeTab("对话与人设", BuildChatTab()));
            tabs.TabPages.Add(MakeTab("启动与数据", BuildStartupTab()));

            Panel buttons = new Panel();
            buttons.Dock = DockStyle.Bottom;
            buttons.Height = 54;
            buttons.BackColor = _theme.WindowBottom;

            Button save = CreateDialogButton("保存", true);
            Button cancel = CreateDialogButton("取消", false);
            save.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            cancel.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            save.Location = new Point(buttons.Width - 202, 12);
            cancel.Location = new Point(buttons.Width - 106, 12);
            buttons.Resize += delegate
            {
                save.Location = new Point(buttons.Width - 202, 12);
                cancel.Location = new Point(buttons.Width - 106, 12);
            };
            save.Click += delegate { SaveAll(); DialogResult = DialogResult.OK; Close(); };
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            buttons.Controls.Add(save);
            buttons.Controls.Add(cancel);

            Controls.Add(tabs);
            Controls.Add(buttons);
            LoadControls();
            ApplyControlTheme(this);
        }

        private Control BuildProviderTab()
        {
            TableLayoutPanel table = CreateTable();
            _providerCombo = CreateCombo();
            _providerCombo.DrawMode = DrawMode.OwnerDrawFixed;
            _providerCombo.ItemHeight = 26;
            _providerCombo.DrawItem += DrawProviderComboItem;
            _providerCombo.SelectedIndexChanged += OnProviderChanged;
            _baseUrlBox = new TextBox();
            _keyBox = new TextBox();
            _keyBox.PasswordChar = '●';
            _modelPathBox = new TextBox();
            _chatPathBox = new TextBox();
            _headersBox = new TextBox();
            _headersBox.Multiline = true;
            _headersBox.Height = 72;
            _headersBox.ScrollBars = ScrollBars.Vertical;
            _modelCombo = new ComboBox();
            _modelCombo.DropDownStyle = ComboBoxStyle.DropDown;
            _modelCombo.DrawMode = DrawMode.OwnerDrawFixed;
            _modelCombo.ItemHeight = 26;
            _modelCombo.DrawItem += DrawModelComboItem;
            _streamCheck = new CheckBox();
            _streamCheck.Text = "服务支持时使用流式回复";
            _tempBox = CreateNumeric(0, 2, 0.7m, 0.1m, 1);
            _maxTokensBox = CreateNumeric(128, 32000, 2048, 128, 0);
            _probeButton = CreateDialogButton("检测连接并获取模型", true);
            _probeButton.Width = 170;
            _probeButton.Click += delegate { ProbeCurrentProvider(); };
            _probeLabel = CreateHint("输入 API Key 后可检测延迟，并自动读取可用 Model ID。选择“自定义接口”可适配小众中转站。");

            AddSection(table, "接口配置");
            AddRow(table, "服务商", _providerCombo);
            AddRow(table, "Base URL", _baseUrlBox);
            AddRow(table, "API Key", _keyBox);
            AddRow(table, "模型列表路径", _modelPathBox);
            AddRow(table, "对话路径", _chatPathBox);
            AddRow(table, "额外请求头", _headersBox);
            AddRow(table, "Model ID", _modelCombo);
            AddSection(table, "生成参数");
            AddRow(table, "流式输出", _streamCheck);
            AddRow(table, "温度", _tempBox);
            AddRow(table, "最大输出", _maxTokensBox);
            AddRow(table, "连接检测", _probeButton);
            AddRow(table, "状态", _probeLabel);
            return Wrap(table);
        }

        private Control BuildCopilotTab()
        {
            TableLayoutPanel table = CreateTable();
            _displayModeCombo = CreateCombo();
            _displayModeCombo.Items.Add(new ComboItem<DisplayMode>(DisplayMode.QuickPrompt, "快速提问小窗"));
            _displayModeCombo.Items.Add(new ComboItem<DisplayMode>(DisplayMode.FullSidebar, "完整侧栏"));
            _rememberModeCheck = new CheckBox();
            _rememberModeCheck.Text = "启动时记住上次使用的形态";
            _expandAnimationCheck = new CheckBox();
            _expandAnimationCheck.Text = "启用快速小窗展开侧栏动画";
            _hotkeyEnabledCheck = new CheckBox();
            _hotkeyEnabledCheck.Text = "启用全局快捷键";
            _hotkeyBox = new TextBox();
            _hotkeyBox.ReadOnly = true;
            _hotkeyBox.Width = 180;
            _hotkeyBox.KeyDown += OnHotkeyBoxKeyDown;
            Button resetShortcut = CreateDialogButton("重置 Ctrl+Shift+Z", false);
            resetShortcut.Width = 150;
            resetShortcut.Click += delegate
            {
                _capturedHotkeyModifiers = HotkeyModifiers.Control | HotkeyModifiers.Shift;
                _capturedHotkeyKey = (int)Keys.Z;
                UpdateHotkeyBox();
            };
            FlowLayoutPanel hotkeyPanel = new FlowLayoutPanel();
            hotkeyPanel.Dock = DockStyle.Top;
            hotkeyPanel.AutoSize = true;
            hotkeyPanel.FlowDirection = FlowDirection.LeftToRight;
            hotkeyPanel.Controls.Add(_hotkeyBox);
            hotkeyPanel.Controls.Add(resetShortcut);
            _hotkeyStatusLabel = CreateHint("默认快捷键为 Ctrl+Shift+Z；按一次显示，再按一次隐藏。若注册失败，请改成未被占用的组合。");

            AddSection(table, "显示形态");
            AddRow(table, "默认打开", _displayModeCombo);
            AddRow(table, "记住形态", _rememberModeCheck);
            AddRow(table, "展开动画", _expandAnimationCheck);
            AddSection(table, "快捷键");
            AddRow(table, "启用", _hotkeyEnabledCheck);
            AddRow(table, "快捷键", hotkeyPanel);
            AddRow(table, "状态", _hotkeyStatusLabel);
            return Wrap(table);
        }

        private Control BuildAppearanceTab()
        {
            TableLayoutPanel table = CreateTable();
            _renderModeCombo = CreateCombo();
            _renderModeCombo.Items.Add(new ComboItem<UiRenderMode>(UiRenderMode.Auto, "自动（优先 WebView2）"));
            _renderModeCombo.Items.Add(new ComboItem<UiRenderMode>(UiRenderMode.WebView2, "WebView2 高级界面"));
            _renderModeCombo.Items.Add(new ComboItem<UiRenderMode>(UiRenderMode.Native, "原生轻量界面"));
            _themeCombo = CreateCombo();
            _themeCombo.Items.Add(new ComboItem<ThemeMode>(ThemeMode.FollowSystem, "跟随 Windows"));
            _themeCombo.Items.Add(new ComboItem<ThemeMode>(ThemeMode.Light, "浅色"));
            _themeCombo.Items.Add(new ComboItem<ThemeMode>(ThemeMode.Dark, "深色"));
            _systemAccentCheck = new CheckBox();
            _systemAccentCheck.Text = "跟随 Windows 主题色";
            _acrylicCheck = new CheckBox();
            _acrylicCheck.Text = "使用 Mica / Acrylic-like 轻量质感";
            _animationCheck = new CheckBox();
            _animationCheck.Text = "启用侧边栏与消息动画";
            _reducedMotionCheck = new CheckBox();
            _reducedMotionCheck.Text = "减少动态效果";
            _animationSpeed = CreateNumeric(60, 180, 100, 5, 0);
            _opacity = CreateNumeric(82, 100, 98, 1, 0);
            Label renderHint = CreateHint("切换界面渲染模式后，请重启 Zako Chat 使新界面生效。");

            AddRow(table, "界面渲染", _renderModeCombo);
            AddRow(table, "提示", renderHint);
            AddRow(table, "主题", _themeCombo);
            AddRow(table, "主题色", _systemAccentCheck);
            AddRow(table, "窗口质感", _acrylicCheck);
            AddRow(table, "动画", _animationCheck);
            AddRow(table, "动态效果", _reducedMotionCheck);
            AddRow(table, "动画速度（%）", _animationSpeed);
            AddRow(table, "窗口不透明度（%）", _opacity);
            return Wrap(table);
        }

        private Control BuildChatTab()
        {
            TableLayoutPanel table = CreateTable();
            _historyCheck = new CheckBox();
            _historyCheck.Text = "保存本地聊天历史";
            _personaCombo = CreateCombo();
            _personaCombo.SelectedIndexChanged += OnPersonaChanged;
            _personaPromptBox = new TextBox();
            _personaPromptBox.Multiline = true;
            _personaPromptBox.Height = 136;
            _personaPromptBox.ScrollBars = ScrollBars.Vertical;

            AddSection(table, "对话记录");
            AddRow(table, "历史记录", _historyCheck);
            AddSection(table, "AI 人设");
            AddRow(table, "AI 人设", _personaCombo);
            AddRow(table, "系统提示词", _personaPromptBox);
            return Wrap(table);
        }

        private Control BuildStartupTab()
        {
            TableLayoutPanel table = CreateTable();
            _topMostCheck = new CheckBox();
            _topMostCheck.Text = "侧边栏保持置顶";
            _startWithWindowsCheck = new CheckBox();
            _startWithWindowsCheck.Text = "开机自启";
            _edgeCombo = CreateCombo();
            _edgeCombo.Items.Add(new ComboItem<SidebarEdge>(SidebarEdge.Right, "屏幕右侧"));
            _edgeCombo.Items.Add(new ComboItem<SidebarEdge>(SidebarEdge.Left, "屏幕左侧"));
            _widthBox = CreateNumeric(560, 920, 640, 8, 0);
            _maxConversationsBox = CreateNumeric(1, 200, 50, 1, 0);
            _maxMessagesBox = CreateNumeric(20, 1000, 200, 10, 0);
            Button clearHistory = CreateDialogButton("清空本地历史", false);
            clearHistory.Width = 130;
            clearHistory.Click += delegate
            {
                HistoryStore.Clear();
                MessageBox.Show("本地聊天历史已清空。", AppInfo.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            Label path = CreateHint("%AppData%\\ZakoChat 保存设置、历史和已加密 API Key；API Key 使用 Windows DPAPI 当前用户加密。");

            AddRow(table, "窗口", _topMostCheck);
            AddRow(table, "启动", _startWithWindowsCheck);
            AddRow(table, "贴边位置", _edgeCombo);
            AddRow(table, "侧边栏宽度", _widthBox);
            AddRow(table, "历史对话上限", _maxConversationsBox);
            AddRow(table, "单轮消息上限", _maxMessagesBox);
            AddRow(table, "本地数据", clearHistory);
            AddRow(table, "隐私", path);
            return Wrap(table);
        }

        private void LoadControls()
        {
            _loading = true;
            try
            {
                _settings.Normalize();
                _providerCombo.Items.Clear();
                foreach (ProviderConfig provider in _settings.Providers)
                    _providerCombo.Items.Add(new ProviderItem(provider));
                SelectProvider(_settings.Chat.DefaultProviderId);

                SelectComboValue(_displayModeCombo, _settings.Copilot.DefaultDisplayMode);
                _rememberModeCheck.Checked = _settings.Copilot.RememberLastMode;
                _expandAnimationCheck.Checked = _settings.Copilot.ExpandAnimation;
                _hotkeyEnabledCheck.Checked = _settings.Hotkey.Enabled;
                _capturedHotkeyModifiers = _settings.Hotkey.PreferredModifiers;
                _capturedHotkeyKey = _settings.Hotkey.PreferredKey;
                UpdateHotkeyBox();
                _hotkeyStatusLabel.Text = string.IsNullOrEmpty(_settings.Hotkey.LastStatus) ? "保存后将注册快捷键。" : _settings.Hotkey.LastStatus;

                SelectComboValue(_renderModeCombo, _settings.Appearance.RenderMode);
                SelectComboValue(_themeCombo, _settings.Appearance.Theme);
                _systemAccentCheck.Checked = _settings.Appearance.UseSystemAccent;
                _acrylicCheck.Checked = _settings.Appearance.UseAcrylic;
                _animationCheck.Checked = _settings.Appearance.AnimationEnabled;
                _reducedMotionCheck.Checked = _settings.Appearance.ReducedMotion;
                _animationSpeed.Value = _settings.Appearance.AnimationSpeedPercent;
                _opacity.Value = _settings.Window.OpacityPercent;

                _streamCheck.Checked = _settings.Chat.StreamResponses;
                _historyCheck.Checked = _settings.Chat.SaveHistory;
                _tempBox.Value = _settings.Chat.Temperature;
                _maxTokensBox.Value = _settings.Chat.MaxTokens;

                _personaCombo.Items.Clear();
                foreach (PersonaProfile persona in _settings.Personas)
                    _personaCombo.Items.Add(new PersonaItem(persona));
                SelectPersona(_settings.Chat.CurrentPersonaId);

                _topMostCheck.Checked = _settings.Window.TopMost;
                _startWithWindowsCheck.Checked = _settings.Startup.StartWithWindows;
                SelectComboValue(_edgeCombo, _settings.Window.Edge);
                _widthBox.Value = _settings.Window.Width;
                _maxConversationsBox.Value = _settings.Chat.MaxConversations;
                _maxMessagesBox.Value = _settings.Chat.MaxMessagesPerConversation;
            }
            finally
            {
                _loading = false;
            }
        }

        private void OnProviderChanged(object sender, EventArgs e)
        {
            if (_loading) return;
            SaveProviderFields();
            ProviderItem item = _providerCombo.SelectedItem as ProviderItem;
            _currentProvider = item == null ? null : item.Provider;
            LoadProviderFields();
        }

        private void LoadProviderFields()
        {
            if (_currentProvider == null) return;
            _baseUrlBox.Text = _currentProvider.BaseUrl;
            _keyBox.Text = _secrets.Load(_currentProvider.ApiKeySecretId);
            _modelPathBox.Text = _currentProvider.ModelListPath;
            _chatPathBox.Text = _currentProvider.ChatPath;
            _headersBox.Text = _currentProvider.ExtraHeaders;
            _modelCombo.Items.Clear();
            AddModelItemIfMissing(_currentProvider, _currentProvider.DefaultModelId);
            if (string.Equals(_settings.Chat.DefaultProviderId, _currentProvider.Id, StringComparison.OrdinalIgnoreCase))
                AddModelItemIfMissing(_currentProvider, _settings.Chat.DefaultModelId);
            _modelCombo.Text = string.Equals(_settings.Chat.DefaultProviderId, _currentProvider.Id, StringComparison.OrdinalIgnoreCase)
                ? _settings.Chat.DefaultModelId
                : _currentProvider.DefaultModelId;
            _probeLabel.Text = _secrets.Exists(_currentProvider.ApiKeySecretId) ? "已保存 API Key。可以重新检测模型。" : "尚未保存 API Key。";
        }

        private void SaveProviderFields()
        {
            if (_currentProvider == null) return;
            _currentProvider.BaseUrl = _baseUrlBox.Text.Trim();
            _currentProvider.ModelListPath = _modelPathBox.Text.Trim();
            _currentProvider.ChatPath = _chatPathBox.Text.Trim();
            _currentProvider.ExtraHeaders = _headersBox.Text;
            _currentProvider.DefaultModelId = _modelCombo.Text.Trim();
            _currentProvider.Normalize();
            if (_keyBox.Text.Trim().Length > 0) _secrets.Save(_currentProvider.ApiKeySecretId, _keyBox.Text.Trim());
        }

        private void ProbeCurrentProvider()
        {
            SaveProviderFields();
            ProviderConfig provider = _currentProvider;
            if (provider == null) return;
            string key = _keyBox.Text.Trim();
            _probeButton.Enabled = false;
            _probeLabel.Text = "正在检测连接...";
            ThreadPool.QueueUserWorkItem(delegate
            {
                ConnectionProbeResult result = _client.Probe(provider, key, 30000);
                SafeBeginInvoke(new Action(delegate
                {
                    _probeButton.Enabled = true;
                    _modelCombo.Items.Clear();
                    foreach (AiModelInfo model in result.Models)
                        _modelCombo.Items.Add(new ModelItem(provider, model.Id));
                    if (result.Models.Count > 0)
                    {
                        _modelCombo.Text = result.Models[0].Id;
                        provider.DefaultModelId = result.Models[0].Id;
                        _settings.Chat.DefaultProviderId = provider.Id;
                        _settings.Chat.DefaultModelId = result.Models[0].Id;
                    }
                    _probeLabel.Text = result.Success
                        ? "连接成功，延迟 " + result.LatencyMs.ToString() + " ms，可用模型 " + result.Models.Count.ToString() + " 个。"
                        : "连接失败：" + result.ErrorMessage;
                }));
            });
        }

        private void OnPersonaChanged(object sender, EventArgs e)
        {
            if (_loading) return;
            SavePersonaFields();
            PersonaItem item = _personaCombo.SelectedItem as PersonaItem;
            _currentPersona = item == null ? null : item.Persona;
            _personaPromptBox.Text = _currentPersona == null ? string.Empty : _currentPersona.Prompt;
        }

        private void SavePersonaFields()
        {
            if (_currentPersona != null)
                _currentPersona.Prompt = _personaPromptBox.Text;
        }

        private void SaveAll()
        {
            SaveProviderFields();
            SavePersonaFields();

            ProviderItem providerItem = _providerCombo.SelectedItem as ProviderItem;
            if (providerItem != null) _settings.Chat.DefaultProviderId = providerItem.Provider.Id;
            ModelItem modelItem = _modelCombo.SelectedItem as ModelItem;
            _settings.Chat.DefaultModelId = modelItem == null ? _modelCombo.Text.Trim() : modelItem.ModelId;

            _settings.Copilot.DefaultDisplayMode = SelectedValue<DisplayMode>(_displayModeCombo);
            _settings.Copilot.RememberLastMode = _rememberModeCheck.Checked;
            _settings.Copilot.ExpandAnimation = _expandAnimationCheck.Checked;
            _settings.Hotkey.Enabled = _hotkeyEnabledCheck.Checked;
            _settings.Hotkey.PreferredModifiers = _capturedHotkeyModifiers == 0 ? (HotkeyModifiers.Control | HotkeyModifiers.Shift) : _capturedHotkeyModifiers;
            _settings.Hotkey.PreferredKey = _capturedHotkeyKey == 0 ? (int)Keys.Z : _capturedHotkeyKey;
            _settings.Hotkey.FallbackModifiers = 0;
            _settings.Hotkey.FallbackKey = 0;

            _settings.Appearance.Theme = SelectedValue<ThemeMode>(_themeCombo);
            _settings.Appearance.RenderMode = SelectedValue<UiRenderMode>(_renderModeCombo);
            _settings.Appearance.UseSystemAccent = _systemAccentCheck.Checked;
            _settings.Appearance.UseAcrylic = _acrylicCheck.Checked;
            _settings.Appearance.AnimationEnabled = _animationCheck.Checked;
            _settings.Appearance.ReducedMotion = _reducedMotionCheck.Checked;
            _settings.Appearance.AnimationSpeedPercent = (int)_animationSpeed.Value;
            _settings.Window.OpacityPercent = (int)_opacity.Value;

            _settings.Chat.StreamResponses = _streamCheck.Checked;
            _settings.Chat.SaveHistory = _historyCheck.Checked;
            _settings.Chat.Temperature = _tempBox.Value;
            _settings.Chat.MaxTokens = (int)_maxTokensBox.Value;

            PersonaItem personaItem = _personaCombo.SelectedItem as PersonaItem;
            if (personaItem != null) _settings.Chat.CurrentPersonaId = personaItem.Persona.Id;

            _settings.Window.TopMost = _topMostCheck.Checked;
            _settings.Startup.StartWithWindows = _startWithWindowsCheck.Checked;
            _settings.Window.Edge = SelectedValue<SidebarEdge>(_edgeCombo);
            _settings.Window.Width = (int)_widthBox.Value;
            _settings.Chat.MaxConversations = (int)_maxConversationsBox.Value;
            _settings.Chat.MaxMessagesPerConversation = (int)_maxMessagesBox.Value;

            _settings.Normalize();
            SettingsStore.Save(_settings);
            StartupManager.SetRunAtLogin(_settings.Startup.StartWithWindows, Application.ExecutablePath);
            if (SettingsSaved != null) SettingsSaved(this, EventArgs.Empty);
        }

        private void OnHotkeyBoxKeyDown(object sender, KeyEventArgs e)
        {
            e.SuppressKeyPress = true;
            if (e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.Menu || e.KeyCode == Keys.ShiftKey || e.KeyCode == Keys.LWin || e.KeyCode == Keys.RWin)
                return;

            int modifiers = HotkeyManager.ModifiersFromKeys(e.Modifiers);
            if (modifiers == 0)
            {
                _hotkeyStatusLabel.Text = "请至少搭配 Ctrl、Alt 或 Shift；默认推荐 Ctrl+Shift+Z。";
                return;
            }

            _capturedHotkeyModifiers = modifiers;
            _capturedHotkeyKey = (int)e.KeyCode;
            UpdateHotkeyBox();
            _hotkeyStatusLabel.Text = "保存后将尝试注册 " + _hotkeyBox.Text + "。";
        }

        private void UpdateHotkeyBox()
        {
            if (_hotkeyBox != null)
                _hotkeyBox.Text = HotkeyManager.Format(_capturedHotkeyModifiers, _capturedHotkeyKey);
        }

        private void DrawProviderComboItem(object sender, DrawItemEventArgs e)
        {
            e.DrawBackground();
            if (e.Index < 0) return;
            ComboBox combo = sender as ComboBox;
            ProviderItem item = combo == null ? null : combo.Items[e.Index] as ProviderItem;
            if (item == null) return;
            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            Color textColor = selected ? SystemColors.HighlightText : _theme.Text;
            Rectangle icon = new Rectangle(e.Bounds.Left + 6, e.Bounds.Top + 4, 18, 18);
            ProviderBadgeRenderer.Draw(e.Graphics, item.Provider.Id, item.Provider.Name, icon);
            TextRenderer.DrawText(e.Graphics, item.Provider.Name, e.Font, new Rectangle(e.Bounds.Left + 30, e.Bounds.Top + 1, e.Bounds.Width - 34, e.Bounds.Height - 2), textColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            e.DrawFocusRectangle();
        }

        private void DrawModelComboItem(object sender, DrawItemEventArgs e)
        {
            e.DrawBackground();
            if (e.Index < 0) return;
            ComboBox combo = sender as ComboBox;
            object raw = combo == null ? null : combo.Items[e.Index];
            ModelItem item = raw as ModelItem;
            string providerId = item == null ? (_currentProvider == null ? "custom" : _currentProvider.Id) : item.ProviderId;
            string providerName = item == null ? (_currentProvider == null ? "模型" : _currentProvider.Name) : item.ProviderName;
            string modelId = item == null ? Convert.ToString(raw) : item.ModelId;
            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            Color textColor = selected ? SystemColors.HighlightText : _theme.Text;
            Rectangle icon = new Rectangle(e.Bounds.Left + 6, e.Bounds.Top + 4, 18, 18);
            ProviderBadgeRenderer.Draw(e.Graphics, providerId, providerName, icon);
            TextRenderer.DrawText(e.Graphics, modelId, e.Font, new Rectangle(e.Bounds.Left + 30, e.Bounds.Top + 1, e.Bounds.Width - 34, e.Bounds.Height - 2), textColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            e.DrawFocusRectangle();
        }

        private static TabPage MakeTab(string title, Control content)
        {
            TabPage page = new TabPage(title);
            content.Dock = DockStyle.Fill;
            page.Controls.Add(content);
            return page;
        }

        private TableLayoutPanel CreateTable()
        {
            TableLayoutPanel table = new TableLayoutPanel();
            table.Dock = DockStyle.Top;
            table.AutoSize = true;
            table.ColumnCount = 2;
            table.Padding = new Padding(18, 16, 18, 16);
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 142));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            table.BackColor = _theme.WindowBottom;
            return table;
        }

        private Control Wrap(Control child)
        {
            Panel panel = new Panel();
            panel.Dock = DockStyle.Fill;
            panel.AutoScroll = true;
            panel.BackColor = _theme.WindowBottom;
            child.Dock = DockStyle.Top;
            panel.Controls.Add(child);
            return panel;
        }

        private void AddRow(TableLayoutPanel table, string label, Control control)
        {
            int row = table.RowCount++;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Label l = new Label();
            l.Text = label;
            l.AutoSize = true;
            l.Margin = new Padding(3, 9, 12, 8);
            l.ForeColor = _theme.SubText;
            control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            control.Margin = new Padding(3, 5, 3, 5);
            table.Controls.Add(l, 0, row);
            table.Controls.Add(control, 1, row);
        }

        private void AddSection(TableLayoutPanel table, string title)
        {
            int row = table.RowCount++;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Label label = new Label();
            label.Text = title;
            label.AutoSize = true;
            label.Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold);
            label.ForeColor = _theme.Text;
            label.Margin = new Padding(3, row == 0 ? 0 : 14, 3, 6);
            table.Controls.Add(label, 0, row);
            table.SetColumnSpan(label, 2);
        }

        private ComboBox CreateCombo()
        {
            ComboBox combo = new ComboBox();
            combo.DropDownStyle = ComboBoxStyle.DropDownList;
            combo.Width = 260;
            return combo;
        }

        private NumericUpDown CreateNumeric(decimal min, decimal max, decimal value, decimal increment, int decimals)
        {
            NumericUpDown n = new NumericUpDown();
            n.Minimum = min;
            n.Maximum = max;
            n.Value = Math.Max(min, Math.Min(max, value));
            n.Increment = increment;
            n.DecimalPlaces = decimals;
            n.Width = 120;
            return n;
        }

        private Label CreateHint(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.MaximumSize = new Size(470, 0);
            label.ForeColor = _theme.SubText;
            return label;
        }

        private Button CreateDialogButton(string text, bool primary)
        {
            Button button = new Button();
            button.Text = text;
            button.Size = new Size(90, 30);
            button.FlatStyle = FlatStyle.Flat;
            button.Font = new Font("Microsoft YaHei UI", 9f);
            button.TabStop = true;
            if (primary)
            {
                button.BackColor = _theme.Accent;
                button.ForeColor = _theme.AccentText;
                button.FlatAppearance.BorderColor = _theme.Accent;
            }
            else
            {
                button.BackColor = _theme.SurfaceAlt;
                button.ForeColor = _theme.Text;
                button.FlatAppearance.BorderColor = _theme.Border;
            }
            button.FlatAppearance.BorderSize = 1;
            return button;
        }

        private void ApplyControlTheme(Control root)
        {
            foreach (Control control in root.Controls)
            {
                TextBox textBox = control as TextBox;
                ComboBox combo = control as ComboBox;
                NumericUpDown numeric = control as NumericUpDown;
                CheckBox check = control as CheckBox;
                TabControl tabs = control as TabControl;
                TabPage page = control as TabPage;

                if (textBox != null)
                {
                    textBox.BackColor = _theme.Surface;
                    textBox.ForeColor = _theme.Text;
                    textBox.BorderStyle = BorderStyle.FixedSingle;
                }
                else if (combo != null)
                {
                    combo.BackColor = _theme.Surface;
                    combo.ForeColor = _theme.Text;
                }
                else if (numeric != null)
                {
                    numeric.BackColor = _theme.Surface;
                    numeric.ForeColor = _theme.Text;
                }
                else if (check != null)
                {
                    check.BackColor = _theme.WindowBottom;
                    check.ForeColor = _theme.Text;
                }
                else if (tabs != null)
                {
                    tabs.BackColor = _theme.WindowBottom;
                    tabs.ForeColor = _theme.Text;
                }
                else if (page != null)
                {
                    page.BackColor = _theme.WindowBottom;
                    page.ForeColor = _theme.Text;
                }
                ApplyControlTheme(control);
            }
        }

        private void AddModelItemIfMissing(ProviderConfig provider, string modelId)
        {
            if (string.IsNullOrEmpty(modelId)) return;
            foreach (object raw in _modelCombo.Items)
            {
                ModelItem item = raw as ModelItem;
                if (item != null && string.Equals(item.ModelId, modelId, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            _modelCombo.Items.Add(new ModelItem(provider, modelId));
        }

        private void SelectProvider(string id)
        {
            for (int i = 0; i < _providerCombo.Items.Count; i++)
            {
                ProviderItem item = _providerCombo.Items[i] as ProviderItem;
                if (item != null && string.Equals(item.Provider.Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    _providerCombo.SelectedIndex = i;
                    _currentProvider = item.Provider;
                    LoadProviderFields();
                    return;
                }
            }
            if (_providerCombo.Items.Count > 0)
            {
                _providerCombo.SelectedIndex = 0;
                ProviderItem item = _providerCombo.SelectedItem as ProviderItem;
                _currentProvider = item == null ? null : item.Provider;
                LoadProviderFields();
            }
        }

        private void SelectPersona(string id)
        {
            for (int i = 0; i < _personaCombo.Items.Count; i++)
            {
                PersonaItem item = _personaCombo.Items[i] as PersonaItem;
                if (item != null && string.Equals(item.Persona.Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    _personaCombo.SelectedIndex = i;
                    _currentPersona = item.Persona;
                    _personaPromptBox.Text = item.Persona.Prompt;
                    return;
                }
            }
            if (_personaCombo.Items.Count > 0)
            {
                _personaCombo.SelectedIndex = 0;
                PersonaItem item = _personaCombo.SelectedItem as PersonaItem;
                _currentPersona = item == null ? null : item.Persona;
                _personaPromptBox.Text = _currentPersona == null ? string.Empty : _currentPersona.Prompt;
            }
        }

        private static void SelectComboValue<T>(ComboBox combo, T value)
        {
            for (int i = 0; i < combo.Items.Count; i++)
            {
                ComboItem<T> item = combo.Items[i] as ComboItem<T>;
                if (item != null && object.Equals(item.Value, value))
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }
            if (combo.Items.Count > 0) combo.SelectedIndex = 0;
        }

        private static T SelectedValue<T>(ComboBox combo)
        {
            ComboItem<T> item = combo.SelectedItem as ComboItem<T>;
            if (item == null) return default(T);
            return item.Value;
        }

        private void SafeBeginInvoke(Delegate method)
        {
            try
            {
                if (!IsDisposed && IsHandleCreated) BeginInvoke(method);
            }
            catch { }
        }

        private sealed class ComboItem<T>
        {
            public T Value;
            private readonly string _text;
            public ComboItem(T value, string text) { Value = value; _text = text; }
            public override string ToString() { return _text; }
        }

        private sealed class ProviderItem
        {
            public ProviderConfig Provider;
            public ProviderItem(ProviderConfig provider) { Provider = provider; }
            public override string ToString() { return Provider == null ? string.Empty : Provider.Name; }
        }

        private sealed class ModelItem
        {
            public string ProviderId;
            public string ProviderName;
            public string ModelId;

            public ModelItem(ProviderConfig provider, string modelId)
            {
                ProviderId = provider == null ? "custom" : provider.Id;
                ProviderName = provider == null ? "模型" : provider.Name;
                ModelId = modelId ?? string.Empty;
            }

            public override string ToString()
            {
                return ModelId;
            }
        }

        private sealed class PersonaItem
        {
            public PersonaProfile Persona;
            public PersonaItem(PersonaProfile persona) { Persona = persona; }
            public override string ToString() { return Persona == null ? string.Empty : Persona.Name; }
        }
    }
}
