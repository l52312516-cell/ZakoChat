using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Windows.Forms;

namespace ZakoChat
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e) { CrashLog.Write(e.Exception); };
                AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
                {
                    Exception ex = e.ExceptionObject as Exception;
                    if (ex != null) CrashLog.Write(ex);
                };
                Application.Run(new ZakoAppContext());
            }
            catch (Exception ex)
            {
                CrashLog.Write(ex);
                MessageBox.Show(ex.Message, AppInfo.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    public sealed class ZakoAppContext : ApplicationContext
    {
        private readonly SecretStore _secrets;
        private readonly IChatClient _client;
        private readonly IImageGenerationClient _imageClient;
        private readonly System.Windows.Forms.Timer _statusTimer;
        private readonly HotkeyManager _hotkeys;
        private AppSettings _settings;
        private ChatSession _session;
        private IQuickPromptSurface _quick;
        private ISidebarSurface _sidebar;
        private Form _settingsForm;
        private NotifyIcon _tray;
        private ToolStripMenuItem _showHideItem;
        private CancellationTokenSource _cancelSource;
        private bool _busy;
        private Icon _appIcon;

        public ZakoAppContext()
        {
            SettingsRepairResult repair;
            _settings = SettingsStore.Load(out repair);
            _settings.Normalize();

            _secrets = new SecretStore();
            _client = new OpenAiCompatibleChatClient();
            _imageClient = (IImageGenerationClient)_client;
            _session = HistoryStore.LoadLatest(_settings);
            _appIcon = LoadAppIcon();

            _quick = CopilotSurfaceFactory.CreateQuickPrompt(_settings);
            _quick.SetIcon(_appIcon);
            _quick.SendRequested += delegate(string text) { SendFromQuickPrompt(text); };
            _quick.ExpandRequested += delegate { ShowFullSidebar(); };
            _quick.SettingsRequested += delegate { OpenSettings(); };
            _quick.HideRequested += delegate { HideAll(); };

            _hotkeys = new HotkeyManager();
            _hotkeys.HotkeyPressed += delegate { ToggleVisibleByHotkey(); };
            RegisterHotkey();

            UpdateProviderStatus();
            BuildTray();

            _statusTimer = new System.Windows.Forms.Timer();
            _statusTimer.Interval = 5000;
            _statusTimer.Tick += delegate { UpdateProviderStatus(); };
            _statusTimer.Start();

            DisplayMode startupMode = _settings.Copilot.RememberLastMode ? _settings.Copilot.LastDisplayMode : _settings.Copilot.DefaultDisplayMode;
            if (startupMode == DisplayMode.FullSidebar)
                ShowFullSidebar();
            else
                ShowQuickPrompt();

            ThreadPool.QueueUserWorkItem(delegate
            {
                Thread.Sleep(2500);
                MemoryTrimmer.TrimCurrentProcess();
            });
        }

        private void BuildTray()
        {
            _tray = new NotifyIcon();
            _tray.Icon = _appIcon;
            _tray.Text = AppInfo.AppName;
            _tray.Visible = true;

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.ShowImageMargin = false;
            menu.Opening += delegate { UpdateTrayText(); };
            menu.Items.Add("快速提问", null, delegate { ShowQuickPrompt(); });
            menu.Items.Add("展开侧栏", null, delegate { ShowFullSidebar(); });
            _showHideItem = new ToolStripMenuItem("隐藏全部", null, delegate { ToggleVisible(); });
            menu.Items.Add(_showHideItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("新建对话", null, delegate { NewChat(); });
            menu.Items.Add("设置...", null, delegate { OpenSettings(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("清空本地历史", null, delegate { ClearHistory(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, delegate { ExitApp(); });
            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += delegate { WakeQuickPrompt(); };
            UpdateTrayText();
        }

        private ISidebarSurface EnsureSidebar()
        {
            if (_sidebar != null && !_sidebar.IsDisposed) return _sidebar;
            _sidebar = CopilotSurfaceFactory.CreateSidebar(_settings, SendUserMessage, StopGeneration, OpenSettings, HideAll);
            _sidebar.SetIcon(_appIcon);
            _sidebar.SidebarStateChanged += delegate { UpdateTrayText(); };
            _sidebar.NewChatRequested += delegate { NewChat(); };
            _sidebar.SessionSelected += delegate(string id) { SelectSession(id); };
            _sidebar.SessionDeleteRequested += delegate(string id) { DeleteSession(id); };
            _sidebar.EdgeToggleRequested += delegate { ToggleSidebarEdge(); };
            _sidebar.ImageGenerationRequested += delegate(string prompt) { GenerateImage(prompt); };
            _sidebar.VisionSendRequested += delegate(string text, string imagePath) { SendVisionMessage(text, imagePath); };
            _sidebar.LoadMessages(_session.Messages);
            RefreshHistoryList();
            UpdateProviderStatus();
            return _sidebar;
        }

        private void RegisterHotkey()
        {
            if (_hotkeys == null) return;
            _settings.Hotkey.Normalize();
            _hotkeys.RegisterFromSettings(_settings.Hotkey);
            SettingsStore.Save(_settings);
        }

        private void WakeQuickPrompt()
        {
            if (_quick != null && _quick.Visible)
            {
                _quick.Activate();
                return;
            }
            if (_sidebar != null && _sidebar.Visible)
            {
                _sidebar.Activate();
                return;
            }
            ShowQuickPrompt();
        }

        private void ToggleVisibleByHotkey()
        {
            if (AnyWindowVisible())
            {
                HideAll();
                return;
            }
            ShowQuickPrompt();
        }

        private void ToggleVisible()
        {
            if (AnyWindowVisible())
                HideAll();
            else
                ShowQuickPrompt();
        }

        private void ShowQuickPrompt()
        {
            if (_sidebar != null && (_sidebar.State == SidebarState.Shown || _sidebar.State == SidebarState.Showing))
                _sidebar.HideSidebarAnimated();
            if (_quick != null)
                _quick.ShowQuickAnimated();
            _settings.Copilot.LastDisplayMode = DisplayMode.QuickPrompt;
            UpdateTrayText();
        }

        private void ShowFullSidebar()
        {
            EnsureSidebar();
            if (_quick != null) _quick.HideQuick();
            RefreshHistoryList();
            if (_sidebar != null) _sidebar.ShowSidebarAnimated();
            _settings.Copilot.LastDisplayMode = DisplayMode.FullSidebar;
            UpdateTrayText();
        }

        private void HideAll()
        {
            if (_quick != null) _quick.HideQuickAnimated();
            if (_sidebar != null) _sidebar.HideSidebarAnimated();
            UpdateTrayText();
        }

        private bool AnyWindowVisible()
        {
            bool quickVisible = _quick != null && _quick.Visible;
            bool sidebarVisible = _sidebar != null && (_sidebar.State == SidebarState.Shown || _sidebar.State == SidebarState.Showing);
            return quickVisible || sidebarVisible;
        }

        private void UpdateTrayText()
        {
            if (_showHideItem != null)
                _showHideItem.Text = AnyWindowVisible() ? "隐藏全部" : "显示快速提问";
            if (_tray != null)
            {
                string stateText = AnyWindowVisible() ? "已显示" : "已隐藏";
                string text = AppInfo.AppName + " - " + stateText;
                _tray.Text = text.Length > 63 ? AppInfo.AppName : text;
            }
        }

        private void NewChat()
        {
            if (_busy) StopGeneration();
            EnsureSidebar();
            _session = new ChatSession();
            _sidebar.LoadMessages(_session.Messages);
            RefreshHistoryList();
            HistoryStore.SaveLatest(_settings, _session);
            ShowFullSidebar();
        }

        private void SelectSession(string sessionId)
        {
            if (_busy) StopGeneration();
            EnsureSidebar();
            ChatSession selected = HistoryStore.LoadById(_settings, sessionId);
            if (selected == null) return;
            _session = selected;
            _sidebar.LoadMessages(_session.Messages);
            RefreshHistoryList();
            ShowFullSidebar();
        }

        private void DeleteSession(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId)) return;
            if (_busy) StopGeneration();
            HistoryStore.DeleteSession(sessionId);
            if (_session != null && string.Equals(_session.Id, sessionId, StringComparison.OrdinalIgnoreCase))
            {
                _session = new ChatSession();
                if (_sidebar != null) _sidebar.LoadMessages(_session.Messages);
            }
            RefreshHistoryList();
        }

        private void ToggleSidebarEdge()
        {
            _settings.Window.Edge = _settings.Window.Edge == SidebarEdge.Right ? SidebarEdge.Left : SidebarEdge.Right;
            SettingsStore.Save(_settings);
            if (_sidebar != null)
            {
                _sidebar.ApplySettings();
                if (_sidebar.State == SidebarState.Shown || _sidebar.State == SidebarState.Showing)
                    _sidebar.ShowSidebarAnimated();
            }
        }

        private void ClearHistory()
        {
            HistoryStore.Clear();
            _session = new ChatSession();
            if (_sidebar != null) _sidebar.LoadMessages(_session.Messages);
            RefreshHistoryList();
        }

        private void OpenSettings()
        {
            if (_settingsForm != null && !_settingsForm.IsDisposed)
            {
                _settingsForm.Show();
                _settingsForm.Activate();
                return;
            }

            _settingsForm = SettingsSurfaceFactory.CreateSettingsForm(_settings, _secrets, _client);
            if (_appIcon != null) _settingsForm.Icon = _appIcon;
            ISettingsSurface settingsSurface = _settingsForm as ISettingsSurface;
            if (settingsSurface != null) settingsSurface.SettingsSaved += delegate
            {
                _settings.Normalize();
                if (_quick != null) _quick.ApplySettings();
                if (_sidebar != null) _sidebar.ApplySettings();
                RegisterHotkey();
                RefreshHistoryList();
                UpdateProviderStatus();
                UpdateTrayText();
            };
            _settingsForm.FormClosed += delegate { _settingsForm = null; MemoryTrimmer.TrimCurrentProcess(); };
            _settingsForm.Show();
            _settingsForm.Activate();
        }

        private void SendFromQuickPrompt(string text)
        {
            ShowFullSidebar();
            SendUserMessage(text);
        }

        private void SendUserMessage(string text)
        {
            SendUserMessageCore(text, string.Empty);
        }

        private void SendVisionMessage(string text, string imagePath)
        {
            SendUserMessageCore(text, imagePath);
        }

        private void SendUserMessageCore(string text, string imagePath)
        {
            if (_busy) return;
            _settings.Normalize();

            ProviderConfig provider = _settings.FindProvider(_settings.Chat.DefaultProviderId);
            if (provider == null)
            {
                ShowFullSidebar();
                _sidebar.AddMessage(new ChatMessage("assistant", "尚未选择服务商。请打开设置，选择一个服务商或自定义接口。"));
                return;
            }

            if (!string.IsNullOrEmpty(imagePath) && (!provider.SupportsVision || provider.VisionApiKind == VisionApiKind.None))
            {
                ShowFullSidebar();
                _sidebar.AddMessage(new ChatMessage("assistant", provider.Name + " 当前未启用视觉输入。请在设置的高级选项中开启视觉能力，或切换支持视觉的模型。"));
                return;
            }

            string apiKey = _secrets.Load(provider.ApiKeySecretId);
            if (string.IsNullOrEmpty(apiKey))
            {
                ShowFullSidebar();
                _sidebar.AddMessage(new ChatMessage("assistant", provider.Name + " 尚未保存 API Key。请打开设置添加密钥。"));
                return;
            }

            if (string.IsNullOrEmpty(_settings.Chat.DefaultModelId))
            {
                ShowFullSidebar();
                _sidebar.AddMessage(new ChatMessage("assistant", "尚未选择 Model ID。请在设置里检测模型，或手动填写模型名称。"));
                return;
            }

            ChatMessage user = string.IsNullOrEmpty(imagePath) ? new ChatMessage("user", text) : ChatMessage.CreateVision("user", text, imagePath);
            _session.Messages.Add(user);
            if (string.IsNullOrEmpty(_session.Title) || _session.Title == "新对话" || _session.Messages.Count == 1)
                _session.Title = text.Length > 40 ? text.Substring(0, 40) : text;
            _sidebar.AddMessage(user);

            ChatMessage assistant = new ChatMessage("assistant", string.Empty);
            object assistantBubble = _sidebar.AddMessage(assistant);

            List<ChatMessage> requestMessages = new List<ChatMessage>();
            foreach (ChatMessage message in _session.Messages)
                requestMessages.Add(message);

            PersonaProfile persona = _settings.FindPersona(_settings.Chat.CurrentPersonaId);
            ChatOptions options = new ChatOptions();
            options.ModelId = _settings.Chat.DefaultModelId;
            options.Stream = _settings.Chat.StreamResponses && provider.SupportsStreaming;
            options.Temperature = _settings.Chat.Temperature;
            options.MaxTokens = _settings.Chat.MaxTokens;
            options.PersonaPrompt = persona == null ? string.Empty : persona.Prompt;

            _busy = true;
            _cancelSource = new CancellationTokenSource();
            SetBusy(true);
            UpdateProviderStatus();

            ThreadPool.QueueUserWorkItem(delegate
            {
                ChatResponse response = _client.SendChat(
                    provider,
                    apiKey,
                    requestMessages,
                    options,
                    delegate(string delta) { _sidebar.UpdateBubble(assistantBubble, delta, true); },
                    _cancelSource.Token);

                InvokeSidebar(delegate
                {
                    _busy = false;
                    SetBusy(false);
                    if (!response.Success)
                    {
                        assistant.Content = string.IsNullOrEmpty(response.ErrorMessage) ? "请求失败。" : response.ErrorMessage;
                        _sidebar.UpdateBubble(assistantBubble, assistant.Content, false);
                    }
                    else
                    {
                        assistant.Content = response.Content;
                        if (!options.Stream)
                            _sidebar.UpdateBubble(assistantBubble, assistant.Content, false);
                    }

                    _session.Messages.Add(assistant);
                    HistoryStore.SaveLatest(_settings, _session);
                    RefreshHistoryList();
                    UpdateProviderStatus();
                });
            });
        }

        private void GenerateImage(string prompt)
        {
            if (_busy || string.IsNullOrEmpty(prompt)) return;
            _settings.Normalize();
            EnsureSidebar();

            ProviderConfig provider = _settings.FindProvider(_settings.Chat.DefaultProviderId);
            if (provider == null)
            {
                _sidebar.AddMessage(new ChatMessage("assistant", "尚未选择服务商。请打开设置选择一个服务商。"));
                return;
            }
            if (!_settings.Chat.ImageGenerationEnabled || !provider.SupportsImageGeneration || provider.ImageApiKind == ImageApiKind.None)
            {
                _sidebar.AddMessage(new ChatMessage("assistant", provider.Name + " 当前没有启用生图能力。请在设置的高级选项中开启或配置生图接口。"));
                return;
            }

            string apiKey = _secrets.Load(provider.ApiKeySecretId);
            if (string.IsNullOrEmpty(apiKey))
            {
                _sidebar.AddMessage(new ChatMessage("assistant", provider.Name + " 尚未保存 API Key。请打开设置添加密钥。"));
                return;
            }

            string modelId = string.IsNullOrEmpty(_settings.Chat.DefaultImageModelId)
                ? provider.DefaultImageModelId
                : _settings.Chat.DefaultImageModelId;
            if (string.IsNullOrEmpty(modelId))
            {
                _sidebar.AddMessage(new ChatMessage("assistant", "尚未选择图片 Model ID。请在设置中填写图片模型。"));
                return;
            }

            ChatMessage user = new ChatMessage("user", "生成图片：" + prompt);
            _session.Messages.Add(user);
            if (string.IsNullOrEmpty(_session.Title) || _session.Title == "新对话" || _session.Messages.Count == 1)
                _session.Title = prompt.Length > 40 ? prompt.Substring(0, 40) : prompt;
            _sidebar.AddMessage(user);

            ChatMessage pending = new ChatMessage("assistant", "正在生成图片...");
            object bubble = _sidebar.AddMessage(pending);

            ImageGenerationOptions options = new ImageGenerationOptions();
            options.ModelId = modelId;
            options.Prompt = prompt;
            options.Size = _settings.Chat.ImageSize;
            options.Count = _settings.Chat.ImageCount;
            options.PreviewCacheDir = _settings.Chat.EffectiveImagePreviewCacheDir;

            _busy = true;
            _cancelSource = new CancellationTokenSource();
            SetBusy(true);
            if (_sidebar != null) _sidebar.SetImageBusy(true);

            ThreadPool.QueueUserWorkItem(delegate
            {
                ImageGenerationResult result = _imageClient.GenerateImage(provider, apiKey, options, _cancelSource.Token);
                InvokeSidebar(delegate
                {
                    _busy = false;
                    SetBusy(false);
                    if (_sidebar != null) _sidebar.SetImageBusy(false);

                    if (!result.Success)
                    {
                        pending.Content = string.IsNullOrEmpty(result.ErrorMessage) ? "图片生成失败。" : result.ErrorMessage;
                        _sidebar.UpdateBubble(bubble, pending.Content, false);
                    }
                    else
                    {
                        GeneratedImage image = result.Images[0];
                        ChatMessage imageMessage = ChatMessage.CreateImage(
                            "assistant",
                            "图片已生成：" + prompt,
                            image.LocalPath,
                            prompt,
                            modelId,
                            options.Size);
                        pending.MessageType = imageMessage.MessageType;
                        pending.Content = imageMessage.Content;
                        pending.ImagePath = imageMessage.ImagePath;
                        pending.ImagePrompt = imageMessage.ImagePrompt;
                        pending.ImageModelId = imageMessage.ImageModelId;
                        pending.ImageSize = imageMessage.ImageSize;
                        _sidebar.UpdateBubble(bubble, pending.Content, false);
                    }

                    _session.Messages.Add(pending);
                    HistoryStore.SaveLatest(_settings, _session);
                    RefreshHistoryList();
                    UpdateProviderStatus();
                });
            });
        }

        private void StopGeneration()
        {
            try
            {
                if (_cancelSource != null)
                    _cancelSource.Cancel();
            }
            catch { }
        }

        private void SetBusy(bool busy)
        {
            if (_sidebar != null) _sidebar.SetBusy(busy);
            if (_quick != null) _quick.SetBusy(busy);
        }

        private void RefreshHistoryList()
        {
            if (_sidebar == null) return;
            List<ChatSession> sessions = HistoryStore.LoadRecent(_settings);
            bool hasCurrent = false;
            foreach (ChatSession session in sessions)
            {
                if (session != null && _session != null && session.Id == _session.Id)
                {
                    hasCurrent = true;
                    break;
                }
            }
            if (!hasCurrent && _session != null)
                sessions.Insert(0, _session);
            _sidebar.LoadSessions(sessions, _session == null ? string.Empty : _session.Id);
        }

        private void UpdateProviderStatus()
        {
            ProviderConfig provider = _settings.FindProvider(_settings.Chat.DefaultProviderId);
            string providerId = provider == null ? string.Empty : provider.Id;
            string providerName = provider == null ? string.Empty : provider.Name;
            string memory = MemoryProbe.CurrentProcessText();
            if (_sidebar != null)
                _sidebar.SetProviderStatus(providerId, providerName, _settings.Chat.DefaultModelId, memory);
            if (_quick != null)
                _quick.SetStatus(providerId, providerName, _settings.Chat.DefaultModelId, memory);
        }

        private void InvokeSidebar(Action action)
        {
            try
            {
                if (_sidebar == null || _sidebar.IsDisposed) return;
                if (_sidebar.InvokeRequired)
                    _sidebar.BeginInvoke(action);
                else
                    action();
            }
            catch { }
        }

        private void ExitApp()
        {
            try { if (_cancelSource != null) _cancelSource.Cancel(); }
            catch { }

            try
            {
                if (_sidebar != null) _settings.Window.Width = _sidebar.Width;
                if (_quick != null && _quick.Visible)
                {
                    _settings.Copilot.QuickPromptX = _quick.Left;
                    _settings.Copilot.QuickPromptY = _quick.Top;
                }
                SettingsStore.Save(_settings);
                HistoryStore.SaveLatest(_settings, _session);
            }
            catch { }

            if (_statusTimer != null) _statusTimer.Stop();
            if (_hotkeys != null) _hotkeys.Dispose();
            if (_tray != null)
            {
                _tray.Visible = false;
                _tray.Dispose();
            }
            if (_quick != null) _quick.Dispose();
            if (_sidebar != null) _sidebar.Dispose();
            if (_settingsForm != null && !_settingsForm.IsDisposed) _settingsForm.Dispose();
            if (_appIcon != null) _appIcon.Dispose();
            Application.ExitThread();
        }

        private static Icon LoadAppIcon()
        {
            try
            {
                Icon icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (icon != null) return (Icon)icon.Clone();
            }
            catch { }
            return MakeFallbackIcon();
        }

        private static Icon MakeFallbackIcon()
        {
            using (Bitmap bmp = new Bitmap(32, 32))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                Rectangle rect = new Rectangle(3, 3, 26, 26);
                using (LinearGradientBrush brush = new LinearGradientBrush(rect, Color.FromArgb(255, 245, 248), Color.FromArgb(196, 225, 248), LinearGradientMode.Vertical))
                using (Pen pen = new Pen(Color.FromArgb(0, 120, 215), 1.5f))
                {
                    UiDrawing.FillRoundedRectangle(g, brush, rect, 8);
                    UiDrawing.DrawRoundedRectangle(g, pen, rect, 8);
                }
                using (SolidBrush hair = new SolidBrush(Color.FromArgb(0, 145, 168)))
                using (SolidBrush face = new SolidBrush(Color.FromArgb(255, 224, 218)))
                using (SolidBrush ink = new SolidBrush(Color.FromArgb(40, 45, 55)))
                {
                    g.FillEllipse(face, 9, 9, 14, 15);
                    g.FillPie(hair, 7, 5, 18, 14, 180, 180);
                    g.FillEllipse(ink, 12, 15, 2, 2);
                    g.FillEllipse(ink, 19, 15, 2, 2);
                    g.FillRectangle(ink, 14, 20, 6, 1);
                }
                IntPtr hIcon = bmp.GetHicon();
                try
                {
                    return (Icon)Icon.FromHandle(hIcon).Clone();
                }
                finally
                {
                    NativeDestroyIcon.DestroyIcon(hIcon);
                }
            }
        }
    }

    internal static class NativeDestroyIcon
    {
        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        internal static extern bool DestroyIcon(IntPtr hIcon);
    }
}
