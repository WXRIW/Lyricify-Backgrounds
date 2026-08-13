# Lyricify Backgrounds

Reusable background renderers and framework adapters for Lyricify applications.

## Projects

- `Lyricify.Backgrounds` contains framework-neutral contracts.
- `Lyricify.Backgrounds.Hosting.Win32` contains HWND and DirectComposition hosting helpers.
- `Lyricify.Backgrounds.Hosting.Wpf` is the single WPF hosting entry point. It contains the reusable STA thread, hidden `HwndSource` host, and the WPF-facing composition swap-chain presenter.
- `Lyricify.Backgrounds.AppleMusicInspired` contains shared settings, artwork loading, mesh generation, spectrum analysis, and shader resources.
	- `.Wpf` contains the production Direct3D renderer and the Apple Music Inspired adapter for the shared WPF host.
	- `.WinUI` contains the native WinUI adapter and presents through `SwapChainPanel`.
- `Demo.Shared`, `Demo.Wpf`, and `Demo.WinUI` provide a common parameter model and two demo shells.