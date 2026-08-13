using Vortice.Direct3D11;
using Win32Presenter = Lyricify.Backgrounds.Hosting.Win32.CompositionSwapChainPresenter;

namespace Lyricify.Backgrounds.Hosting.Wpf;

/// <summary>
/// WPF-hosting facade for presenting a D3D11 texture through a
/// composition swap chain attached to the host HWND.
/// </summary>
public sealed class CompositionSwapChainPresenter : IDisposable
{
    private readonly Win32Presenter presenter;

    public CompositionSwapChainPresenter(
        ID3D11Device device,
        IntPtr windowHandle,
        Action? firstFramePresented = null)
    {
        presenter = new Win32Presenter(device, windowHandle, firstFramePresented);
    }

    public void EnsureSize(int width, int height, float scaleX, float scaleY) =>
        presenter.EnsureSize(width, height, scaleX, scaleY);

    public void Present(ID3D11DeviceContext context, ID3D11Texture2D source) =>
        presenter.Present(context, source);

    public void Dispose() => presenter.Dispose();
}
