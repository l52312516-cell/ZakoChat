using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace ZakoChat
{
    public sealed class QuickPromptForm : Form, IQuickPromptSurface
    {
        private readonly AppSettings _settings;
        private readonly TextBox _input;
        private readonly Button _sendButton;
        private readonly Button _expandButton;
        private readonly Button _settingsButton;
        private readonly Button _hideButton;
        private readonly ToolTip _tips;
        private readonly Timer _animationTimer;
        private ThemePalette _theme;
        private Rectangle _shownBounds;
        private Rectangle _hiddenBounds;
        private DateTime _animationStart;
        private bool _targetVisible;
        private string _providerId;
        private string _providerName;
        private string _modelName;
        private string _memoryText;
        private bool _busy;

        public event Action<string> SendRequested;
        public event EventHandler ExpandRequested;
        public event EventHandler SettingsRequested;
        public event EventHandler HideRequested;

        public QuickPromptForm(AppSettings settings)
        {
            _settings = settings;
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
            Size = new Size(704, 136);
            MinimumSize = new Size(560, 132);
            Font = new Font("Microsoft YaHei UI", 9f);
            KeyPreview = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            _input = new TextBox();
            _input.Multiline = true;
            _input.BorderStyle = BorderStyle.None;
            _input.AcceptsReturn = true;
            _input.ScrollBars = ScrollBars.None;
            _input.Font = new Font("Microsoft YaHei UI", 10.5f);
            _input.KeyDown += OnInputKeyDown;

            _sendButton = CreateButton("发送");
            _sendButton.Click += delegate { SendFromInput(); };
            _expandButton = CreateButton("展开");
            _expandButton.Click += delegate { if (ExpandRequested != null) ExpandRequested(this, EventArgs.Empty); };
            _settingsButton = CreateButton("设置");
            _settingsButton.Click += delegate { if (SettingsRequested != null) SettingsRequested(this, EventArgs.Empty); };
            _hideButton = CreateButton("隐藏");
            _hideButton.Click += delegate { if (HideRequested != null) HideRequested(this, EventArgs.Empty); };
            _tips = new ToolTip();
            _tips.SetToolTip(_sendButton, "发送");
            _tips.SetToolTip(_expandButton, "展开侧栏");
            _tips.SetToolTip(_settingsButton, "设置");
            _tips.SetToolTip(_hideButton, "隐藏");

            Controls.Add(_input);
            Controls.Add(_sendButton);
            Controls.Add(_expandButton);
            Controls.Add(_settingsButton);
            Controls.Add(_hideButton);

            _animationTimer = new Timer();
            _animationTimer.Interval = 15;
            _animationTimer.Tick += OnAnimationTick;

            Resize += delegate { LayoutControls(); ApplyRoundedCorners(); Invalidate(); };
            ApplySettings();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyRoundedCorners();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (LinearGradientBrush brush = new LinearGradientBrush(ClientRectangle, _theme.WindowTop, _theme.WindowBottom, LinearGradientMode.Vertical))
                e.Graphics.FillRectangle(brush, ClientRectangle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            Rectangle outer = new Rectangle(0, 0, Width - 1, Height - 1);
            using (Pen outerPen = new Pen(_theme.Border))
                UiDrawing.DrawRoundedRectangle(e.Graphics, outerPen, outer, 22);

            Rectangle shell = new Rectangle(14, 44, Width - 28, Height - 58);
            using (SolidBrush brush = new SolidBrush(_theme.InputBack))
            using (Pen pen = new Pen(_theme.Border))
            {
                UiDrawing.FillRoundedRectangle(e.Graphics, brush, shell, 20);
                UiDrawing.DrawRoundedRectangle(e.Graphics, pen, new Rectangle(shell.X, shell.Y, shell.Width - 1, shell.Height - 1), 20);
            }

            Rectangle badge = new Rectangle(18, 13, 18, 18);
            ProviderBadgeRenderer.Draw(e.Graphics, _providerId, _providerName, badge);
            using (Font titleFont = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold))
            {
                TextRenderer.DrawText(e.Graphics, AppInfo.AppName, titleFont, new Rectangle(42, 8, 120, 26), _theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }

            string status = BuildStatusText();
            TextRenderer.DrawText(e.Graphics, status, Font, new Rectangle(150, 8, Math.Max(80, Width - 294), 26), _theme.SubText, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        public void ApplySettings()
        {
            _settings.Normalize();
            _theme = ThemePalette.Resolve(_settings);
            TopMost = _settings.Window.TopMost;
            BackColor = _theme.WindowBottom;
            _input.BackColor = _theme.InputBack;
            _input.ForeColor = _theme.Text;
            ApplyButtonStyle(_sendButton, true);
            ApplyButtonStyle(_expandButton, false);
            ApplyButtonStyle(_settingsButton, false);
            ApplyButtonStyle(_hideButton, false);
            LayoutControls();
            Invalidate(true);
        }

        public void SetIcon(Icon icon)
        {
            Icon = icon;
        }

        public void SetStatus(string providerId, string providerName, string modelName, string memoryText)
        {
            _providerId = providerId ?? string.Empty;
            _providerName = providerName ?? string.Empty;
            _modelName = modelName ?? string.Empty;
            _memoryText = memoryText ?? string.Empty;
            Invalidate();
        }

        public void SetBusy(bool busy)
        {
            _busy = busy;
            _sendButton.Enabled = !busy;
            Invalidate();
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
                Activate();
                _input.Focus();
                return;
            }

            Bounds = _hiddenBounds;
            Opacity = 0.08;
            _targetVisible = true;
            _animationStart = DateTime.UtcNow;
            Show();
            _animationTimer.Start();
            Activate();
            _input.Focus();
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

        private void OnAnimationTick(object sender, EventArgs e)
        {
            int duration = ScaleDuration(167);
            double t = (DateTime.UtcNow - _animationStart).TotalMilliseconds / Math.Max(1, duration);
            if (t >= 1)
            {
                _animationTimer.Stop();
                if (_targetVisible)
                {
                    Bounds = _shownBounds;
                    Opacity = _settings.Window.OpacityPercent / 100.0;
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
            int x = (int)(from.X + (to.X - from.X) * eased);
            int y = (int)(from.Y + (to.Y - from.Y) * eased);
            Bounds = new Rectangle(x, y, to.Width, to.Height);
            double maxOpacity = _settings.Window.OpacityPercent / 100.0;
            Opacity = _targetVisible ? 0.08 + (maxOpacity - 0.08) * eased : maxOpacity - (maxOpacity - 0.08) * eased;
        }

        private void PositionQuick()
        {
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            int width = Math.Min(704, Math.Max(560, area.Width - 48));
            int height = 136;
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

        private void LayoutControls()
        {
            int shellLeft = 32;
            int shellTop = 58;
            int shellRight = Width - 32;
            int shellBottom = Height - 24;
            _sendButton.Size = new Size(56, 34);
            _expandButton.Size = new Size(48, 28);
            _settingsButton.Size = new Size(48, 28);
            _hideButton.Size = new Size(48, 28);
            _sendButton.Location = new Point(shellRight - 64, shellBottom - 40);
            _hideButton.Location = new Point(Width - 64, 10);
            _settingsButton.Location = new Point(Width - 118, 10);
            _expandButton.Location = new Point(Width - 172, 10);
            _input.Location = new Point(shellLeft, shellTop);
            _input.Size = new Size(Math.Max(100, shellRight - shellLeft - 74), Math.Max(44, shellBottom - shellTop - 2));
            ApplyButtonRegion(_sendButton, 18);
            ApplyButtonRegion(_expandButton, 14);
            ApplyButtonRegion(_settingsButton, 14);
            ApplyButtonRegion(_hideButton, 14);
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
            if (e.KeyCode == Keys.Enter && (e.Control || !e.Shift && !e.Alt))
            {
                e.SuppressKeyPress = true;
                SendFromInput();
            }
            else if (e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;
                if (HideRequested != null) HideRequested(this, EventArgs.Empty);
            }
        }

        private void SendFromInput()
        {
            string text = _input.Text.Trim();
            if (text.Length == 0) return;
            _input.Clear();
            if (SendRequested != null) SendRequested(text);
        }

        private string BuildStatusText()
        {
            string provider = string.IsNullOrEmpty(_providerName) ? "未配置服务商" : _providerName;
            string model = string.IsNullOrEmpty(_modelName) ? "未选择模型" : _modelName;
            string status = _busy ? "正在生成回复" : "快速提问";
            if (!string.IsNullOrEmpty(_memoryText))
                return status + " · " + provider + " · " + model + " · " + _memoryText;
            return status + " · " + provider + " · " + model;
        }

        private int ScaleDuration(int baseMs)
        {
            return Math.Max(45, baseMs * 100 / Math.Max(60, _settings.Appearance.AnimationSpeedPercent));
        }

        private void ApplyRoundedCorners()
        {
            try
            {
                Region old = Region;
                IntPtr rgn = NativeUi.CreateRoundRectRgn(0, 0, Width + 1, Height + 1, 24, 24);
                Region = Region.FromHrgn(rgn);
                NativeUi.DeleteObject(rgn);
                if (old != null) old.Dispose();
            }
            catch { }
        }
    }

    public sealed class ConversationListControl : Control
    {
        private readonly List<ChatSession> _sessions;
        private ThemePalette _theme;
        private string _selectedId;
        private int _hoverIndex;
        private bool _collapsed;

        public event EventHandler NewChatRequested;
        public event Action<string> SessionSelected;

        public ConversationListControl()
        {
            _sessions = new List<ChatSession>();
            _theme = ThemePalette.Resolve(AppSettings.CreateDefault());
            _selectedId = string.Empty;
            _hoverIndex = -2;
            Width = 180;
            Font = new Font("Microsoft YaHei UI", 8.7f);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        }

        public void SetTheme(ThemePalette theme)
        {
            _theme = theme;
            BackColor = _theme.SurfaceAlt;
            ForeColor = _theme.Text;
            Invalidate();
        }

        public void SetSessions(IList<ChatSession> sessions, string selectedId)
        {
            _sessions.Clear();
            if (sessions != null)
            {
                foreach (ChatSession session in sessions)
                {
                    if (session != null) _sessions.Add(session);
                }
            }
            _selectedId = selectedId ?? string.Empty;
            Invalidate();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using (SolidBrush brush = new SolidBrush(_theme.SurfaceAlt))
                e.Graphics.FillRectangle(brush, ClientRectangle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            using (Pen pen = new Pen(_theme.Border))
                e.Graphics.DrawLine(pen, Width - 1, 0, Width - 1, Height);

            using (Font titleFont = new Font("Microsoft YaHei UI", 8.4f, FontStyle.Bold))
                TextRenderer.DrawText(e.Graphics, _collapsed ? "☰" : "历史", titleFont, new Rectangle(12, 10, Width - 48, 22), _theme.SubText, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

            DrawToggle(e.Graphics, ToggleRect(), _hoverIndex == -3);

            Rectangle newRect = NewChatRect();
            DrawItem(e.Graphics, newRect, _collapsed ? "+" : "新建对话", true, _hoverIndex == -1);

            int y = 72;
            for (int i = 0; i < _sessions.Count; i++)
            {
                ChatSession session = _sessions[i];
                Rectangle item = new Rectangle(8, y, Width - 16, 40);
                bool selected = string.Equals(session.Id, _selectedId, StringComparison.OrdinalIgnoreCase);
                DrawItem(e.Graphics, item, _collapsed ? ShortTitle(session) : DisplayTitle(session), selected, _hoverIndex == i);
                y += 44;
                if (y > Height + 44) break;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int hover = HitTest(e.Location);
            if (hover != _hoverIndex)
            {
                _hoverIndex = hover;
                Cursor = hover >= -1 || hover == -3 ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hoverIndex = -2;
            Cursor = Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int hit = HitTest(e.Location);
            if (hit == -3)
            {
                _collapsed = !_collapsed;
                Width = _collapsed ? 52 : 180;
                Invalidate();
            }
            else if (hit == -1)
            {
                if (NewChatRequested != null) NewChatRequested(this, EventArgs.Empty);
            }
            else if (hit >= 0 && hit < _sessions.Count)
            {
                if (SessionSelected != null) SessionSelected(_sessions[hit].Id);
            }
        }

        private void DrawItem(Graphics g, Rectangle rect, string text, bool selected, bool hover)
        {
            Color fill = selected ? UiDrawing.WithAlpha(_theme.Accent, 38) : (hover ? UiDrawing.WithAlpha(_theme.Text, _theme.IsLight ? 16 : 24) : Color.Transparent);
            if (fill.A > 0)
            {
                using (SolidBrush brush = new SolidBrush(fill))
                    UiDrawing.FillRoundedRectangle(g, brush, rect, 9);
            }

            Color textColor = selected ? _theme.Text : _theme.SubText;
            TextFormatFlags flags = _collapsed
                ? TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
                : TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
            TextRenderer.DrawText(g, text, Font, new Rectangle(rect.X + 10, rect.Y + 2, rect.Width - 20, rect.Height - 4), textColor, flags);
        }

        private void DrawToggle(Graphics g, Rectangle rect, bool hover)
        {
            Color fill = hover ? UiDrawing.WithAlpha(_theme.Text, _theme.IsLight ? 16 : 24) : Color.Transparent;
            if (fill.A > 0)
            {
                using (SolidBrush brush = new SolidBrush(fill))
                    UiDrawing.FillRoundedRectangle(g, brush, rect, 8);
            }
            string text = _collapsed ? "›" : "‹";
            TextRenderer.DrawText(g, text, Font, rect, _theme.SubText, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        private Rectangle NewChatRect()
        {
            return new Rectangle(8, 36, Width - 16, 30);
        }

        private Rectangle ToggleRect()
        {
            return new Rectangle(Math.Max(8, Width - 36), 8, 26, 24);
        }

        private int HitTest(Point point)
        {
            if (ToggleRect().Contains(point)) return -3;
            if (NewChatRect().Contains(point)) return -1;
            int y = 72;
            for (int i = 0; i < _sessions.Count; i++)
            {
                Rectangle item = new Rectangle(8, y, Width - 16, 40);
                if (item.Contains(point)) return i;
                y += 44;
            }
            return -2;
        }

        private string DisplayTitle(ChatSession session)
        {
            if (session == null || string.IsNullOrEmpty(session.Title)) return "新对话";
            return session.Title;
        }

        private string ShortTitle(ChatSession session)
        {
            string title = DisplayTitle(session);
            return title.Length == 0 ? "…" : title.Substring(0, 1);
        }
    }
}
