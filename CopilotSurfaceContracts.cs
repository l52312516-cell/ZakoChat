using System;
using System.Collections.Generic;
using System.Drawing;

namespace ZakoChat
{
    public interface ISettingsSurface
    {
        event EventHandler SettingsSaved;
    }

    public interface IQuickPromptSurface : IDisposable
    {
        event Action<string> SendRequested;
        event EventHandler ExpandRequested;
        event EventHandler SettingsRequested;
        event EventHandler HideRequested;

        bool Visible { get; }
        int Left { get; }
        int Top { get; }

        void Activate();
        void ApplySettings();
        void SetIcon(Icon icon);
        void SetStatus(string providerId, string providerName, string modelName, string memoryText);
        void SetBusy(bool busy);
        void ShowQuickAnimated();
        void HideQuickAnimated();
        void HideQuick();
    }

    public interface ISidebarSurface : IDisposable
    {
        event EventHandler SidebarStateChanged;
        event EventHandler NewChatRequested;
        event Action<string> SessionSelected;
        event Action<string> SessionDeleteRequested;
        event EventHandler EdgeToggleRequested;
        event Action<string> ImageGenerationRequested;
        event Action<string, string> VisionSendRequested;

        SidebarState State { get; }
        bool Visible { get; }
        bool IsDisposed { get; }
        bool IsHandleCreated { get; }
        bool InvokeRequired { get; }
        int Width { get; }

        void Activate();
        IAsyncResult BeginInvoke(Delegate method, params object[] args);
        void ApplySettings();
        void SetIcon(Icon icon);
        void SetProviderStatus(string providerId, string provider, string model, string memory);
        void SetBusy(bool busy);
        void SetImageBusy(bool busy);
        object AddMessage(ChatMessage message);
        void UpdateBubble(object bubble, string text, bool append);
        void LoadMessages(IEnumerable<ChatMessage> messages);
        void LoadSessions(IList<ChatSession> sessions, string selectedId);
        void ShowSidebarAnimated();
        void HideSidebarAnimated();
    }
}
