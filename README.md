# Zako Chat

Zako Chat 是一款面向 Windows 10/11 的轻量 AI 对话组件。它以托盘常驻和快捷键唤起为核心，默认打开快速提问小窗，也可以展开为接近 Windows 11 Copilot 使用习惯的右侧/左侧侧边栏。

当前版本：**V1.0.0**

## 软件亮点

- **双形态体验**：快速小窗适合临时提问，完整侧边栏适合连续对话、查看历史和切换模式。
- **接近系统组件的观感**：WebView2 高级界面使用 Fluent 风格图标、圆角窗口、柔和阴影、浅色/深色主题和短动画；WebView2 不可用时会回退到原生 WinForms 轻量界面。
- **多服务商兼容**：内置 OpenAI、Gemini、DeepSeek、智谱、硅基流动、Kimi、OpenRouter、OneAPI/NewAPI 与自定义接口。
- **自动检测模型**：填写 API Key 后可以检测连接延迟并获取可用模型列表，也允许手动填写 Model ID。
- **文字与图片模式分离**：侧边栏顶部可在文字对话和图片生成之间切换；输入框旁图片按钮用于上传图片给视觉模型。
- **隐私本地优先**：API Key 使用 Windows DPAPI 按当前用户加密保存，聊天历史和预览缓存默认保存在本机。
- **单 exe 发布**：Release 目录只保留 `ZakoChat-V1.0.0.exe`，不散落 DLL、脚本或图标文件。

## 主要功能

- `Ctrl+Shift+Z` 显示/隐藏 Zako Chat。
- 托盘菜单支持快速提问、展开侧边栏、新建对话、设置、清空本地历史和退出。
- 支持流式输出、温度、最大输出长度、人设提示词、历史容量等设置。
- 支持服务商图标/徽章展示，模型列表与当前模型状态会跟随服务商变化。
- 支持视觉图片上传，适配 OpenAI-compatible 多模态消息格式。
- 支持文本生图预览，生成结果默认只用于侧边栏预览，不自动保存到下载目录。

## 使用方式

1. 下载并运行 `release\ZakoChat-V1.0.0.exe`。
2. 在设置中选择服务商，填写 `API Key`、`Base URL` 和 `Model ID`。
3. 点击“检测延迟并获取模型”验证接口可用性。
4. 使用 `Ctrl+Shift+Z` 快速显示或隐藏窗口。
5. 在侧边栏顶部选择“文字对话”或“图片生成”模式。

## 构建方式

项目保持 C# WinForms + .NET Framework 路线，并嵌入 WebView2 SDK 依赖作为高级 UI 渲染层。构建不需要 Electron、WebView 或 NuGet 在线恢复。

在项目根目录运行：

```powershell
powershell -ExecutionPolicy Bypass -File E:\worke\ZakoChat\build.ps1
```

构建产物：

- `bin\ZakoChat.exe`
- `release\ZakoChat-V1.0.0.exe`

## 目录说明

- `*.cs`：主程序源码。
- `ico\ZakoChat.ico`：应用图标。
- `packages\Microsoft.Web.WebView2.*`：WebView2 编译引用与内嵌资源来源。
- `build.ps1`：正式构建脚本。
- `run.ps1`：本地运行脚本。
- `release\ZakoChat-V1.0.0.exe`：适合直接发布的单文件程序。

## 隐私与安全

Zako Chat 不内置云端账号系统。API Key 使用 Windows DPAPI 当前用户加密保存；聊天历史、WebView2 数据、图片预览缓存默认保存在 `%AppData%\ZakoChat`，图片预览缓存目录也可以在设置中自定义。第三方服务标识归各自所有者所有。

## 已知说明

- 推荐安装 Microsoft Edge WebView2 Runtime，以获得更好的 Fluent 风格界面。
- 当前首个正式版专注文字对话、视觉图片上传和文本生图预览，不包含语音、文件上传、图片编辑、局部重绘或视频生成。
- 不同服务商的 API 兼容程度不同；自定义中转站可通过高级设置调整路径、请求头和模型 ID。
