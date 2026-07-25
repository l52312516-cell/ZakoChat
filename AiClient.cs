using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace ZakoChat
{
    public sealed class AiModelInfo
    {
        public string Id { get; set; }
        public string Owner { get; set; }
        public string DisplayName { get; set; }
        public string SourceProviderId { get; set; }

        public AiModelInfo()
        {
            Id = string.Empty;
            Owner = string.Empty;
            DisplayName = string.Empty;
            SourceProviderId = string.Empty;
        }

        public override string ToString()
        {
            return string.IsNullOrEmpty(DisplayName) ? Id : DisplayName;
        }
    }

    public sealed class ConnectionProbeResult
    {
        public bool Success { get; set; }
        public int LatencyMs { get; set; }
        public List<AiModelInfo> Models { get; set; }
        public int StatusCode { get; set; }
        public string ErrorMessage { get; set; }

        public ConnectionProbeResult()
        {
            Models = new List<AiModelInfo>();
            ErrorMessage = string.Empty;
        }
    }

    public sealed class ChatOptions
    {
        public string ModelId { get; set; }
        public decimal Temperature { get; set; }
        public int MaxTokens { get; set; }
        public bool Stream { get; set; }
        public string PersonaPrompt { get; set; }

        public ChatOptions()
        {
            ModelId = string.Empty;
            Temperature = 0.7m;
            MaxTokens = 2048;
            Stream = true;
            PersonaPrompt = string.Empty;
        }
    }

    public sealed class ChatResponse
    {
        public bool Success { get; set; }
        public string Content { get; set; }
        public int StatusCode { get; set; }
        public string ErrorMessage { get; set; }

        public ChatResponse()
        {
            Content = string.Empty;
            ErrorMessage = string.Empty;
        }
    }

    public sealed class ImageGenerationOptions
    {
        public string ModelId { get; set; }
        public string Prompt { get; set; }
        public string Size { get; set; }
        public int Count { get; set; }
        public string PreviewCacheDir { get; set; }

        public ImageGenerationOptions()
        {
            ModelId = string.Empty;
            Prompt = string.Empty;
            Size = "1024x1024";
            Count = 1;
            PreviewCacheDir = string.Empty;
        }
    }

    public sealed class GeneratedImage
    {
        public string LocalPath { get; set; }
        public string SourceUrl { get; set; }
        public string RevisedPrompt { get; set; }
        public string DataUrl { get; set; }

        public GeneratedImage()
        {
            LocalPath = string.Empty;
            SourceUrl = string.Empty;
            RevisedPrompt = string.Empty;
            DataUrl = string.Empty;
        }
    }

    public sealed class ImageGenerationResult
    {
        public bool Success { get; set; }
        public int StatusCode { get; set; }
        public string ErrorMessage { get; set; }
        public List<GeneratedImage> Images { get; set; }

        public ImageGenerationResult()
        {
            ErrorMessage = string.Empty;
            Images = new List<GeneratedImage>();
        }
    }

    public interface IChatClient
    {
        ConnectionProbeResult Probe(ProviderConfig provider, string apiKey, int timeoutMs);
        ChatResponse SendChat(ProviderConfig provider, string apiKey, IList<ChatMessage> messages, ChatOptions options, Action<string> onDelta, CancellationToken token);
    }

    public interface IImageGenerationClient
    {
        ImageGenerationResult GenerateImage(ProviderConfig provider, string apiKey, ImageGenerationOptions options, CancellationToken token);
    }

    public sealed class OpenAiCompatibleChatClient : IChatClient, IImageGenerationClient
    {
        private readonly JavaScriptSerializer _json;

        public OpenAiCompatibleChatClient()
        {
            _json = new JavaScriptSerializer();
            _json.MaxJsonLength = 1024 * 1024 * 8;
            try
            {
                ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol | (SecurityProtocolType)3072;
            }
            catch { }
        }

        public ConnectionProbeResult Probe(ProviderConfig provider, string apiKey, int timeoutMs)
        {
            ConnectionProbeResult result = new ConnectionProbeResult();
            Stopwatch sw = Stopwatch.StartNew();

            try
            {
                if (provider == null) throw new InvalidOperationException("尚未选择服务商。");
                provider.Normalize();
                if (string.IsNullOrEmpty(provider.BaseUrl))
                    throw new InvalidOperationException("Base URL 为空。");
                if (string.IsNullOrEmpty(apiKey))
                    throw new InvalidOperationException("API Key 为空。");

                HttpWebRequest request = CreateRequest(provider, provider.ModelListPath, "GET", apiKey, timeoutMs);
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (Stream stream = response.GetResponseStream())
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                {
                    string body = reader.ReadToEnd();
                    result.StatusCode = (int)response.StatusCode;
                    result.Models = ParseModels(body, provider.Id);
                    result.Success = response.StatusCode == HttpStatusCode.OK;
                    result.LatencyMs = (int)sw.ElapsedMilliseconds;
                    if (result.Models.Count == 0)
                        result.ErrorMessage = "连接成功，但服务没有返回模型列表。你可以手动填写 Model ID。";
                }
            }
            catch (WebException ex)
            {
                result.LatencyMs = (int)sw.ElapsedMilliseconds;
                FillWebError(result, ex);
            }
            catch (Exception ex)
            {
                result.LatencyMs = (int)sw.ElapsedMilliseconds;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        public ChatResponse SendChat(ProviderConfig provider, string apiKey, IList<ChatMessage> messages, ChatOptions options, Action<string> onDelta, CancellationToken token)
        {
            ChatResponse result = new ChatResponse();
            HttpWebRequest request = null;
            try
            {
                if (provider == null) throw new InvalidOperationException("尚未选择服务商。");
                provider.Normalize();
                if (string.IsNullOrEmpty(apiKey))
                    throw new InvalidOperationException("API Key 为空。");
                if (options == null) options = new ChatOptions();
                if (string.IsNullOrEmpty(options.ModelId))
                    throw new InvalidOperationException("Model ID 为空。");

                string body = BuildChatRequest(messages, options);
                request = CreateRequest(provider, provider.ChatPath, "POST", apiKey, 120000);
                byte[] bytes = Encoding.UTF8.GetBytes(body);
                request.ContentType = "application/json; charset=utf-8";
                request.ContentLength = bytes.Length;

                using (token.Register(delegate { TryAbort(request); }))
                {
                    using (Stream requestStream = request.GetRequestStream())
                    {
                        requestStream.Write(bytes, 0, bytes.Length);
                    }

                    using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                    {
                        result.StatusCode = (int)response.StatusCode;
                        if (options.Stream && provider.SupportsStreaming)
                        {
                            result.Content = ReadStreamingResponse(response, onDelta, token);
                        }
                        else
                        {
                            using (Stream stream = response.GetResponseStream())
                            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                            {
                                result.Content = ParseChatContent(reader.ReadToEnd());
                                if (onDelta != null) onDelta(result.Content);
                            }
                        }
                        result.Success = true;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                result.ErrorMessage = "已取消。";
            }
            catch (WebException ex)
            {
                if (token.IsCancellationRequested)
                    result.ErrorMessage = "已取消。";
                else
                    FillChatWebError(result, ex);
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
            }
            return result;
        }

        public ImageGenerationResult GenerateImage(ProviderConfig provider, string apiKey, ImageGenerationOptions options, CancellationToken token)
        {
            ImageGenerationResult result = new ImageGenerationResult();
            HttpWebRequest request = null;
            try
            {
                if (provider == null) throw new InvalidOperationException("尚未选择服务商。");
                provider.Normalize();
                if (!provider.SupportsImageGeneration || provider.ImageApiKind == ImageApiKind.None)
                    throw new InvalidOperationException(provider.Name + " 当前未配置生图接口。");
                if (string.IsNullOrEmpty(apiKey))
                    throw new InvalidOperationException("API Key 为空。");
                if (options == null) options = new ImageGenerationOptions();
                if (string.IsNullOrEmpty(options.Prompt))
                    throw new InvalidOperationException("图片提示词为空。");
                if (string.IsNullOrEmpty(options.ModelId))
                    options.ModelId = provider.DefaultImageModelId;
                if (string.IsNullOrEmpty(options.ModelId))
                    throw new InvalidOperationException("图片 Model ID 为空。");

                string body = BuildImageRequest(provider, options);
                string path = provider.ImagePath;
                request = CreateRequest(provider, path, "POST", apiKey, 180000);
                byte[] bytes = Encoding.UTF8.GetBytes(body);
                request.ContentType = "application/json; charset=utf-8";
                request.ContentLength = bytes.Length;

                using (token.Register(delegate { TryAbort(request); }))
                {
                    using (Stream requestStream = request.GetRequestStream())
                    {
                        requestStream.Write(bytes, 0, bytes.Length);
                    }

                    using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                    using (Stream stream = response.GetResponseStream())
                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        result.StatusCode = (int)response.StatusCode;
                        result.Images = ParseAndSaveImages(reader.ReadToEnd(), options, token);
                        result.Success = result.Images.Count > 0;
                        if (!result.Success) result.ErrorMessage = "服务已响应，但没有返回可保存的图片。";
                    }
                }
            }
            catch (OperationCanceledException)
            {
                result.ErrorMessage = "已取消。";
            }
            catch (WebException ex)
            {
                if (token.IsCancellationRequested)
                    result.ErrorMessage = "已取消。";
                else
                    FillImageWebError(result, ex);
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
            }
            return result;
        }

        private static void TryAbort(HttpWebRequest request)
        {
            try
            {
                if (request != null) request.Abort();
            }
            catch { }
        }

        private HttpWebRequest CreateRequest(ProviderConfig provider, string path, string method, string apiKey, int timeoutMs)
        {
            string url = CombineUrl(provider.BaseUrl, path);
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = method;
            request.Timeout = timeoutMs;
            request.ReadWriteTimeout = timeoutMs;
            request.UserAgent = "ZakoChat/" + AppInfo.Version;
            request.Accept = "application/json";
            request.Headers[HttpRequestHeader.Authorization] = "Bearer " + apiKey;
            ApplyExtraHeaders(request, provider.ExtraHeaders);
            return request;
        }

        private static string CombineUrl(string baseUrl, string path)
        {
            if (path == null) path = string.Empty;
            if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return path;
            if (!path.StartsWith("/")) path = "/" + path;
            return (baseUrl ?? string.Empty).TrimEnd('/') + path;
        }

        private static void ApplyExtraHeaders(HttpWebRequest request, string extraHeaders)
        {
            if (string.IsNullOrEmpty(extraHeaders)) return;
            string[] lines = extraHeaders.Replace("\r", "").Split('\n');
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                int colon = line.IndexOf(':');
                if (colon <= 0) continue;
                string name = line.Substring(0, colon).Trim();
                string value = line.Substring(colon + 1).Trim();
                if (name.Length == 0) continue;
                if (string.Equals(name, "Authorization", StringComparison.OrdinalIgnoreCase))
                    request.Headers[HttpRequestHeader.Authorization] = value;
                else if (string.Equals(name, "User-Agent", StringComparison.OrdinalIgnoreCase))
                    request.UserAgent = value;
                else if (string.Equals(name, "Accept", StringComparison.OrdinalIgnoreCase))
                    request.Accept = value;
                else if (string.Equals(name, "Content-Type", StringComparison.OrdinalIgnoreCase))
                    request.ContentType = value;
                else
                    request.Headers[name] = value;
            }
        }

        private string BuildChatRequest(IList<ChatMessage> messages, ChatOptions options)
        {
            Dictionary<string, object> root = new Dictionary<string, object>();
            root["model"] = options.ModelId;
            root["stream"] = options.Stream;
            root["temperature"] = (double)options.Temperature;
            root["max_tokens"] = options.MaxTokens;

            List<object> jsonMessages = new List<object>();
            if (!string.IsNullOrEmpty(options.PersonaPrompt))
            {
                Dictionary<string, object> system = new Dictionary<string, object>();
                system["role"] = "system";
                system["content"] = options.PersonaPrompt;
                jsonMessages.Add(system);
            }

            if (messages != null)
            {
                foreach (ChatMessage message in messages)
                {
                    if (message == null || string.IsNullOrEmpty(message.Content)) continue;
                    if (message.Role != "user" && message.Role != "assistant" && message.Role != "system") continue;
                    Dictionary<string, object> item = new Dictionary<string, object>();
                    item["role"] = message.Role;
                    if (message.Role == "user" && !string.IsNullOrEmpty(message.AttachmentPath))
                        item["content"] = BuildVisionContent(message);
                    else
                        item["content"] = message.Content;
                    jsonMessages.Add(item);
                }
            }

            root["messages"] = jsonMessages.ToArray();
            return _json.Serialize(root);
        }

        private object[] BuildVisionContent(ChatMessage message)
        {
            List<object> content = new List<object>();
            Dictionary<string, object> text = new Dictionary<string, object>();
            text["type"] = "text";
            text["text"] = message.Content ?? string.Empty;
            content.Add(text);

            string dataUrl = BuildImageDataUrl(message.AttachmentPath);
            if (dataUrl.Length > 0)
            {
                Dictionary<string, object> image = new Dictionary<string, object>();
                image["type"] = "image_url";
                Dictionary<string, object> imageUrl = new Dictionary<string, object>();
                imageUrl["url"] = dataUrl;
                image["image_url"] = imageUrl;
                content.Add(image);
            }
            return content.ToArray();
        }

        private static string BuildImageDataUrl(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return string.Empty;
                byte[] bytes = File.ReadAllBytes(path);
                string ext = Path.GetExtension(path).ToLowerInvariant();
                string mime = ext == ".jpg" || ext == ".jpeg" ? "image/jpeg" :
                    ext == ".webp" ? "image/webp" :
                    ext == ".gif" ? "image/gif" : "image/png";
                return "data:" + mime + ";base64," + Convert.ToBase64String(bytes);
            }
            catch
            {
                return string.Empty;
            }
        }

        private List<AiModelInfo> ParseModels(string body, string providerId)
        {
            List<AiModelInfo> models = new List<AiModelInfo>();
            object parsed = _json.DeserializeObject(body);
            Dictionary<string, object> root = parsed as Dictionary<string, object>;
            if (root == null || !root.ContainsKey("data")) return models;
            object[] data = root["data"] as object[];
            if (data == null) return models;
            foreach (object item in data)
            {
                Dictionary<string, object> dict = item as Dictionary<string, object>;
                if (dict == null || !dict.ContainsKey("id")) continue;
                AiModelInfo model = new AiModelInfo();
                model.Id = Convert.ToString(dict["id"]);
                model.DisplayName = model.Id;
                model.SourceProviderId = providerId;
                if (dict.ContainsKey("owned_by")) model.Owner = Convert.ToString(dict["owned_by"]);
                if (!string.IsNullOrEmpty(model.Id)) models.Add(model);
            }
            models.Sort(delegate(AiModelInfo a, AiModelInfo b) { return string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase); });
            return models;
        }

        private string ParseChatContent(string body)
        {
            object parsed = _json.DeserializeObject(body);
            Dictionary<string, object> root = parsed as Dictionary<string, object>;
            if (root == null || !root.ContainsKey("choices")) return string.Empty;
            object[] choices = root["choices"] as object[];
            if (choices == null || choices.Length == 0) return string.Empty;
            Dictionary<string, object> choice = choices[0] as Dictionary<string, object>;
            if (choice == null || !choice.ContainsKey("message")) return string.Empty;
            Dictionary<string, object> message = choice["message"] as Dictionary<string, object>;
            if (message == null || !message.ContainsKey("content")) return string.Empty;
            return Convert.ToString(message["content"]);
        }

        private string ReadStreamingResponse(HttpWebResponse response, Action<string> onDelta, CancellationToken token)
        {
            StringBuilder content = new StringBuilder();
            using (Stream stream = response.GetResponseStream())
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
            {
                while (!reader.EndOfStream)
                {
                    if (token.IsCancellationRequested) throw new OperationCanceledException();
                    string line = reader.ReadLine();
                    if (line == null) break;
                    line = line.Trim();
                    if (line.Length == 0 || line.StartsWith(":", StringComparison.Ordinal)) continue;
                    if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;

                    string data = line.Substring(5).Trim();
                    if (data == "[DONE]") break;
                    string delta = ParseStreamDelta(data);
                    if (delta.Length > 0)
                    {
                        content.Append(delta);
                        if (onDelta != null) onDelta(delta);
                    }
                }
            }
            return content.ToString();
        }

        private string ParseStreamDelta(string data)
        {
            try
            {
                object parsed = _json.DeserializeObject(data);
                Dictionary<string, object> root = parsed as Dictionary<string, object>;
                if (root == null || !root.ContainsKey("choices")) return string.Empty;
                object[] choices = root["choices"] as object[];
                if (choices == null || choices.Length == 0) return string.Empty;
                Dictionary<string, object> choice = choices[0] as Dictionary<string, object>;
                if (choice == null || !choice.ContainsKey("delta")) return string.Empty;
                Dictionary<string, object> delta = choice["delta"] as Dictionary<string, object>;
                if (delta == null || !delta.ContainsKey("content")) return string.Empty;
                return Convert.ToString(delta["content"]);
            }
            catch
            {
                return string.Empty;
            }
        }

        private string BuildImageRequest(ProviderConfig provider, ImageGenerationOptions options)
        {
            Dictionary<string, object> root = new Dictionary<string, object>();
            if (provider.ImageApiKind == ImageApiKind.GeminiNative)
            {
                root["model"] = options.ModelId;
                root["prompt"] = options.Prompt;
                root["size"] = options.Size;
                root["n"] = options.Count;
            }
            else
            {
                root["model"] = options.ModelId;
                root["prompt"] = options.Prompt;
                root["size"] = options.Size;
                root["n"] = options.Count;
            }
            return _json.Serialize(root);
        }

        private List<GeneratedImage> ParseAndSaveImages(string body, ImageGenerationOptions options, CancellationToken token)
        {
            List<GeneratedImage> images = new List<GeneratedImage>();
            object parsed = _json.DeserializeObject(body);
            CollectImages(parsed, images, options, token);
            return images;
        }

        private void CollectImages(object node, List<GeneratedImage> images, ImageGenerationOptions options, CancellationToken token)
        {
            if (node == null || images.Count >= Math.Max(1, options.Count)) return;
            if (token.IsCancellationRequested) throw new OperationCanceledException();

            Dictionary<string, object> dict = node as Dictionary<string, object>;
            if (dict != null)
            {
                string revised = GetString(dict, "revised_prompt");
                string url = GetString(dict, "url");
                string b64 = GetString(dict, "b64_json");
                if (b64.Length == 0 && dict.ContainsKey("inlineData"))
                {
                    Dictionary<string, object> inline = dict["inlineData"] as Dictionary<string, object>;
                    if (inline != null) b64 = GetString(inline, "data");
                }
                if (b64.Length == 0 && dict.ContainsKey("inline_data"))
                {
                    Dictionary<string, object> inline = dict["inline_data"] as Dictionary<string, object>;
                    if (inline != null) b64 = GetString(inline, "data");
                }
                if (b64.Length == 0) b64 = GetString(dict, "data");

                if (url.Length > 0 || LooksLikeBase64Image(b64))
                {
                    GeneratedImage image = new GeneratedImage();
                    image.SourceUrl = url;
                    image.RevisedPrompt = revised;
                    if (url.Length > 0)
                    {
                        image.LocalPath = SaveUrlImage(url, options.PreviewCacheDir, token);
                    }
                    else
                    {
                        image.DataUrl = NormalizeDataUrl(b64);
                        image.LocalPath = SaveBase64Image(b64, options.PreviewCacheDir);
                    }
                    if (image.LocalPath.Length > 0 || image.SourceUrl.Length > 0 || image.DataUrl.Length > 0) images.Add(image);
                }

                foreach (object value in dict.Values)
                    CollectImages(value, images, options, token);
                return;
            }

            object[] array = node as object[];
            if (array != null)
            {
                foreach (object item in array)
                    CollectImages(item, images, options, token);
            }
        }

        private static bool LooksLikeBase64Image(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            if (value.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)) return true;
            return value.Length > 128 && value.IndexOf(" ") < 0 && value.IndexOf("{") < 0;
        }

        private static string NormalizeDataUrl(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            if (value.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)) return value;
            return "data:image/png;base64," + value;
        }

        private static string SaveBase64Image(string value, string cacheDir)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            int comma = value.IndexOf(',');
            if (value.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase) && comma >= 0)
                value = value.Substring(comma + 1);
            byte[] bytes = Convert.FromBase64String(value);
            return SaveImageBytes(bytes, "png", cacheDir);
        }

        private static string SaveUrlImage(string url, string cacheDir, CancellationToken token)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            request.Timeout = 120000;
            request.ReadWriteTimeout = 120000;
            request.UserAgent = "ZakoChat/" + AppInfo.Version;
            using (token.Register(delegate { TryAbort(request); }))
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (Stream stream = response.GetResponseStream())
            using (MemoryStream ms = new MemoryStream())
            {
                stream.CopyTo(ms);
                return SaveImageBytes(ms.ToArray(), GuessExtension(response.ContentType), cacheDir);
            }
        }

        private static string SaveImageBytes(byte[] bytes, string extension, string cacheDir)
        {
            if (bytes == null || bytes.Length == 0) return string.Empty;
            if (string.IsNullOrEmpty(cacheDir)) cacheDir = AppInfo.ImagePreviewCacheDir;
            if (!Directory.Exists(cacheDir)) Directory.CreateDirectory(cacheDir);
            extension = string.IsNullOrEmpty(extension) ? "png" : extension.TrimStart('.');
            string fileName = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + "." + extension;
            string path = Path.Combine(cacheDir, fileName);
            File.WriteAllBytes(path, bytes);
            return path;
        }

        private static string GuessExtension(string contentType)
        {
            if (string.IsNullOrEmpty(contentType)) return "png";
            contentType = contentType.ToLowerInvariant();
            if (contentType.Contains("jpeg") || contentType.Contains("jpg")) return "jpg";
            if (contentType.Contains("webp")) return "webp";
            if (contentType.Contains("gif")) return "gif";
            return "png";
        }

        private static string GetString(Dictionary<string, object> dict, string key)
        {
            return dict != null && dict.ContainsKey(key) && dict[key] != null ? Convert.ToString(dict[key]) : string.Empty;
        }

        private static void FillWebError(ConnectionProbeResult result, WebException ex)
        {
            HttpWebResponse response = ex.Response as HttpWebResponse;
            if (response != null)
            {
                result.StatusCode = (int)response.StatusCode;
                result.ErrorMessage = ReadErrorBody(response);
                response.Close();
            }
            else
            {
                result.ErrorMessage = ex.Message;
            }
        }

        private static void FillChatWebError(ChatResponse result, WebException ex)
        {
            HttpWebResponse response = ex.Response as HttpWebResponse;
            if (response != null)
            {
                result.StatusCode = (int)response.StatusCode;
                result.ErrorMessage = ReadErrorBody(response);
                response.Close();
            }
            else
            {
                result.ErrorMessage = ex.Message;
            }
        }

        private static void FillImageWebError(ImageGenerationResult result, WebException ex)
        {
            HttpWebResponse response = ex.Response as HttpWebResponse;
            if (response != null)
            {
                result.StatusCode = (int)response.StatusCode;
                result.ErrorMessage = ReadErrorBody(response);
                response.Close();
            }
            else
            {
                result.ErrorMessage = ex.Message;
            }
        }

        private static string ReadErrorBody(HttpWebResponse response)
        {
            try
            {
                using (Stream stream = response.GetResponseStream())
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                {
                    string body = reader.ReadToEnd();
                    if (body.Length > 500) body = body.Substring(0, 500);
                    return ((int)response.StatusCode).ToString() + " " + response.StatusDescription + ": " + body;
                }
            }
            catch
            {
                return ((int)response.StatusCode).ToString() + " " + response.StatusDescription;
            }
        }
    }
}
