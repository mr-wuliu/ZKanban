# Zhipu Usage Widget

基于 WPF + WebView2 的智谱用量桌面悬浮组件。

## 功能

- 悬浮窗常驻桌面，支持拖拽和手动刷新
- 本地配置智谱账号、密码、刷新间隔
- 使用 WebView2 维护官网登录态并抓取 `https://bigmodel.cn/coding-plan/personal/usage`
- 凭据使用当前 Windows 用户的 DPAPI 加密后保存到 `%AppData%\\ZhipuUsageWidget\\settings.json`
- 适合直接发布到 GitHub，仓库内不包含真实账号密码

## 运行环境

- Windows 10/11
- .NET 8 SDK
- Microsoft Edge WebView2 Runtime

## 本地运行

```powershell
cd .\ZhipuUsageWidget
dotnet restore
dotnet run
```

首次启动后点击“设置”，填写智谱账号和密码即可。

## 发布

```powershell
cd .\ZhipuUsageWidget
dotnet publish -c Release -r win-x64 --self-contained false
```

输出目录默认在：

`ZhipuUsageWidget\bin\Release\net8.0-windows\win-x64\publish`

## 说明

- 当前抓取逻辑基于官网页面文本和常见登录表单做启发式识别；如果智谱页面结构改版，可能需要调整 `Services/BigModelAutomationService.cs`
- 为了避免敏感信息进入 Git 仓库，本项目没有把任何真实账号密码写入代码或配置文件
