using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Serialization;
using Microsoft.Win32;

namespace ZakoChat
{
    public static class AppInfo
    {
        public const string AppName = "Zako Chat";
        public const string AppId = "ZakoChat";
        public const string Version = "1.0.0";

        public static string AppDataDir
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppId);
            }
        }

        public static string WebView2UserDataDir
        {
            get
            {
                return Path.Combine(AppDataDir, "runtime", "WebView2");
            }
        }

        public static string ImagePreviewCacheDir
        {
            get
            {
                return Path.Combine(AppDataDir, "image-preview-cache");
            }
        }
    }

    public static class SettingsVersion
    {
        public const string Current = "1.0.0";
    }

    public sealed class SettingsRepairResult
    {
        public bool CreatedNew { get; set; }
        public bool Repaired { get; set; }
        public bool BackupCreated { get; set; }
        public string BackupPath { get; set; }
        public string ErrorSummary { get; set; }

        public SettingsRepairResult()
        {
            BackupPath = string.Empty;
            ErrorSummary = string.Empty;
        }
    }

    public enum ThemeMode
    {
        FollowSystem = 0,
        Light = 1,
        Dark = 2
    }

    public enum SidebarEdge
    {
        Right = 0,
        Left = 1
    }

    public enum SidebarState
    {
        Shown = 0,
        Hidden = 1,
        Showing = 2,
        Hiding = 3
    }

    public enum DisplayMode
    {
        QuickPrompt = 0,
        FullSidebar = 1
    }

    public enum UiRenderMode
    {
        Auto = 0,
        WebView2 = 1,
        Native = 2
    }

    public enum ImageApiKind
    {
        None = 0,
        OpenAiCompatible = 1,
        GeminiNative = 2,
        OpenRouter = 3
    }

    public enum VisionApiKind
    {
        None = 0,
        OpenAiCompatible = 1
    }

    public enum ImagePreviewPolicy
    {
        PreviewCacheOnly = 0
    }

    [Serializable]
    public sealed class AppSettings
    {
        public string Version { get; set; }
        public WindowSettings Window { get; set; }
        public AppearanceSettings Appearance { get; set; }
        public StartupSettings Startup { get; set; }
        public CopilotSettings Copilot { get; set; }
        public HotkeySettings Hotkey { get; set; }
        public ChatSettings Chat { get; set; }
        public List<ProviderConfig> Providers { get; set; }
        public List<PersonaProfile> Personas { get; set; }

        public AppSettings()
        {
            Version = SettingsVersion.Current;
            Window = new WindowSettings();
            Appearance = new AppearanceSettings();
            Startup = new StartupSettings();
            Copilot = new CopilotSettings();
            Hotkey = new HotkeySettings();
            Chat = new ChatSettings();
            Providers = ProviderPresets.CreateDefaultProviders();
            Personas = PersonaProfile.CreateDefaults();
        }

        public static AppSettings CreateDefault()
        {
            return new AppSettings();
        }

        public void Normalize()
        {
            Version = SettingsVersion.Current;
            if (Window == null) Window = new WindowSettings();
            if (Appearance == null) Appearance = new AppearanceSettings();
            if (Startup == null) Startup = new StartupSettings();
            if (Copilot == null) Copilot = new CopilotSettings();
            if (Hotkey == null) Hotkey = new HotkeySettings();
            if (Chat == null) Chat = new ChatSettings();
            if (Providers == null) Providers = new List<ProviderConfig>();
            if (Personas == null || Personas.Count == 0) Personas = PersonaProfile.CreateDefaults();

            Window.Normalize();
            Appearance.Normalize();
            Copilot.Normalize();
            Hotkey.Normalize();
            Chat.Normalize();
            Providers = ProviderPresets.RebuildProviders(Providers);

            Personas = PersonaRepair.Dedupe(Personas);
            bool hasPersona = false;
            foreach (PersonaProfile persona in Personas)
            {
                persona.Normalize();
                if (string.Equals(persona.Id, Chat.CurrentPersonaId, StringComparison.OrdinalIgnoreCase))
                    hasPersona = true;
            }
            if (!hasPersona && Personas.Count > 0) Chat.CurrentPersonaId = Personas[0].Id;

            ProviderConfig selected = FindProvider(Chat.DefaultProviderId);
            if (selected == null && Providers.Count > 0)
            {
                Chat.DefaultProviderId = Providers[0].Id;
                selected = Providers[0];
            }
            if (selected != null && string.IsNullOrEmpty(Chat.DefaultModelId))
                Chat.DefaultModelId = selected.DefaultModelId;
        }

        public ProviderConfig FindProvider(string id)
        {
            if (Providers == null) return null;
            foreach (ProviderConfig provider in Providers)
            {
                if (provider != null && string.Equals(provider.Id, id, StringComparison.OrdinalIgnoreCase))
                    return provider;
            }
            return null;
        }

        public PersonaProfile FindPersona(string id)
        {
            if (Personas == null) return null;
            foreach (PersonaProfile persona in Personas)
            {
                if (persona != null && string.Equals(persona.Id, id, StringComparison.OrdinalIgnoreCase))
                    return persona;
            }
            return null;
        }
    }

    [Serializable]
    public sealed class WindowSettings
    {
        public SidebarEdge Edge { get; set; }
        public bool TopMost { get; set; }
        public bool AutoHide { get; set; }
        public int Width { get; set; }
        public int LastHeight { get; set; }
        public int OpacityPercent { get; set; }

        public WindowSettings()
        {
            Edge = SidebarEdge.Right;
            TopMost = true;
            AutoHide = false;
            Width = 640;
            LastHeight = 720;
            OpacityPercent = 98;
        }

        public void Normalize()
        {
            Width = Clamp(Width, 560, 920);
            LastHeight = Clamp(LastHeight, 480, 1600);
            OpacityPercent = Clamp(OpacityPercent, 82, 100);
        }

        private static int Clamp(int value, int min, int max)
        {
            return Math.Max(min, Math.Min(max, value));
        }
    }

    [Serializable]
    public sealed class AppearanceSettings
    {
        public ThemeMode Theme { get; set; }
        public bool UseSystemAccent { get; set; }
        public bool UseAcrylic { get; set; }
        public bool AnimationEnabled { get; set; }
        public bool ReducedMotion { get; set; }
        public int AnimationSpeedPercent { get; set; }
        public int AccentArgb { get; set; }
        public UiRenderMode RenderMode { get; set; }

        public AppearanceSettings()
        {
            Theme = ThemeMode.FollowSystem;
            UseSystemAccent = true;
            UseAcrylic = true;
            AnimationEnabled = true;
            ReducedMotion = false;
            AnimationSpeedPercent = 100;
            AccentArgb = Color.FromArgb(0, 120, 215).ToArgb();
            RenderMode = UiRenderMode.Auto;
        }

        public void Normalize()
        {
            AnimationSpeedPercent = Math.Max(60, Math.Min(180, AnimationSpeedPercent));
            if (RenderMode != UiRenderMode.Auto && RenderMode != UiRenderMode.WebView2 && RenderMode != UiRenderMode.Native)
                RenderMode = UiRenderMode.Auto;
        }

        [XmlIgnore]
        public Color AccentColor
        {
            get { return Color.FromArgb(AccentArgb); }
            set { AccentArgb = value.ToArgb(); }
        }
    }

    [Serializable]
    public sealed class StartupSettings
    {
        public bool StartWithWindows { get; set; }
    }

    [Serializable]
    public sealed class CopilotSettings
    {
        public DisplayMode DefaultDisplayMode { get; set; }
        public bool RememberLastMode { get; set; }
        public DisplayMode LastDisplayMode { get; set; }
        public bool ExpandAnimation { get; set; }
        public int QuickPromptX { get; set; }
        public int QuickPromptY { get; set; }

        public CopilotSettings()
        {
            DefaultDisplayMode = DisplayMode.QuickPrompt;
            RememberLastMode = false;
            LastDisplayMode = DisplayMode.QuickPrompt;
            ExpandAnimation = true;
            QuickPromptX = -1;
            QuickPromptY = -1;
        }

        public void Normalize()
        {
            if (DefaultDisplayMode != DisplayMode.QuickPrompt && DefaultDisplayMode != DisplayMode.FullSidebar)
                DefaultDisplayMode = DisplayMode.QuickPrompt;
            if (LastDisplayMode != DisplayMode.QuickPrompt && LastDisplayMode != DisplayMode.FullSidebar)
                LastDisplayMode = DisplayMode.QuickPrompt;
            if (QuickPromptX < -1) QuickPromptX = -1;
            if (QuickPromptY < -1) QuickPromptY = -1;
        }
    }

    [Serializable]
    public sealed class HotkeySettings
    {
        public bool Enabled { get; set; }
        public int PreferredModifiers { get; set; }
        public int PreferredKey { get; set; }
        public int FallbackModifiers { get; set; }
        public int FallbackKey { get; set; }
        public int ActiveModifiers { get; set; }
        public int ActiveKey { get; set; }
        public string LastStatus { get; set; }

        public HotkeySettings()
        {
            Enabled = true;
            PreferredModifiers = HotkeyModifiers.Control | HotkeyModifiers.Shift;
            PreferredKey = (int)System.Windows.Forms.Keys.Z;
            FallbackModifiers = 0;
            FallbackKey = 0;
            ActiveModifiers = PreferredModifiers;
            ActiveKey = PreferredKey;
            LastStatus = string.Empty;
        }

        public void Normalize()
        {
            if (PreferredKey == 0) PreferredKey = (int)System.Windows.Forms.Keys.Z;
            if (PreferredModifiers == 0) PreferredModifiers = HotkeyModifiers.Control | HotkeyModifiers.Shift;
            if (ActiveKey == 0) ActiveKey = PreferredKey;
            if (ActiveModifiers == 0) ActiveModifiers = PreferredModifiers;
            if (LastStatus == null) LastStatus = string.Empty;
        }

        public bool LooksLikeOldDefault()
        {
            if (PreferredKey != (int)System.Windows.Forms.Keys.Z) return false;
            return PreferredModifiers == HotkeyModifiers.Win ||
                PreferredModifiers == (HotkeyModifiers.Control | HotkeyModifiers.Alt);
        }

        public void ResetToDefaultShortcut()
        {
            PreferredModifiers = HotkeyModifiers.Control | HotkeyModifiers.Shift;
            PreferredKey = (int)System.Windows.Forms.Keys.Z;
            FallbackModifiers = 0;
            FallbackKey = 0;
            ActiveModifiers = PreferredModifiers;
            ActiveKey = PreferredKey;
            LastStatus = string.Empty;
        }
    }

    public static class HotkeyModifiers
    {
        public const int Alt = 0x0001;
        public const int Control = 0x0002;
        public const int Shift = 0x0004;
        public const int Win = 0x0008;
    }

    [Serializable]
    public sealed class ChatSettings
    {
        public string DefaultProviderId { get; set; }
        public string DefaultModelId { get; set; }
        public string CurrentPersonaId { get; set; }
        public bool StreamResponses { get; set; }
        public bool SaveHistory { get; set; }
        public int MaxConversations { get; set; }
        public int MaxMessagesPerConversation { get; set; }
        public decimal Temperature { get; set; }
        public int MaxTokens { get; set; }
        public bool ImageGenerationEnabled { get; set; }
        public string DefaultImageModelId { get; set; }
        public string ImageSize { get; set; }
        public int ImageCount { get; set; }
        public ImagePreviewPolicy ImagePreviewPolicy { get; set; }
        public string ImagePreviewCacheDir { get; set; }
        public int MaxUploadImageMb { get; set; }

        public ChatSettings()
        {
            DefaultProviderId = "openai";
            DefaultModelId = string.Empty;
            CurrentPersonaId = "general";
            StreamResponses = true;
            SaveHistory = true;
            MaxConversations = 50;
            MaxMessagesPerConversation = 200;
            Temperature = 0.7m;
            MaxTokens = 2048;
            ImageGenerationEnabled = true;
            DefaultImageModelId = string.Empty;
            ImageSize = "1024x1024";
            ImageCount = 1;
            ImagePreviewPolicy = ImagePreviewPolicy.PreviewCacheOnly;
            ImagePreviewCacheDir = string.Empty;
            MaxUploadImageMb = 8;
        }

        public void Normalize()
        {
            if (string.IsNullOrEmpty(DefaultProviderId)) DefaultProviderId = "openai";
            if (string.IsNullOrEmpty(DefaultModelId)) DefaultModelId = string.Empty;
            if (string.IsNullOrEmpty(CurrentPersonaId)) CurrentPersonaId = "general";
            MaxConversations = Math.Max(1, Math.Min(200, MaxConversations));
            MaxMessagesPerConversation = Math.Max(20, Math.Min(1000, MaxMessagesPerConversation));
            if (Temperature < 0) Temperature = 0;
            if (Temperature > 2) Temperature = 2;
            MaxTokens = Math.Max(128, Math.Min(32000, MaxTokens));
            if (DefaultImageModelId == null) DefaultImageModelId = string.Empty;
            if (string.IsNullOrEmpty(ImageSize)) ImageSize = "1024x1024";
            ImageCount = Math.Max(1, Math.Min(4, ImageCount));
            if (ImagePreviewPolicy != ImagePreviewPolicy.PreviewCacheOnly) ImagePreviewPolicy = ImagePreviewPolicy.PreviewCacheOnly;
            if (ImagePreviewCacheDir == null) ImagePreviewCacheDir = string.Empty;
            MaxUploadImageMb = Math.Max(1, Math.Min(32, MaxUploadImageMb));
        }

        [XmlIgnore]
        public string EffectiveImagePreviewCacheDir
        {
            get { return string.IsNullOrEmpty(ImagePreviewCacheDir) ? AppInfo.ImagePreviewCacheDir : ImagePreviewCacheDir; }
        }
    }

    public sealed class ProviderDescriptor
    {
        public string Id { get; private set; }
        public string Name { get; private set; }
        public string DefaultBaseUrl { get; private set; }
        public string ModelListPath { get; private set; }
        public string ChatPath { get; private set; }
        public bool SupportsStreaming { get; private set; }
        public string DefaultModelId { get; private set; }
        public bool SupportsImageGeneration { get; private set; }
        public ImageApiKind ImageApiKind { get; private set; }
        public string ImagePath { get; private set; }
        public string ImageModelListPath { get; private set; }
        public string DefaultImageModelId { get; private set; }

        public ProviderDescriptor(string id, string name, string baseUrl, string defaultModelId)
        {
            Id = id;
            Name = name;
            DefaultBaseUrl = baseUrl;
            ModelListPath = "/models";
            ChatPath = "/chat/completions";
            SupportsStreaming = true;
            DefaultModelId = defaultModelId;
            SupportsImageGeneration = false;
            ImageApiKind = ImageApiKind.None;
            ImagePath = "/images/generations";
            ImageModelListPath = "/models";
            DefaultImageModelId = string.Empty;
        }

        public ProviderDescriptor WithImage(ImageApiKind apiKind, string imagePath, string defaultImageModelId)
        {
            SupportsImageGeneration = apiKind != ImageApiKind.None;
            ImageApiKind = apiKind;
            if (!string.IsNullOrEmpty(imagePath)) ImagePath = imagePath;
            DefaultImageModelId = defaultImageModelId ?? string.Empty;
            return this;
        }
    }

    [Serializable]
    public sealed class ProviderConfig
    {
        public string Id { get; set; }
        public string BaseUrl { get; set; }
        public string ApiKeySecretId { get; set; }
        public string ModelListPath { get; set; }
        public string ChatPath { get; set; }
        public string ExtraHeaders { get; set; }
        public bool SupportsStreaming { get; set; }
        public bool Enabled { get; set; }
        public string DefaultModelId { get; set; }
        public bool SupportsImageGeneration { get; set; }
        public ImageApiKind ImageApiKind { get; set; }
        public string ImagePath { get; set; }
        public string ImageModelListPath { get; set; }
        public string DefaultImageModelId { get; set; }
        public bool SupportsVision { get; set; }
        public VisionApiKind VisionApiKind { get; set; }
        public int MaxUploadImageMb { get; set; }

        [XmlIgnore]
        public string Name { get; set; }

        public ProviderConfig()
        {
            Id = string.Empty;
            Name = string.Empty;
            BaseUrl = string.Empty;
            ApiKeySecretId = string.Empty;
            ModelListPath = "/models";
            ChatPath = "/chat/completions";
            ExtraHeaders = string.Empty;
            SupportsStreaming = true;
            Enabled = true;
            DefaultModelId = string.Empty;
            SupportsImageGeneration = false;
            ImageApiKind = ImageApiKind.None;
            ImagePath = "/images/generations";
            ImageModelListPath = "/models";
            DefaultImageModelId = string.Empty;
            SupportsVision = false;
            VisionApiKind = VisionApiKind.None;
            MaxUploadImageMb = 8;
        }

        public void ApplyDescriptor(ProviderDescriptor descriptor)
        {
            if (descriptor == null) return;
            Id = descriptor.Id;
            Name = descriptor.Name;
            if (string.IsNullOrEmpty(BaseUrl)) BaseUrl = descriptor.DefaultBaseUrl;
            if (string.IsNullOrEmpty(ModelListPath)) ModelListPath = descriptor.ModelListPath;
            if (string.IsNullOrEmpty(ChatPath)) ChatPath = descriptor.ChatPath;
            if (string.IsNullOrEmpty(DefaultModelId)) DefaultModelId = descriptor.DefaultModelId;
            if (ImageApiKind == ImageApiKind.None) ImageApiKind = descriptor.ImageApiKind;
            SupportsImageGeneration = descriptor.SupportsImageGeneration;
            if (string.IsNullOrEmpty(ImagePath)) ImagePath = descriptor.ImagePath;
            if (string.IsNullOrEmpty(ImageModelListPath)) ImageModelListPath = descriptor.ImageModelListPath;
            if (string.IsNullOrEmpty(DefaultImageModelId)) DefaultImageModelId = descriptor.DefaultImageModelId;
            if (string.IsNullOrEmpty(ApiKeySecretId)) ApiKeySecretId = "provider-" + SafeId(Id);
            BaseUrl = BaseUrl.TrimEnd('/');
            if (ExtraHeaders == null) ExtraHeaders = string.Empty;
        }

        public ProviderConfig CloneEditable()
        {
            ProviderConfig copy = new ProviderConfig();
            copy.Id = Id;
            copy.BaseUrl = BaseUrl;
            copy.ApiKeySecretId = ApiKeySecretId;
            copy.ModelListPath = ModelListPath;
            copy.ChatPath = ChatPath;
            copy.ExtraHeaders = ExtraHeaders;
            copy.SupportsStreaming = SupportsStreaming;
            copy.Enabled = Enabled;
            copy.DefaultModelId = DefaultModelId;
            copy.SupportsImageGeneration = SupportsImageGeneration;
            copy.ImageApiKind = ImageApiKind;
            copy.ImagePath = ImagePath;
            copy.ImageModelListPath = ImageModelListPath;
            copy.DefaultImageModelId = DefaultImageModelId;
            copy.SupportsVision = SupportsVision;
            copy.VisionApiKind = VisionApiKind;
            copy.MaxUploadImageMb = MaxUploadImageMb;
            copy.Name = Name;
            return copy;
        }

        public void Normalize()
        {
            if (Id == null) Id = string.Empty;
            if (BaseUrl == null) BaseUrl = string.Empty;
            if (ApiKeySecretId == null || ApiKeySecretId.Length == 0)
                ApiKeySecretId = "provider-" + SafeId(Id);
            if (ModelListPath == null || ModelListPath.Length == 0) ModelListPath = "/models";
            if (ChatPath == null || ChatPath.Length == 0) ChatPath = "/chat/completions";
            if (ExtraHeaders == null) ExtraHeaders = string.Empty;
            if (DefaultModelId == null) DefaultModelId = string.Empty;
            if (ImagePath == null || ImagePath.Length == 0) ImagePath = "/images/generations";
            if (ImageModelListPath == null || ImageModelListPath.Length == 0) ImageModelListPath = "/models";
            if (DefaultImageModelId == null) DefaultImageModelId = string.Empty;
            MaxUploadImageMb = Math.Max(1, Math.Min(32, MaxUploadImageMb));
            BaseUrl = BaseUrl.TrimEnd('/');
        }

        private static string SafeId(string value)
        {
            if (string.IsNullOrEmpty(value)) return Guid.NewGuid().ToString("N");
            StringBuilder sb = new StringBuilder();
            foreach (char c in value)
            {
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_') sb.Append(c);
            }
            return sb.Length == 0 ? Guid.NewGuid().ToString("N") : sb.ToString();
        }
    }

    public static class ProviderPresets
    {
        private static readonly ProviderDescriptor[] Descriptors = new ProviderDescriptor[]
        {
            new ProviderDescriptor("openai", "OpenAI", "https://api.openai.com/v1", "gpt-4o-mini"),
            new ProviderDescriptor("gemini", "Gemini（OpenAI 兼容）", "https://generativelanguage.googleapis.com/v1beta/openai", "gemini-1.5-flash"),
            new ProviderDescriptor("deepseek", "DeepSeek", "https://api.deepseek.com", "deepseek-chat"),
            new ProviderDescriptor("bigmodel", "智谱 BigModel / GLM", "https://open.bigmodel.cn/api/paas/v4", "glm-4-flash"),
            new ProviderDescriptor("siliconflow", "硅基流动 SiliconFlow", "https://api.siliconflow.cn/v1", "Qwen/Qwen2.5-7B-Instruct"),
            new ProviderDescriptor("moonshot", "Kimi / 月之暗面", "https://api.moonshot.cn/v1", "moonshot-v1-8k"),
            new ProviderDescriptor("openrouter", "OpenRouter", "https://openrouter.ai/api/v1", string.Empty),
            new ProviderDescriptor("oneapi", "OneAPI / NewAPI", "http://127.0.0.1:3000/v1", string.Empty),
            new ProviderDescriptor("custom", "自定义接口", "https://example.com/v1", string.Empty)
        };

        public static IList<ProviderDescriptor> GetDescriptors()
        {
            return Descriptors;
        }

        public static ProviderDescriptor FindDescriptor(string id)
        {
            foreach (ProviderDescriptor descriptor in Descriptors)
            {
                if (string.Equals(descriptor.Id, id, StringComparison.OrdinalIgnoreCase))
                    return descriptor;
            }
            return null;
        }

        public static List<ProviderConfig> CreateDefaultProviders()
        {
            return RebuildProviders(null);
        }

        public static List<ProviderConfig> RebuildProviders(IEnumerable<ProviderConfig> existingProviders)
        {
            List<ProviderConfig> existing = new List<ProviderConfig>();
            if (existingProviders != null)
            {
                foreach (ProviderConfig provider in existingProviders)
                {
                    if (provider == null) continue;
                    provider.Normalize();
                    existing.Add(provider);
                }
            }

            List<ProviderConfig> rebuilt = new List<ProviderConfig>();
            foreach (ProviderDescriptor descriptor in Descriptors)
            {
                ProviderConfig merged = FindFirst(existing, descriptor.Id);
                if (merged == null) merged = new ProviderConfig();
                else merged = merged.CloneEditable();
                merged.ApplyDescriptor(descriptor);
                ApplyProviderOverrides(merged);
                merged.Normalize();
                rebuilt.Add(merged);
            }
            return rebuilt;
        }

        private static void ApplyProviderOverrides(ProviderConfig provider)
        {
            if (provider == null) return;
            string id = provider.Id ?? string.Empty;
            if (id == "openai")
            {
                provider.Name = "OpenAI";
                provider.BaseUrl = string.IsNullOrEmpty(provider.BaseUrl) ? "https://api.openai.com/v1" : provider.BaseUrl;
                provider.SupportsVision = true;
                provider.VisionApiKind = VisionApiKind.OpenAiCompatible;
                provider.SupportsImageGeneration = true;
                provider.ImageApiKind = ImageApiKind.OpenAiCompatible;
                if (string.IsNullOrEmpty(provider.DefaultImageModelId)) provider.DefaultImageModelId = "gpt-image-1";
            }
            else if (id == "gemini")
            {
                provider.Name = "Gemini（OpenAI 兼容）";
                provider.BaseUrl = string.IsNullOrEmpty(provider.BaseUrl) ? "https://generativelanguage.googleapis.com/v1beta/openai" : provider.BaseUrl;
                provider.SupportsVision = true;
                provider.VisionApiKind = VisionApiKind.OpenAiCompatible;
                provider.SupportsImageGeneration = true;
                provider.ImageApiKind = ImageApiKind.GeminiNative;
                provider.ImagePath = "https://generativelanguage.googleapis.com/v1beta/interactions";
                if (string.IsNullOrEmpty(provider.DefaultImageModelId)) provider.DefaultImageModelId = "imagen-3.0-generate-002";
            }
            else if (id == "deepseek")
            {
                provider.Name = "DeepSeek";
                provider.SupportsVision = false;
                provider.VisionApiKind = VisionApiKind.None;
                provider.SupportsImageGeneration = false;
                provider.ImageApiKind = ImageApiKind.None;
            }
            else if (id == "bigmodel")
            {
                provider.Name = "智谱 BigModel / GLM";
                provider.SupportsVision = false;
                provider.VisionApiKind = VisionApiKind.None;
                provider.SupportsImageGeneration = true;
                provider.ImageApiKind = ImageApiKind.OpenAiCompatible;
                if (string.IsNullOrEmpty(provider.DefaultImageModelId)) provider.DefaultImageModelId = "cogview-3-flash";
            }
            else if (id == "siliconflow")
            {
                provider.Name = "硅基流动 SiliconFlow";
                provider.SupportsVision = false;
                provider.VisionApiKind = VisionApiKind.None;
                provider.SupportsImageGeneration = true;
                provider.ImageApiKind = ImageApiKind.OpenAiCompatible;
                if (string.IsNullOrEmpty(provider.DefaultImageModelId)) provider.DefaultImageModelId = "Kwai-Kolors/Kolors";
            }
            else if (id == "moonshot")
            {
                provider.Name = "Kimi / 月之暗面";
                provider.SupportsVision = false;
                provider.VisionApiKind = VisionApiKind.None;
                provider.SupportsImageGeneration = false;
                provider.ImageApiKind = ImageApiKind.None;
            }
            else if (id == "openrouter")
            {
                provider.Name = "OpenRouter";
                provider.SupportsVision = true;
                provider.VisionApiKind = VisionApiKind.OpenAiCompatible;
                provider.SupportsImageGeneration = true;
                provider.ImageApiKind = ImageApiKind.OpenRouter;
            }
            else if (id == "oneapi")
            {
                provider.Name = "OneAPI / NewAPI";
                provider.SupportsVision = true;
                provider.VisionApiKind = VisionApiKind.OpenAiCompatible;
                provider.SupportsImageGeneration = true;
                provider.ImageApiKind = ImageApiKind.OpenAiCompatible;
            }
            else if (id == "custom")
            {
                provider.Name = "自定义接口";
                provider.SupportsVision = true;
                provider.VisionApiKind = VisionApiKind.OpenAiCompatible;
                provider.SupportsImageGeneration = true;
                provider.ImageApiKind = ImageApiKind.OpenAiCompatible;
            }
            if (provider.SupportsImageGeneration && string.IsNullOrEmpty(provider.ImagePath))
                provider.ImagePath = "/images/generations";
        }

        private static ProviderConfig FindFirst(List<ProviderConfig> providers, string id)
        {
            foreach (ProviderConfig provider in providers)
            {
                if (provider != null && string.Equals(provider.Id, id, StringComparison.OrdinalIgnoreCase))
                    return provider;
            }
            return null;
        }
    }

    [Serializable]
    public sealed class PersonaProfile
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Prompt { get; set; }

        public PersonaProfile()
        {
            Id = string.Empty;
            Name = string.Empty;
            Prompt = string.Empty;
        }

        public static List<PersonaProfile> CreateDefaults()
        {
            List<PersonaProfile> personas = new List<PersonaProfile>();
            personas.Add(new PersonaProfile
            {
                Id = "general",
                Name = "通用助手",
                Prompt = "你是 Zako Chat，一款安静、轻量、可靠的 Windows AI 助手。请用清晰自然的中文回答，必要时给出可执行的步骤。"
            });
            personas.Add(new PersonaProfile
            {
                Id = "coder",
                Name = "编程伙伴",
                Prompt = "你是一名细心的高级工程助手。请优先给出准确、可落地、便于维护的代码建议。"
            });
            personas.Add(new PersonaProfile
            {
                Id = "brief",
                Name = "简洁模式",
                Prompt = "请用简短、直接、少废话的中文回答。保留关键结论和必要步骤。"
            });
            return personas;
        }

        public void Normalize()
        {
            if (string.IsNullOrEmpty(Id)) Id = Guid.NewGuid().ToString("N");
            if (Name == null || Name.Length == 0) Name = "人设";
            if (Prompt == null) Prompt = string.Empty;
        }
    }

    public static class PersonaRepair
    {
        public static List<PersonaProfile> Dedupe(IEnumerable<PersonaProfile> source)
        {
            List<PersonaProfile> result = new List<PersonaProfile>();
            HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> fingerprints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            IEnumerable<PersonaProfile> items = source ?? PersonaProfile.CreateDefaults();

            foreach (PersonaProfile persona in items)
            {
                if (persona == null) continue;
                persona.Normalize();
                FixBuiltInPersona(persona);
                string id = persona.Id ?? string.Empty;
                string fingerprint = (persona.Name ?? string.Empty).Trim() + "\n" + (persona.Prompt ?? string.Empty).Trim();
                if (id.Length > 0 && !ids.Add(id)) continue;
                if (fingerprint.Trim().Length > 0 && !fingerprints.Add(fingerprint)) continue;
                result.Add(persona);
            }

            foreach (PersonaProfile persona in PersonaProfile.CreateDefaults())
            {
                persona.Normalize();
                FixBuiltInPersona(persona);
                if (!ids.Contains(persona.Id))
                {
                    ids.Add(persona.Id);
                    result.Insert(Math.Min(result.Count, ids.Count - 1), persona);
                }
            }

            return result.Count == 0 ? PersonaProfile.CreateDefaults() : result;
        }

        private static void FixBuiltInPersona(PersonaProfile persona)
        {
            if (persona == null) return;
            if (string.Equals(persona.Id, "general", StringComparison.OrdinalIgnoreCase))
            {
                persona.Name = "通用助手";
                persona.Prompt = "你是 Zako Chat，一款安静、轻量、可靠的 Windows AI 助手。请用清晰自然的中文回答，必要时给出可执行的步骤。";
            }
            else if (string.Equals(persona.Id, "coder", StringComparison.OrdinalIgnoreCase))
            {
                persona.Name = "编程伙伴";
                persona.Prompt = "你是一名细心的高级工程助手。请优先给出准确、可落地、便于维护的代码建议。";
            }
            else if (string.Equals(persona.Id, "brief", StringComparison.OrdinalIgnoreCase))
            {
                persona.Name = "简洁模式";
                persona.Prompt = "请用简短、直接、少废话的中文回答。保留关键结论和必要步骤。";
            }
        }
    }

    public static class SettingsStore
    {
        public static SettingsRepairResult LastRepairResult { get; private set; }

        public static string SettingsPath
        {
            get { return Path.Combine(AppInfo.AppDataDir, "settings.xml"); }
        }

        public static string BackupPath
        {
            get { return Path.Combine(AppInfo.AppDataDir, "settings.bak.xml"); }
        }

        public static AppSettings Load()
        {
            SettingsRepairResult ignored;
            return Load(out ignored);
        }

        public static AppSettings Load(out SettingsRepairResult repair)
        {
            repair = new SettingsRepairResult();
            LastRepairResult = repair;
            EnsureAppDataDir();

            if (!File.Exists(SettingsPath))
            {
                AppSettings created = AppSettings.CreateDefault();
                Save(created);
                repair.CreatedNew = true;
                return created;
            }

            try
            {
                XmlSerializer serializer = new XmlSerializer(typeof(AppSettings));
                AppSettings settings;
                using (FileStream fs = File.OpenRead(SettingsPath))
                {
                    settings = serializer.Deserialize(fs) as AppSettings;
                }
                if (settings == null) throw new InvalidDataException("设置文件为空。");

                bool needsRepair = NeedsRepair(settings);
                if (needsRepair)
                {
                    BackupExistingSettings(repair);
                    settings = RepairFrom(settings);
                    repair.Repaired = true;
                    Save(settings);
                }
                else
                {
                    settings.Normalize();
                    Save(settings);
                }
                return settings;
            }
            catch (Exception ex)
            {
                repair.ErrorSummary = ex.Message;
                BackupExistingSettings(repair);
                repair.Repaired = true;
                AppSettings clean = AppSettings.CreateDefault();
                Save(clean);
                return clean;
            }
        }

        public static void Save(AppSettings settings)
        {
            if (settings == null) settings = AppSettings.CreateDefault();
            settings.Normalize();
            EnsureAppDataDir();
            XmlSerializer serializer = new XmlSerializer(typeof(AppSettings));
            using (FileStream fs = File.Create(SettingsPath))
            {
                serializer.Serialize(fs, settings);
            }
        }

        private static bool NeedsRepair(AppSettings settings)
        {
            if (settings == null) return true;
            if (!string.Equals(settings.Version, SettingsVersion.Current, StringComparison.OrdinalIgnoreCase)) return true;
            if (settings.Providers == null) return true;
            if (settings.Providers.Count != ProviderPresets.GetDescriptors().Count) return true;
            if (HasDuplicateProviderIds(settings.Providers)) return true;
            if (HasDuplicatePersonas(settings.Personas)) return true;
            if (settings.Chat == null) return true;
            if (settings.FindProvider(settings.Chat.DefaultProviderId) == null) return true;
            return false;
        }

        private static bool HasDuplicateProviderIds(IEnumerable<ProviderConfig> providers)
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ProviderConfig provider in providers)
            {
                if (provider == null || string.IsNullOrEmpty(provider.Id)) continue;
                if (!seen.Add(provider.Id)) return true;
            }
            return false;
        }

        private static bool HasDuplicatePersonas(IEnumerable<PersonaProfile> personas)
        {
            if (personas == null) return true;
            HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> fingerprints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (PersonaProfile persona in personas)
            {
                if (persona == null) continue;
                string id = persona.Id ?? string.Empty;
                if (id.Length > 0 && !ids.Add(id)) return true;
                string fingerprint = (persona.Name ?? string.Empty).Trim() + "\n" + (persona.Prompt ?? string.Empty).Trim();
                if (fingerprint.Trim().Length > 0 && !fingerprints.Add(fingerprint)) return true;
            }
            return false;
        }

        private static AppSettings RepairFrom(AppSettings oldSettings)
        {
            AppSettings clean = AppSettings.CreateDefault();
            if (oldSettings == null) return clean;

            if (oldSettings.Window != null) clean.Window = oldSettings.Window;
            if (oldSettings.Appearance != null) clean.Appearance = oldSettings.Appearance;
            if (oldSettings.Startup != null) clean.Startup = oldSettings.Startup;
            if (oldSettings.Copilot != null) clean.Copilot = oldSettings.Copilot;
            if (oldSettings.Hotkey != null)
            {
                clean.Hotkey = oldSettings.Hotkey;
                if (clean.Hotkey.LooksLikeOldDefault())
                    clean.Hotkey.ResetToDefaultShortcut();
            }
            if (oldSettings.Chat != null) clean.Chat = oldSettings.Chat;
            if (oldSettings.Personas != null && oldSettings.Personas.Count > 0) clean.Personas = PersonaRepair.Dedupe(oldSettings.Personas);

            clean.Providers = ProviderPresets.RebuildProviders(oldSettings.Providers);
            clean.Normalize();
            return clean;
        }

        private static void BackupExistingSettings(SettingsRepairResult repair)
        {
            try
            {
                if (!File.Exists(SettingsPath)) return;
                EnsureAppDataDir();
                File.Copy(SettingsPath, BackupPath, true);
                repair.BackupCreated = true;
                repair.BackupPath = BackupPath;
            }
            catch (Exception ex)
            {
                repair.ErrorSummary = (repair.ErrorSummary + " 备份失败：" + ex.Message).Trim();
            }
        }

        private static void EnsureAppDataDir()
        {
            if (!Directory.Exists(AppInfo.AppDataDir))
                Directory.CreateDirectory(AppInfo.AppDataDir);
        }
    }

    public sealed class SecretStore
    {
        private readonly string _dir;

        public SecretStore()
        {
            _dir = Path.Combine(AppInfo.AppDataDir, "secrets");
        }

        public void Save(string secretId, string secret)
        {
            if (!Directory.Exists(_dir)) Directory.CreateDirectory(_dir);
            byte[] plain = Encoding.UTF8.GetBytes(secret == null ? string.Empty : secret);
            byte[] protectedBytes = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(GetPath(secretId), protectedBytes);
        }

        public string Load(string secretId)
        {
            try
            {
                string path = GetPath(secretId);
                if (!File.Exists(path)) return string.Empty;
                byte[] protectedBytes = File.ReadAllBytes(path);
                byte[] plain = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plain);
            }
            catch
            {
                return string.Empty;
            }
        }

        public bool Exists(string secretId)
        {
            return File.Exists(GetPath(secretId));
        }

        public void Delete(string secretId)
        {
            try
            {
                string path = GetPath(secretId);
                if (File.Exists(path)) File.Delete(path);
            }
            catch { }
        }

        private string GetPath(string secretId)
        {
            string safe = string.IsNullOrEmpty(secretId) ? "default" : secretId.Replace("/", "_").Replace("\\", "_").Replace(":", "_");
            return Path.Combine(_dir, safe + ".bin");
        }
    }

    public static class StartupManager
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "ZakoChat";

        public static void SetRunAtLogin(bool enabled, string exePath)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (key == null) return;
                    if (enabled)
                        key.SetValue(ValueName, "\"" + exePath + "\"", RegistryValueKind.String);
                    else
                        key.DeleteValue(ValueName, false);
                }
            }
            catch { }
        }
    }

    [Serializable]
    public sealed class ChatMessage
    {
        public string Role { get; set; }
        public string Content { get; set; }
        public string MessageType { get; set; }
        public string ImagePath { get; set; }
        public string ImagePrompt { get; set; }
        public string ImageModelId { get; set; }
        public string ImageSize { get; set; }
        public string AttachmentPath { get; set; }
        public string AttachmentName { get; set; }
        public DateTime CreatedAt { get; set; }

        public ChatMessage()
        {
            Role = "user";
            Content = string.Empty;
            MessageType = "text";
            ImagePath = string.Empty;
            ImagePrompt = string.Empty;
            ImageModelId = string.Empty;
            ImageSize = string.Empty;
            AttachmentPath = string.Empty;
            AttachmentName = string.Empty;
            CreatedAt = DateTime.Now;
        }

        public ChatMessage(string role, string content)
        {
            Role = role;
            Content = content ?? string.Empty;
            MessageType = "text";
            ImagePath = string.Empty;
            ImagePrompt = string.Empty;
            ImageModelId = string.Empty;
            ImageSize = string.Empty;
            AttachmentPath = string.Empty;
            AttachmentName = string.Empty;
            CreatedAt = DateTime.Now;
        }

        public static ChatMessage CreateVision(string role, string content, string attachmentPath)
        {
            ChatMessage message = new ChatMessage(role, content);
            message.MessageType = "vision";
            message.AttachmentPath = attachmentPath ?? string.Empty;
            message.AttachmentName = string.IsNullOrEmpty(attachmentPath) ? string.Empty : Path.GetFileName(attachmentPath);
            return message;
        }

        public static ChatMessage CreateImage(string role, string content, string imagePath, string prompt, string modelId, string size)
        {
            ChatMessage message = new ChatMessage(role, content);
            message.MessageType = "image";
            message.ImagePath = imagePath ?? string.Empty;
            message.ImagePrompt = prompt ?? string.Empty;
            message.ImageModelId = modelId ?? string.Empty;
            message.ImageSize = size ?? string.Empty;
            return message;
        }
    }

    [Serializable]
    public sealed class ChatSession
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public DateTime UpdatedAt { get; set; }
        public List<ChatMessage> Messages { get; set; }

        public ChatSession()
        {
            Id = Guid.NewGuid().ToString("N");
            Title = "新对话";
            Title = "新对话";
            UpdatedAt = DateTime.Now;
            Messages = new List<ChatMessage>();
        }
    }

    [Serializable]
    public sealed class ChatHistoryDocument
    {
        public List<ChatSession> Sessions { get; set; }

        public ChatHistoryDocument()
        {
            Sessions = new List<ChatSession>();
        }
    }

    public static class HistoryStore
    {
        private static string HistoryPath
        {
            get { return Path.Combine(AppInfo.AppDataDir, "history.xml"); }
        }

        public static ChatSession LoadLatest(AppSettings settings)
        {
            try
            {
                if (settings == null || settings.Chat == null || !settings.Chat.SaveHistory || !File.Exists(HistoryPath))
                    return new ChatSession();
                XmlSerializer serializer = new XmlSerializer(typeof(ChatHistoryDocument));
                using (FileStream fs = File.OpenRead(HistoryPath))
                {
                    ChatHistoryDocument document = serializer.Deserialize(fs) as ChatHistoryDocument;
                    if (document == null || document.Sessions == null || document.Sessions.Count == 0)
                        return new ChatSession();
                    document.Sessions.Sort(delegate(ChatSession a, ChatSession b) { return b.UpdatedAt.CompareTo(a.UpdatedAt); });
                    return document.Sessions[0];
                }
            }
            catch
            {
                return new ChatSession();
            }
        }

        public static List<ChatSession> LoadRecent(AppSettings settings)
        {
            List<ChatSession> sessions = new List<ChatSession>();
            try
            {
                if (settings == null || settings.Chat == null || !settings.Chat.SaveHistory) return sessions;
                ChatHistoryDocument document = LoadDocument();
                if (document.Sessions == null) return sessions;
                document.Sessions.Sort(delegate(ChatSession a, ChatSession b) { return b.UpdatedAt.CompareTo(a.UpdatedAt); });
                int max = Math.Max(1, Math.Min(settings.Chat.MaxConversations, document.Sessions.Count));
                for (int i = 0; i < max; i++)
                    sessions.Add(document.Sessions[i]);
            }
            catch { }
            return sessions;
        }

        public static ChatSession LoadById(AppSettings settings, string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId)) return null;
            List<ChatSession> sessions = LoadRecent(settings);
            foreach (ChatSession session in sessions)
            {
                if (session != null && string.Equals(session.Id, sessionId, StringComparison.OrdinalIgnoreCase))
                    return session;
            }
            return null;
        }

        public static void SaveLatest(AppSettings settings, ChatSession session)
        {
            if (settings == null || settings.Chat == null || !settings.Chat.SaveHistory || session == null) return;
            try
            {
                if (!Directory.Exists(AppInfo.AppDataDir)) Directory.CreateDirectory(AppInfo.AppDataDir);
                ChatHistoryDocument document = LoadDocument();
                if (document.Sessions == null) document.Sessions = new List<ChatSession>();

                session.UpdatedAt = DateTime.Now;
                if (session.Messages == null) session.Messages = new List<ChatMessage>();
                if (session.Messages.Count > settings.Chat.MaxMessagesPerConversation)
                    session.Messages.RemoveRange(0, session.Messages.Count - settings.Chat.MaxMessagesPerConversation);

                bool replaced = false;
                for (int i = 0; i < document.Sessions.Count; i++)
                {
                    if (document.Sessions[i].Id == session.Id)
                    {
                        document.Sessions[i] = session;
                        replaced = true;
                        break;
                    }
                }
                if (!replaced) document.Sessions.Add(session);

                document.Sessions.Sort(delegate(ChatSession a, ChatSession b) { return b.UpdatedAt.CompareTo(a.UpdatedAt); });
                if (document.Sessions.Count > settings.Chat.MaxConversations)
                    document.Sessions.RemoveRange(settings.Chat.MaxConversations, document.Sessions.Count - settings.Chat.MaxConversations);

                XmlSerializer serializer = new XmlSerializer(typeof(ChatHistoryDocument));
                using (FileStream fs = File.Create(HistoryPath))
                {
                    serializer.Serialize(fs, document);
                }
            }
            catch { }
        }

        public static void Clear()
        {
            try
            {
                if (File.Exists(HistoryPath)) File.Delete(HistoryPath);
            }
            catch { }
        }

        public static void DeleteSession(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId)) return;
            try
            {
                ChatHistoryDocument document = LoadDocument();
                if (document.Sessions == null) return;
                for (int i = document.Sessions.Count - 1; i >= 0; i--)
                {
                    ChatSession session = document.Sessions[i];
                    if (session != null && string.Equals(session.Id, sessionId, StringComparison.OrdinalIgnoreCase))
                        document.Sessions.RemoveAt(i);
                }
                XmlSerializer serializer = new XmlSerializer(typeof(ChatHistoryDocument));
                using (FileStream fs = File.Create(HistoryPath))
                {
                    serializer.Serialize(fs, document);
                }
            }
            catch { }
        }

        private static ChatHistoryDocument LoadDocument()
        {
            try
            {
                if (!File.Exists(HistoryPath)) return new ChatHistoryDocument();
                XmlSerializer serializer = new XmlSerializer(typeof(ChatHistoryDocument));
                using (FileStream fs = File.OpenRead(HistoryPath))
                {
                    ChatHistoryDocument document = serializer.Deserialize(fs) as ChatHistoryDocument;
                    return document == null ? new ChatHistoryDocument() : document;
                }
            }
            catch
            {
                return new ChatHistoryDocument();
            }
        }
    }

    public static class SystemTheme
    {
        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmGetColorizationColor(out uint colorizationColor, out bool colorizationOpaqueBlend);

        public static bool IsLight()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object value = key == null ? null : key.GetValue("AppsUseLightTheme");
                    if (value is int) return ((int)value) != 0;
                }
            }
            catch { }
            return true;
        }

        public static Color AccentColor()
        {
            try
            {
                uint raw;
                bool opaque;
                if (DwmGetColorizationColor(out raw, out opaque) == 0)
                {
                    int r = (int)((raw >> 16) & 0xff);
                    int g = (int)((raw >> 8) & 0xff);
                    int b = (int)(raw & 0xff);
                    Color color = Color.FromArgb(r, g, b);
                    if (color.GetBrightness() > 0.18f && color.GetBrightness() < 0.88f)
                        return color;
                }
            }
            catch { }

            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM"))
                {
                    object value = key == null ? null : key.GetValue("ColorizationColor");
                    if (value is int)
                    {
                        int raw = (int)value;
                        int r = (raw >> 16) & 0xff;
                        int g = (raw >> 8) & 0xff;
                        int b = raw & 0xff;
                        return Color.FromArgb(r, g, b);
                    }
                }
            }
            catch { }

            return Color.FromArgb(0, 120, 215);
        }
    }

    public static class MemoryProbe
    {
        public static string CurrentProcessText()
        {
            try
            {
                Process process = Process.GetCurrentProcess();
                long mb = process.WorkingSet64 / (1024 * 1024);
                return mb.ToString() + " MB";
            }
            catch
            {
                return "n/a";
            }
        }
    }

    public static class MemoryTrimmer
    {
        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool EmptyWorkingSet(IntPtr hProcess);

        public static void TrimCurrentProcess()
        {
            try
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                Process process = Process.GetCurrentProcess();
                EmptyWorkingSet(process.Handle);
            }
            catch { }
        }
    }

    public static class CrashLog
    {
        public static void Write(Exception ex)
        {
            try
            {
                if (!Directory.Exists(AppInfo.AppDataDir))
                    Directory.CreateDirectory(AppInfo.AppDataDir);
                File.AppendAllText(
                    Path.Combine(AppInfo.AppDataDir, "crash.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine +
                    ex.ToString() + Environment.NewLine + Environment.NewLine,
                    Encoding.UTF8);
            }
            catch { }
        }
    }
}
