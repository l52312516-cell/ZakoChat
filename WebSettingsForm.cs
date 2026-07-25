namespace ZakoChat
{
    internal static class SettingsSurfaceFactory
    {
        public static System.Windows.Forms.Form CreateSettingsForm(AppSettings settings, SecretStore secrets, IChatClient client)
        {
            bool preferWeb = settings == null || settings.Appearance == null || settings.Appearance.RenderMode != UiRenderMode.Native;
            if (preferWeb && WebView2RuntimeBootstrap.TryPrepare())
            {
                try { return new WebSettingsSafeForm(settings, secrets, client); }
                catch (System.Exception ex) { CrashLog.Write(ex); }
            }
            return new SettingsForm(settings, secrets, client);
        }
    }
}
