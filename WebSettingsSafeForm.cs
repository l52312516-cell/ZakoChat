using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ZakoChat
{
    public sealed class WebSettingsSafeForm : Form, ISettingsSurface
    {
        private readonly AppSettings _settings;
        private readonly SecretStore _secrets;
        private readonly IChatClient _client;
        private readonly JavaScriptSerializer _json;
        private WebView2 _web;
        private ThemePalette _theme;
        private bool _ready;
        private string _probeProviderId;
        private string _probeText;
        private List<AiModelInfo> _probeModels;

        public event EventHandler SettingsSaved;

        public WebSettingsSafeForm(AppSettings settings, SecretStore secrets, IChatClient client)
        {
            _settings = settings;
            _settings.Normalize();
            _secrets = secrets;
            _client = client;
            _json = new JavaScriptSerializer();
            _json.MaxJsonLength = 1024 * 1024 * 4;
            _theme = ThemePalette.Resolve(_settings);
            _probeProviderId = string.Empty;
            _probeText = string.Empty;
            _probeModels = new List<AiModelInfo>();

            Text = "Zako Chat \u8bbe\u7f6e";
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(940, 720);
            MinimumSize = new Size(800, 580);
            BackColor = _theme.WindowBottom;
            InitializeWeb();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _web != null) _web.Dispose();
            base.Dispose(disposing);
        }

        private async void InitializeWeb()
        {
            try
            {
                _web = new WebView2();
                _web.Dock = DockStyle.Fill;
                _web.AllowExternalDrop = false;
                _web.DefaultBackgroundColor = _theme.WindowBottom;
                Controls.Add(_web);
                _web.CoreWebView2InitializationCompleted += OnWebInitialized;
                Directory.CreateDirectory(AppInfo.WebView2UserDataDir);
                CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(null, AppInfo.WebView2UserDataDir, null);
                await _web.EnsureCoreWebView2Async(environment);
            }
            catch (Exception ex)
            {
                CrashLog.Write(ex);
                MessageBox.Show("\u9ad8\u7ea7\u8bbe\u7f6e\u754c\u9762\u542f\u52a8\u5931\u8d25\uff0c\u8bf7\u5207\u6362\u5230\u539f\u751f\u8f7b\u91cf\u754c\u9762\u3002", AppInfo.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Close();
            }
        }

        private void OnWebInitialized(object sender, CoreWebView2InitializationCompletedEventArgs e)
        {
            if (!e.IsSuccess || _web == null || _web.CoreWebView2 == null)
            {
                CrashLog.Write(e.InitializationException ?? new InvalidOperationException("WebView2 settings failed."));
                Close();
                return;
            }
            _web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            _web.CoreWebView2.Settings.AreDevToolsEnabled = false;
            _web.CoreWebView2.WebMessageReceived += OnWebMessage;
            _web.NavigateToString(BuildHtml());
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
            else if (action == "switchProvider")
            {
                ProviderConfig provider = _settings.FindProvider(GetString(msg, "providerId"));
                if (provider != null)
                {
                    _settings.Chat.DefaultProviderId = provider.Id;
                    _settings.Chat.DefaultModelId = provider.DefaultModelId;
                    _settings.Chat.DefaultImageModelId = provider.DefaultImageModelId;
                    _probeText = string.Empty;
                    _probeModels.Clear();
                    _settings.Normalize();
                    PostState();
                }
            }
            else if (action == "probe")
            {
                ProbeFromMessage(msg);
            }
            else if (action == "save")
            {
                SaveFromMessage(msg);
            }
            else if (action == "choosePreviewCache")
            {
                ChoosePreviewCacheDir();
            }
            else if (action == "clearPreviewCache")
            {
                ClearPreviewCache();
            }
            else if (action == "close")
            {
                Close();
            }
        }

        private void ProbeFromMessage(Dictionary<string, object> msg)
        {
            ProviderConfig current = _settings.FindProvider(GetString(msg, "providerId"));
            if (current == null || _client == null) return;
            ProviderConfig probeProvider = current.CloneEditable();
            ApplyProviderFields(probeProvider, msg);
            string apiKey = GetString(msg, "apiKey");
            if (apiKey.Length == 0) apiKey = _secrets.Load(probeProvider.ApiKeySecretId);
            _probeProviderId = probeProvider.Id;
            _probeModels.Clear();
            _probeText = "\u6b63\u5728\u68c0\u6d4b...";
            PostState();

            ConnectionProbeResult result = _client.Probe(probeProvider, apiKey, 30000);
            if (result.Success)
            {
                _probeModels = result.Models ?? new List<AiModelInfo>();
                _probeText = "\u8fde\u63a5\u6210\u529f\uff0c\u5ef6\u8fdf " + result.LatencyMs + " ms\uff0c\u83b7\u53d6\u5230 " + _probeModels.Count + " \u4e2a\u6a21\u578b\u3002";
                if (_probeModels.Count > 0 && string.IsNullOrEmpty(_settings.Chat.DefaultModelId))
                    _settings.Chat.DefaultModelId = _probeModels[0].Id;
            }
            else
            {
                _probeText = "\u68c0\u6d4b\u5931\u8d25\uff1a" + (string.IsNullOrEmpty(result.ErrorMessage) ? ("HTTP " + result.StatusCode) : result.ErrorMessage);
            }
            PostState();
        }

        private void SaveFromMessage(Dictionary<string, object> msg)
        {
            ProviderConfig provider = _settings.FindProvider(GetString(msg, "providerId"));
            if (provider != null)
            {
                _settings.Chat.DefaultProviderId = provider.Id;
                ApplyProviderFields(provider, msg);
                string apiKey = GetString(msg, "apiKey");
                if (apiKey.Length > 0) _secrets.Save(provider.ApiKeySecretId, apiKey);
            }

            _settings.Chat.DefaultModelId = GetString(msg, "modelId").Trim();
            _settings.Chat.DefaultImageModelId = GetString(msg, "imageModelId").Trim();
            _settings.Chat.StreamResponses = GetBool(msg, "stream");
            _settings.Chat.ImageGenerationEnabled = GetBool(msg, "imageEnabled");
            _settings.Chat.Temperature = GetDecimal(msg, "temperature", _settings.Chat.Temperature);
            _settings.Chat.MaxTokens = GetInt(msg, "maxTokens", _settings.Chat.MaxTokens);
            _settings.Chat.ImageSize = GetString(msg, "imageSize").Trim();
            _settings.Chat.ImageCount = GetInt(msg, "imageCount", _settings.Chat.ImageCount);
            _settings.Chat.ImagePreviewCacheDir = GetString(msg, "imagePreviewCacheDir").Trim();
            _settings.Chat.MaxUploadImageMb = GetInt(msg, "maxUploadImageMb", _settings.Chat.MaxUploadImageMb);
            _settings.Window.Edge = GetString(msg, "edge") == "left" ? SidebarEdge.Left : SidebarEdge.Right;
            _settings.Appearance.RenderMode = (UiRenderMode)GetInt(msg, "renderMode", (int)_settings.Appearance.RenderMode);
            _settings.Appearance.Theme = (ThemeMode)GetInt(msg, "theme", (int)_settings.Appearance.Theme);
            _settings.Appearance.AnimationEnabled = GetBool(msg, "animation");
            _settings.Appearance.ReducedMotion = GetBool(msg, "reducedMotion");
            _settings.Chat.MaxConversations = GetInt(msg, "maxConversations", _settings.Chat.MaxConversations);
            _settings.Chat.MaxMessagesPerConversation = GetInt(msg, "maxMessages", _settings.Chat.MaxMessagesPerConversation);
            _settings.Chat.CurrentPersonaId = GetString(msg, "personaId");
            PersonaProfile persona = _settings.FindPersona(_settings.Chat.CurrentPersonaId);
            if (persona != null) persona.Prompt = GetString(msg, "personaPrompt");

            _settings.Normalize();
            SettingsStore.Save(_settings);
            if (SettingsSaved != null) SettingsSaved(this, EventArgs.Empty);
            _probeText = "\u5df2\u4fdd\u5b58\u8bbe\u7f6e\u3002";
            PostState();
        }

        private void ApplyProviderFields(ProviderConfig provider, Dictionary<string, object> msg)
        {
            provider.BaseUrl = GetString(msg, "baseUrl").Trim();
            provider.ChatPath = GetString(msg, "chatPath").Trim();
            provider.ModelListPath = GetString(msg, "modelListPath").Trim();
            provider.ExtraHeaders = GetString(msg, "extraHeaders");
            provider.SupportsStreaming = GetBool(msg, "supportsStreaming");
            provider.SupportsImageGeneration = GetBool(msg, "supportsImageGeneration");
            provider.SupportsVision = GetBool(msg, "supportsVision");
            provider.VisionApiKind = (VisionApiKind)GetInt(msg, "visionApiKind", (int)provider.VisionApiKind);
            provider.MaxUploadImageMb = GetInt(msg, "maxUploadImageMb", _settings.Chat.MaxUploadImageMb);
            provider.ImageApiKind = (ImageApiKind)GetInt(msg, "imageApiKind", (int)provider.ImageApiKind);
            provider.ImagePath = GetString(msg, "imagePath").Trim();
            provider.DefaultImageModelId = GetString(msg, "imageModelId").Trim();
        }

        private void ChoosePreviewCacheDir()
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "\u9009\u62e9\u56fe\u7247\u9884\u89c8\u7f13\u5b58\u76ee\u5f55";
                dialog.SelectedPath = _settings.Chat.EffectiveImagePreviewCacheDir;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _settings.Chat.ImagePreviewCacheDir = dialog.SelectedPath;
                    PostState();
                }
            }
        }

        private void ClearPreviewCache()
        {
            try
            {
                string dir = _settings.Chat.EffectiveImagePreviewCacheDir;
                if (Directory.Exists(dir))
                {
                    foreach (string file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                    {
                        try { File.Delete(file); }
                        catch { }
                    }
                }
                MessageBox.Show("\u56fe\u7247\u9884\u89c8\u7f13\u5b58\u5df2\u6e05\u7406\u3002", AppInfo.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                CrashLog.Write(ex);
                MessageBox.Show("\u6e05\u7406\u7f13\u5b58\u5931\u8d25\uff1a" + ex.Message, AppInfo.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void PostState()
        {
            if (!_ready || _web == null || _web.CoreWebView2 == null) return;
            _theme = ThemePalette.Resolve(_settings);
            _web.CoreWebView2.PostWebMessageAsJson(_json.Serialize(BuildState()));
        }

        private Dictionary<string, object> BuildState()
        {
            ProviderConfig current = _settings.FindProvider(_settings.Chat.DefaultProviderId);
            Dictionary<string, object> state = new Dictionary<string, object>();
            state["type"] = "settings";
            state["themeObj"] = WebQuickPromptForm.ThemeObject(_theme, _settings);
            state["providerId"] = _settings.Chat.DefaultProviderId;
            state["modelId"] = _settings.Chat.DefaultModelId;
            state["stream"] = _settings.Chat.StreamResponses;
            state["imageEnabled"] = _settings.Chat.ImageGenerationEnabled;
            state["temperature"] = (double)_settings.Chat.Temperature;
            state["maxTokens"] = _settings.Chat.MaxTokens;
            state["imageSize"] = _settings.Chat.ImageSize;
            state["imageCount"] = _settings.Chat.ImageCount;
            state["imagePreviewCacheDir"] = _settings.Chat.EffectiveImagePreviewCacheDir;
            state["maxUploadImageMb"] = _settings.Chat.MaxUploadImageMb;
            state["edge"] = _settings.Window.Edge == SidebarEdge.Left ? "left" : "right";
            state["renderMode"] = (int)_settings.Appearance.RenderMode;
            state["theme"] = (int)_settings.Appearance.Theme;
            state["animation"] = _settings.Appearance.AnimationEnabled;
            state["reducedMotion"] = _settings.Appearance.ReducedMotion;
            state["maxConversations"] = _settings.Chat.MaxConversations;
            state["maxMessages"] = _settings.Chat.MaxMessagesPerConversation;
            state["webDataDir"] = AppInfo.WebView2UserDataDir;
            state["providers"] = ProviderObjects();
            state["personas"] = PersonaObjects();
            state["personaId"] = _settings.Chat.CurrentPersonaId;
            state["probeText"] = _probeProviderId == _settings.Chat.DefaultProviderId ? _probeText : string.Empty;
            state["probeModels"] = ModelObjects();
            PersonaProfile currentPersona = _settings.FindPersona(_settings.Chat.CurrentPersonaId);
            state["personaPrompt"] = currentPersona == null ? string.Empty : currentPersona.Prompt;
            if (current != null)
            {
                state["baseUrl"] = current.BaseUrl;
                state["chatPath"] = current.ChatPath;
                state["modelListPath"] = current.ModelListPath;
                state["extraHeaders"] = current.ExtraHeaders;
                state["supportsStreaming"] = current.SupportsStreaming;
                state["supportsImageGeneration"] = current.SupportsImageGeneration;
                state["supportsVision"] = current.SupportsVision;
                state["visionApiKind"] = (int)current.VisionApiKind;
                state["imagePath"] = current.ImagePath;
                state["imageModelId"] = string.IsNullOrEmpty(_settings.Chat.DefaultImageModelId) ? current.DefaultImageModelId : _settings.Chat.DefaultImageModelId;
                state["imageApiKind"] = (int)current.ImageApiKind;
                state["hasApiKey"] = _secrets.Exists(current.ApiKeySecretId);
            }
            return state;
        }

        private List<object> ProviderObjects()
        {
            List<object> items = new List<object>();
            foreach (ProviderConfig provider in _settings.Providers)
            {
                Dictionary<string, object> item = new Dictionary<string, object>();
                item["id"] = provider.Id;
                item["name"] = provider.Name;
                item["icon"] = ProviderIcons.DataUri(provider.Id);
                items.Add(item);
            }
            return items;
        }

        private List<object> ModelObjects()
        {
            List<object> items = new List<object>();
            foreach (AiModelInfo model in _probeModels)
            {
                Dictionary<string, object> item = new Dictionary<string, object>();
                item["id"] = model.Id;
                item["name"] = string.IsNullOrEmpty(model.DisplayName) ? model.Id : model.DisplayName;
                items.Add(item);
            }
            return items;
        }

        private List<object> PersonaObjects()
        {
            List<object> items = new List<object>();
            foreach (PersonaProfile persona in _settings.Personas)
            {
                Dictionary<string, object> item = new Dictionary<string, object>();
                item["id"] = persona.Id;
                item["name"] = persona.Name;
                items.Add(item);
            }
            return items;
        }

        private string BuildHtml()
        {
            return @"<!doctype html><html><head><meta charset='utf-8'><style>" + Css + @"</style></head><body><div class='shell'><aside><div class='brand'><span class='zako'></span><div><b>Zako Chat</b><small>V1.0.0</small></div></div><button data-tab='basic' class='active'>&#24120;&#29992;&#35774;&#32622;</button><button data-tab='vision'>&#22270;&#29255;&#19982;&#35270;&#35273;</button><button data-tab='advanced'>&#39640;&#32423;&#35774;&#32622;</button><button data-tab='persona'>AI &#20154;&#35774;</button><button data-tab='data'>&#21551;&#21160;&#19982;&#25968;&#25454;</button></aside><main><section id='basic'><h2>&#24120;&#29992;&#35774;&#32622;</h2><div class='providers' id='providerList'></div><div id='customBox' class='custom'><label>Base URL<input id='baseUrl' placeholder='https://api.example.com/v1'></label></div><label>API Key<input id='apiKey' type='password' placeholder=''></label><div class='model-row'><label>Model ID<input id='modelId' list='modelList'></label><button id='probe'>&#26816;&#27979;&#24310;&#36831;&#24182;&#33719;&#21462;&#27169;&#22411;</button></div><datalist id='modelList'></datalist><select id='modelSelect' class='model-select'></select><p id='probeText' class='note'></p><div class='grid'><label class='check'>&#27969;&#24335;&#36755;&#20986;<input id='stream' type='checkbox'></label><label class='check'>&#29983;&#22270;&#24320;&#20851;<input id='imageEnabled' type='checkbox'></label></div><div class='grid'><label>&#28201;&#24230;<input id='temperature' type='number' min='0' max='2' step='0.1'></label><label>&#26368;&#22823;&#36755;&#20986;<input id='maxTokens' type='number' min='128' max='32000' step='128'></label></div></section><section id='vision' hidden><h2>&#22270;&#29255;&#19982;&#35270;&#35273;</h2><div class='grid'><label class='check'>&#20801;&#35768;&#19978;&#20256;&#22270;&#29255;&#32473;&#35270;&#35273;&#27169;&#22411;<input id='supportsVision' type='checkbox'></label><label>&#35270;&#35273;&#25509;&#21475;&#31867;&#22411;<select id='visionApiKind'><option value='0'>&#19981;&#25903;&#25345;</option><option value='1'>OpenAI &#20860;&#23481;</option></select></label></div><div class='grid'><label>&#19978;&#20256;&#22270;&#29255;&#22823;&#23567;&#38480;&#21046; MB<input id='maxUploadImageMb' type='number' min='1' max='32'></label><label>&#29983;&#22270;&#40664;&#35748; Model ID<input id='imageModelId'></label></div><label>&#39044;&#35272;&#32531;&#23384;&#30446;&#24405;<div class='path-row'><input id='imagePreviewCacheDir'><button id='chooseCache'>&#36873;&#25321;</button><button id='clearCache'>&#28165;&#29702;</button></div></label><p class='note'>&#29983;&#25104;&#22270;&#29255;&#40664;&#35748;&#21482;&#29992;&#20110;&#20391;&#36793;&#26639;&#39044;&#35272;&#65292;&#19981;&#33258;&#21160;&#20445;&#23384;&#21040;&#19979;&#36733;&#30446;&#24405;&#12290;</p></section><section id='advanced' hidden><h2>&#39640;&#32423;&#35774;&#32622;</h2><details open><summary>&#25509;&#21475;&#20860;&#23481;</summary><label>&#32842;&#22825;&#36335;&#24452;<input id='chatPath'></label><label>&#27169;&#22411;&#21015;&#34920;&#36335;&#24452;<input id='modelListPath'></label><label>&#39069;&#22806;&#35831;&#27714;&#22836;<textarea id='extraHeaders'></textarea></label></details><details><summary>&#29983;&#22270;&#39640;&#32423;</summary><label>&#22270;&#29255;&#25509;&#21475;&#31867;&#22411;<select id='imageApiKind'><option value='0'>&#19981;&#25903;&#25345;</option><option value='1'>OpenAI &#20860;&#23481;</option><option value='2'>Gemini Native</option><option value='3'>OpenRouter</option></select></label><label>&#22270;&#29255;&#25509;&#21475;&#36335;&#24452;<input id='imagePath'></label><div class='grid'><label>&#22270;&#29255;&#23610;&#23544;<input id='imageSize'></label><label>&#22270;&#29255;&#25968;&#37327;<input id='imageCount' type='number' min='1' max='4'></label></div></details><details><summary>&#30028;&#38754;</summary><label>&#28210;&#26579;&#27169;&#24335;<select id='renderMode'><option value='0'>&#33258;&#21160;</option><option value='1'>WebView2 &#39640;&#32423;&#30028;&#38754;</option><option value='2'>&#21407;&#29983;&#36731;&#37327;&#30028;&#38754;</option></select></label><label>&#20027;&#39064;<select id='theme'><option value='0'>&#36319;&#38543; Windows</option><option value='1'>&#27973;&#33394;</option><option value='2'>&#28145;&#33394;</option></select></label><label>&#20391;&#26639;&#20301;&#32622;<select id='edge'><option value='right'>&#21491;&#20391;</option><option value='left'>&#24038;&#20391;</option></select></label><div class='grid'><label class='check'>&#21160;&#30011;<input id='animation' type='checkbox'></label><label class='check'>&#20943;&#23569;&#21160;&#24577;&#25928;&#26524;<input id='reducedMotion' type='checkbox'></label></div></details></section><section id='persona' hidden><h2>AI &#20154;&#35774;</h2><label>&#24403;&#21069;&#20154;&#35774;<select id='personaId'></select></label><label>&#31995;&#32479;&#25552;&#31034;&#35789;<textarea id='personaPrompt'></textarea></label></section><section id='data' hidden><h2>&#21551;&#21160;&#19982;&#25968;&#25454;</h2><div class='grid'><label>&#21382;&#21490;&#20250;&#35805;&#25968;<input id='maxConversations' type='number'></label><label>&#21333;&#20250;&#35805;&#28040;&#24687;&#25968;<input id='maxMessages' type='number'></label></div><p id='paths' class='note'></p><p class='note'>API Key &#20351;&#29992; Windows DPAPI &#24403;&#21069;&#29992;&#25143;&#21152;&#23494;&#20445;&#23384;&#12290;</p></section><footer><button id='cancel'>&#20851;&#38381;</button><button id='save'>&#20445;&#23384;&#35774;&#32622;</button></footer></main></div><script>" + Script + @"</script></body></html>";
        }

        private Dictionary<string, object> Parse(string json)
        {
            try { return _json.Deserialize<Dictionary<string, object>>(json) ?? new Dictionary<string, object>(); }
            catch { return new Dictionary<string, object>(); }
        }

        private static string GetString(Dictionary<string, object> dict, string key)
        {
            return dict.ContainsKey(key) && dict[key] != null ? Convert.ToString(dict[key]) : string.Empty;
        }

        private static bool GetBool(Dictionary<string, object> dict, string key)
        {
            object value;
            if (!dict.TryGetValue(key, out value) || value == null) return false;
            bool b;
            if (bool.TryParse(Convert.ToString(value), out b)) return b;
            return Convert.ToString(value) == "1";
        }

        private static int GetInt(Dictionary<string, object> dict, string key, int fallback)
        {
            object value;
            if (!dict.TryGetValue(key, out value) || value == null) return fallback;
            int parsed;
            return int.TryParse(Convert.ToString(value), out parsed) ? parsed : fallback;
        }

        private static decimal GetDecimal(Dictionary<string, object> dict, string key, decimal fallback)
        {
            object value;
            if (!dict.TryGetValue(key, out value) || value == null) return fallback;
            decimal parsed;
            return decimal.TryParse(Convert.ToString(value), out parsed) ? parsed : fallback;
        }

        private const string Css = @":root{--accent:#2563eb;--accentText:#fff;--text:#1f2328;--sub:#5f6673;--muted:#8a92a3;--windowTop:#f8fbff;--windowBottom:#eef3fb;--line:rgba(0,0,0,.10);--card:rgba(255,255,255,.76);--hover:rgba(0,0,0,.055);--mutedCard:rgba(255,255,255,.42)}*{box-sizing:border-box}body{margin:0;font-family:'Segoe UI Variable Text','Segoe UI','Microsoft YaHei UI',sans-serif;color:var(--text);background:linear-gradient(145deg,var(--windowTop),var(--windowBottom))}body[data-theme='dark']{--line:rgba(255,255,255,.12);--card:rgba(42,45,54,.82);--hover:rgba(255,255,255,.09);--mutedCard:rgba(35,38,46,.74)}.shell{height:100vh;display:grid;grid-template-columns:220px minmax(0,1fr)}aside{padding:22px 14px;border-right:1px solid var(--line);background:var(--mutedCard);backdrop-filter:blur(24px)}.brand{display:flex;align-items:center;gap:10px;margin:0 10px 22px}.brand b{display:block;font-size:17px}.brand small{display:block;color:var(--sub);font-size:11px}.zako{width:30px;height:30px;border-radius:12px;background:conic-gradient(from 215deg,var(--accent),#72f0cf,#8aa8ff,var(--accent))}aside button{width:100%;height:40px;margin:3px 0;border:0;border-radius:12px;text-align:left;padding:0 12px;background:transparent;color:var(--text);font:inherit;cursor:pointer}aside button.active,aside button:hover{background:var(--hover)}main{min-width:0;display:grid;grid-template-rows:1fr 64px}section{padding:24px 30px;overflow:auto}h2{font-size:22px;margin:0 0 20px}label{display:block;margin:14px 0;color:var(--sub);font-size:12px}input,select,textarea{width:100%;margin-top:6px;padding:10px 12px;border-radius:12px;border:1px solid var(--line);background:var(--card);color:var(--text);font:inherit;outline:none}input:focus,select:focus,textarea:focus{border-color:var(--accent);box-shadow:0 0 0 3px rgba(96,165,250,.16)}textarea{min-height:86px;resize:vertical}.grid{display:grid;grid-template-columns:1fr 1fr;gap:14px}.check{display:flex;align-items:center;justify-content:space-between;padding:10px 12px;border-radius:12px;border:1px solid var(--line);background:var(--card)}.check input{width:auto;margin:0}.providers{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:10px}.provider{height:54px;border:1px solid var(--line);border-radius:14px;background:var(--card);display:flex;align-items:center;gap:10px;padding:0 12px;color:var(--text);cursor:pointer}.provider.active{border-color:var(--accent);box-shadow:0 0 0 3px rgba(96,165,250,.13)}.provider img{width:28px;height:28px;object-fit:contain;border-radius:7px}.provider span{white-space:nowrap;overflow:hidden;text-overflow:ellipsis}.custom{display:none}.custom.show{display:block}.model-row{display:grid;grid-template-columns:1fr 180px;gap:10px;align-items:end}.model-row button,.path-row button{height:40px;border:1px solid var(--line);border-radius:12px;background:var(--card);color:var(--text);font:inherit;cursor:pointer}.model-select{display:none}.model-select.show{display:block}.path-row{display:grid;grid-template-columns:1fr 72px 72px;gap:8px}.note{color:var(--muted);font-size:12px;line-height:1.7;white-space:pre-wrap}details{margin:12px 0;padding:14px;border:1px solid var(--line);border-radius:14px;background:var(--card)}summary{cursor:pointer;font-weight:600}footer{display:flex;align-items:center;justify-content:flex-end;gap:10px;padding:12px 20px;border-top:1px solid var(--line);background:var(--mutedCard)}footer button{height:38px;padding:0 18px;border:1px solid var(--line);border-radius:12px;background:var(--card);color:var(--text);font:inherit;cursor:pointer}#save{background:var(--accent);color:var(--accentText);border-color:transparent}";

        private const string Script = @"const $=s=>document.querySelector(s);let state={};function post(m){chrome.webview.postMessage(m)}function css(t){document.body.dataset.theme=t.light?'light':'dark';const r=document.documentElement.style;r.setProperty('--accent',t.accent);r.setProperty('--accentText',t.accentText);r.setProperty('--text',t.text);r.setProperty('--sub',t.subText);r.setProperty('--muted',t.mutedText);r.setProperty('--windowTop',t.windowTop);r.setProperty('--windowBottom',t.windowBottom)}function val(id){const e=$('#'+id);return e.type==='checkbox'?e.checked:e.value}function set(id,v){const e=$('#'+id);if(!e)return;if(e.type==='checkbox')e.checked=!!v;else e.value=v==null?'':v}function payload(action){return {action:action,providerId:state.providerId,apiKey:val('apiKey'),modelId:val('modelId'),stream:val('stream'),imageEnabled:val('imageEnabled'),temperature:val('temperature'),maxTokens:val('maxTokens'),baseUrl:val('baseUrl'),chatPath:val('chatPath'),modelListPath:val('modelListPath'),extraHeaders:val('extraHeaders'),supportsStreaming:val('stream'),supportsImageGeneration:val('imageEnabled'),supportsVision:val('supportsVision'),visionApiKind:val('visionApiKind'),maxUploadImageMb:val('maxUploadImageMb'),imageApiKind:val('imageApiKind'),imagePath:val('imagePath'),imageModelId:val('imageModelId'),imageSize:val('imageSize'),imageCount:val('imageCount'),imagePreviewCacheDir:val('imagePreviewCacheDir'),edge:val('edge'),renderMode:val('renderMode'),theme:val('theme'),animation:val('animation'),reducedMotion:val('reducedMotion'),maxConversations:val('maxConversations'),maxMessages:val('maxMessages'),personaId:val('personaId'),personaPrompt:val('personaPrompt')}}function renderProviders(s){$('#providerList').innerHTML=s.providers.map(p=>`<div class='provider ${p.id===s.providerId?'active':''}' data-id='${p.id}'>${p.icon?`<img src='${p.icon}'>`:''}<span>${p.name}</span></div>`).join('');document.querySelectorAll('.provider').forEach(p=>p.onclick=()=>post({action:'switchProvider',providerId:p.dataset.id}))}function renderModels(s){const models=s.probeModels||[];$('#modelList').innerHTML=models.map(m=>`<option value='${m.id}'>${m.name}</option>`).join('');$('#modelSelect').innerHTML=models.length?'<option value="""">\u9009\u62e9\u68c0\u6d4b\u5230\u7684\u6a21\u578b</option>'+models.map(m=>`<option value='${m.id}'>${m.name}</option>`).join(''):'';$('#modelSelect').classList.toggle('show',models.length>0)}function render(s){state=s;css(s.themeObj);renderProviders(s);renderModels(s);$('#customBox').classList.toggle('show',s.providerId==='custom');$('#personaId').innerHTML=s.personas.map(p=>`<option value='${p.id}'>${p.name}</option>`).join('');['modelId','baseUrl','chatPath','modelListPath','extraHeaders','imagePath','imageModelId','imageSize','edge','personaId','personaPrompt','imagePreviewCacheDir'].forEach(id=>set(id,s[id]));['stream','imageEnabled','supportsStreaming','supportsImageGeneration','supportsVision','animation','reducedMotion'].forEach(id=>set(id,s[id]));['temperature','maxTokens','imageCount','imageApiKind','visionApiKind','renderMode','theme','maxConversations','maxMessages','maxUploadImageMb'].forEach(id=>set(id,s[id]));$('#apiKey').placeholder=s.hasApiKey?'\u5df2\u4fdd\u5b58\uff0c\u7559\u7a7a\u4e0d\u4fee\u6539':'\u8f93\u5165 API Key';$('#probeText').textContent=s.probeText||'';$('#paths').textContent='WebView2 \u6570\u636e\uff1a'+s.webDataDir+'\n\u56fe\u7247\u9884\u89c8\u7f13\u5b58\uff1a'+s.imagePreviewCacheDir}document.querySelectorAll('aside button').forEach(b=>b.onclick=()=>{document.querySelectorAll('aside button').forEach(x=>x.classList.remove('active'));b.classList.add('active');document.querySelectorAll('section').forEach(s=>s.hidden=s.id!==b.dataset.tab)});$('#modelSelect').onchange=()=>{if($('#modelSelect').value)$('#modelId').value=$('#modelSelect').value};$('#probe').onclick=()=>post(payload('probe'));$('#chooseCache').onclick=()=>post({action:'choosePreviewCache'});$('#clearCache').onclick=()=>post({action:'clearPreviewCache'});$('#save').onclick=()=>post(payload('save'));$('#cancel').onclick=()=>post({action:'close'});chrome.webview.addEventListener('message',e=>{if(e.data.type==='settings')render(e.data)});post({action:'ready'});";
    }
}
