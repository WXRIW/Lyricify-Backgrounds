# Lyricify Backgrounds

简体中文 | [English](README.en.md)

面向 Lyricify 的可复用动态背景渲染器。本仓库将跨框架契约、背景配置、Windows 基础设施、框架渲染器和宿主 Catalog 分层，使应用可以通过稳定的背景 ID 枚举和创建背景。

## 可用背景

| ID | 显示名称 | 说明 |
| --- | --- | --- |
| `lyricify.backgrounds.apple-music-inspired.ios` | Apple Music Inspired · iOS | 现代版本，包含当前 mesh、色彩处理、模糊、旋转图层和可选的音频响应低频脉冲。 |
| `lyricify.backgrounds.apple-music-inspired.ios-classic` | Apple Music Inspired · iOS Classic | 基于 iOS 13 的经典版本。 |

两个背景均支持 WPF 和 WinUI，并提供封面切换、播放与可见状态更新、竖屏和横屏布局、明暗外观、歌词模式、首帧通知、错误上报及资源释放。

### 核心与宿主

| 项目 | 职责 |
| --- | --- |
| `Lyricify.Backgrounds` | 跨框架契约，包括 Provider、Settings、State、Artwork、Session 和错误类型。 |
| `Lyricify.Backgrounds.Hosting.Win32` | HWND 和 DirectComposition 宿主辅助设施。 |
| `Lyricify.Backgrounds.Hosting.Wpf` | WPF Factory 接口、Instance 和 `WpfBackgroundCatalog`。 |
| `Lyricify.Backgrounds.Hosting.WinUI` | WinUI Factory 接口、Instance 和 `WinUIBackgroundCatalog`。 |

### Apple Music Inspired 家族

| 项目 | 职责 |
| --- | --- |
| `Lyricify.Backgrounds.AppleMusicInspired.Windows` | 供框架渲染器共用的 Windows 图片解码和 Apple Music 专用系统音频分析。 |
| `Lyricify.Backgrounds.AppleMusicInspired.Ios` | 现代版本的公开 Settings 和 Provider。 |
| `Lyricify.Backgrounds.AppleMusicInspired.Ios.Shared` | 现代版本的跨框架 mesh 和 HLSL 资源。 |
| `Lyricify.Backgrounds.AppleMusicInspired.Ios.Wpf` | 现代版本的 WPF Renderer、Session 和 Factory。 |
| `Lyricify.Backgrounds.AppleMusicInspired.Ios.WinUI` | 现代版本的 WinUI Renderer、Session 和 Factory。 |
| `Lyricify.Backgrounds.AppleMusicInspired.IosClassic` | iOS 13 版本的公开 Settings 和 Provider。 |
| `Lyricify.Backgrounds.AppleMusicInspired.IosClassic.Shared` | iOS 13 版本的跨框架 mesh 和 HLSL 资源。 |
| `Lyricify.Backgrounds.AppleMusicInspired.IosClassic.Wpf` | iOS 13 版本的 WPF Renderer、Session 和 Factory。 |
| `Lyricify.Backgrounds.AppleMusicInspired.IosClassic.WinUI` | iOS 13 版本的 WinUI Renderer、Session 和 Factory。 |

`Demo.Shared`、`Demo.Wpf` 和 `Demo.WinUI` 提供共用参数模型及两个 Demo 外壳。`Tests.Wpf` 和 `Tests.WinUI` 覆盖 Catalog 校验、Factory 创建、Shader 资源、View 所有权和资源释放行为。

## 命名规则

背景包遵循以下格式：

```text
Lyricify.Backgrounds.<Family>.<Variant>.<Framework>
```

基础设施包可以使用 `.Shared`、`.Windows` 或 `.Hosting.<Framework>` 后缀。背景 ID 统一使用 `lyricify.backgrounds.*` 下的小写 kebab-case。

## 创建背景

WPF 示例：

```csharp
var catalog = new WpfBackgroundCatalog(
[
    new AppleMusicIosWpfBackgroundFactory(lightTheme: true),
    new AppleMusicIosClassicWpfBackgroundFactory(lightTheme: true),
]);

WpfBackgroundInstance background = catalog.Create(
    AppleMusicIosBackgroundProvider.BackgroundId);

backgroundHost.Children.Add(background.View);
background.Session.UpdateState(new BackgroundState
{
    IsPlaying = true,
    IsVertical = true,
    IsLightTheme = true,
    IsBehindLyrics = false,
    IsVisible = true,
});

await background.Session.SetArtworkAsync(
    new BackgroundArtwork(artworkId, encodedArtwork));

// 移除 View，并在不再使用背景时释放 Instance。
background.Dispose();
```

WinUI 使用相同的 Provider ID 和 Session 契约，对应类型为 `WinUIBackgroundCatalog`、`AppleMusicIosWinUIBackgroundFactory` 和 `AppleMusicIosClassicWinUIBackgroundFactory`。

未知 ID、重复注册和错误的 Settings 类型都会抛出包含明确原因的异常。

## 构建与测试

WPF 项目面向 Windows 上的 .NET 6，WinUI 和 Demo 项目面向 Windows 上的 .NET 8，跨框架契约和 Profile 使用 .NET Standard。

```powershell
dotnet build Lyricify.Backgrounds.slnx
dotnet test Lyricify.Backgrounds.Tests.Wpf\Lyricify.Backgrounds.Tests.Wpf.csproj
dotnet test Lyricify.Backgrounds.Tests.WinUI\Lyricify.Backgrounds.Tests.WinUI.csproj
```

运行 `Lyricify.Backgrounds.Demo.Wpf` 或 `Lyricify.Backgrounds.Demo.WinUI`，可以交互式比较两个版本并调整参数。

## 许可证

本项目使用 [Apache License 2.0](LICENSE.txt)。
