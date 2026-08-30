# Lyricify Backgrounds

[简体中文](README.md) | English

Reusable dynamic background renderers for Lyricify. This repository separates cross-framework contracts, background configuration, Windows infrastructure, framework renderers, and host catalogs into distinct layers, allowing applications to enumerate and create backgrounds through stable IDs.

## Available backgrounds

| ID | Display name | Description |
| --- | --- | --- |
| `lyricify.backgrounds.apple-music-inspired.ios` | Apple Music Inspired · iOS | The modern version, including its current mesh, color treatment, blur, rotation layers, and optional audio-reactive bass pulse. |
| `lyricify.backgrounds.apple-music-inspired.ios-classic` | Apple Music Inspired · iOS Classic | The classic version based on iOS 13. |

Both backgrounds support WPF and WinUI, artwork switching, playback and visibility state updates, portrait and landscape layouts, light and dark appearances, lyrics mode, first-frame notification, fault reporting, and resource disposal.

### Core and hosting

| Project | Responsibility |
| --- | --- |
| `Lyricify.Backgrounds` | Framework-neutral contracts: providers, settings, state, artwork, sessions, and errors. |
| `Lyricify.Backgrounds.Hosting.Win32` | HWND and DirectComposition hosting helpers. |
| `Lyricify.Backgrounds.Hosting.Wpf` | WPF factory interfaces, instances, and `WpfBackgroundCatalog`. |
| `Lyricify.Backgrounds.Hosting.WinUI` | WinUI factory interfaces, instances, and `WinUIBackgroundCatalog`. |

### Apple Music Inspired family

| Project | Responsibility |
| --- | --- |
| `Lyricify.Backgrounds.AppleMusicInspired.Windows` | Windows artwork decoding and Apple Music-specific system-audio analysis shared by framework renderers. |
| `Lyricify.Backgrounds.AppleMusicInspired.Ios` | Public settings and provider for the modern version. |
| `Lyricify.Backgrounds.AppleMusicInspired.Ios.Shared` | Cross-framework mesh and HLSL resources for the modern version. |
| `Lyricify.Backgrounds.AppleMusicInspired.Ios.Wpf` | WPF renderer, session, and factory for the modern version. |
| `Lyricify.Backgrounds.AppleMusicInspired.Ios.WinUI` | WinUI renderer, session, and factory for the modern version. |
| `Lyricify.Backgrounds.AppleMusicInspired.IosClassic` | Public settings and provider for the iOS 13 version. |
| `Lyricify.Backgrounds.AppleMusicInspired.IosClassic.Shared` | Cross-framework mesh and HLSL resources for the iOS 13 version. |
| `Lyricify.Backgrounds.AppleMusicInspired.IosClassic.Wpf` | WPF renderer, session, and factory for the iOS 13 version. |
| `Lyricify.Backgrounds.AppleMusicInspired.IosClassic.WinUI` | WinUI renderer, session, and factory for the iOS 13 version. |

`Demo.Shared`, `Demo.Wpf`, and `Demo.WinUI` provide a shared parameter model and two demo shells. `Tests.Wpf` and `Tests.WinUI` cover catalog validation, factory creation, shader resources, view ownership, and disposal behavior.

## Naming conventions

Background packages follow:

```text
Lyricify.Backgrounds.<Family>.<Variant>.<Framework>
```

Infrastructure packages may use the `.Shared`, `.Windows`, or `.Hosting.<Framework>` suffix. Background IDs consistently use lowercase kebab-case under `lyricify.backgrounds.*`.

## Creating a background

WPF example:

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

// Remove the view and dispose the instance when it is no longer needed.
background.Dispose();
```

WinUI uses the same provider IDs and session contracts. The corresponding types are `WinUIBackgroundCatalog`, `AppleMusicIosWinUIBackgroundFactory`, and `AppleMusicIosClassicWinUIBackgroundFactory`.

Unknown IDs, duplicate registrations, and settings of the wrong type throw exceptions with explicit reasons.

## Build and test

The WPF projects target .NET 6 for Windows, the WinUI and Demo projects target .NET 8 for Windows, and the framework-neutral contracts and profiles target .NET Standard.

```powershell
dotnet build Lyricify.Backgrounds.slnx
dotnet test Lyricify.Backgrounds.Tests.Wpf\Lyricify.Backgrounds.Tests.Wpf.csproj
dotnet test Lyricify.Backgrounds.Tests.WinUI\Lyricify.Backgrounds.Tests.WinUI.csproj
```

Run `Lyricify.Backgrounds.Demo.Wpf` or `Lyricify.Backgrounds.Demo.WinUI` to compare the two versions and adjust their settings interactively.

## License

This project is licensed under the [Apache License 2.0](LICENSE.txt).
