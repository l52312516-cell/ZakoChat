using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ZakoChat
{
    public sealed class HotkeyManager : NativeWindow, IDisposable
    {
        private const int WmHotkey = 0x0312;
        private const int HotkeyId = 0x5A31;
        private const int ModNoRepeat = 0x4000;
        private bool _registered;

        public event EventHandler HotkeyPressed;

        public HotkeyManager()
        {
            CreateHandle(new CreateParams());
        }

        public void RegisterFromSettings(HotkeySettings settings)
        {
            if (settings == null) return;
            settings.Normalize();
            Unregister();

            if (!settings.Enabled)
            {
                settings.LastStatus = "快捷键已关闭。";
                return;
            }

            if (TryRegister(settings.PreferredModifiers, settings.PreferredKey))
            {
                settings.ActiveModifiers = settings.PreferredModifiers;
                settings.ActiveKey = settings.PreferredKey;
                settings.LastStatus = "已注册 " + Format(settings.ActiveModifiers, settings.ActiveKey) + "。再次按下可显示或隐藏 Zako Chat。";
                return;
            }

            int preferredError = Marshal.GetLastWin32Error();
            if (settings.FallbackModifiers != 0 && settings.FallbackKey != 0 &&
                (settings.FallbackModifiers != settings.PreferredModifiers || settings.FallbackKey != settings.PreferredKey) &&
                TryRegister(settings.FallbackModifiers, settings.FallbackKey))
            {
                settings.ActiveModifiers = settings.FallbackModifiers;
                settings.ActiveKey = settings.FallbackKey;
                settings.LastStatus = Format(settings.PreferredModifiers, settings.PreferredKey) + " 可能被占用，已使用备用快捷键 " + Format(settings.ActiveModifiers, settings.ActiveKey) + "。";
                return;
            }

            int fallbackError = Marshal.GetLastWin32Error();
            settings.ActiveModifiers = 0;
            settings.ActiveKey = 0;
            settings.LastStatus = "快捷键注册失败，可能已被其他程序占用。错误码 " + preferredError.ToString() + "/" + fallbackError.ToString() + "。";
        }

        public void Unregister()
        {
            if (!_registered) return;
            try
            {
                UnregisterHotKey(Handle, HotkeyId);
            }
            catch { }
            _registered = false;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmHotkey && m.WParam.ToInt32() == HotkeyId)
            {
                if (HotkeyPressed != null) HotkeyPressed(this, EventArgs.Empty);
                return;
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            Unregister();
            DestroyHandle();
        }

        private bool TryRegister(int modifiers, int key)
        {
            bool ok = RegisterHotKey(Handle, HotkeyId, modifiers | ModNoRepeat, key);
            _registered = ok;
            return ok;
        }

        public static string Format(int modifiers, int key)
        {
            if (key == 0) return "未设置";
            string text = string.Empty;
            if ((modifiers & HotkeyModifiers.Win) != 0) text += "Win+";
            if ((modifiers & HotkeyModifiers.Control) != 0) text += "Ctrl+";
            if ((modifiers & HotkeyModifiers.Alt) != 0) text += "Alt+";
            if ((modifiers & HotkeyModifiers.Shift) != 0) text += "Shift+";
            Keys keys = (Keys)key;
            text += keys.ToString();
            return text;
        }

        public static int ModifiersFromKeys(Keys modifiers)
        {
            int result = 0;
            if ((modifiers & Keys.Control) == Keys.Control) result |= HotkeyModifiers.Control;
            if ((modifiers & Keys.Alt) == Keys.Alt) result |= HotkeyModifiers.Alt;
            if ((modifiers & Keys.Shift) == Keys.Shift) result |= HotkeyModifiers.Shift;
            return result;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }
}
