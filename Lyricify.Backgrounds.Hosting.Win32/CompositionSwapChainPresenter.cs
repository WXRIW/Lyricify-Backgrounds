using System;
using Vortice.Direct3D11;
using Vortice.DirectComposition;
using Vortice.DXGI;
using Format = Vortice.DXGI.Format;

namespace Lyricify.Backgrounds.Hosting.Win32
{
    public sealed class CompositionSwapChainPresenter : IDisposable
    {
        private readonly ID3D11Device device;
        private readonly IDCompositionDevice compositionDevice;
        private readonly IDCompositionTarget compositionTarget;
        private readonly IDCompositionVisual compositionVisual;
        private readonly IDCompositionScaleTransform scaleTransform;
        private Action? firstFramePresented;
        private IDXGISwapChain1? swapChain;
        private int width;
        private int height;

        public CompositionSwapChainPresenter(
            ID3D11Device device,
            IntPtr windowHandle,
            Action? firstFramePresented = null)
        {
            this.device = device ?? throw new ArgumentNullException(nameof(device));
            this.firstFramePresented = firstFramePresented;
            using IDXGIDevice dxgiDevice = device.QueryInterface<IDXGIDevice>();
            compositionDevice = DComp.DCompositionCreateDevice<IDCompositionDevice>(dxgiDevice);
            compositionDevice.CreateTargetForHwnd(windowHandle, true, out compositionTarget).CheckError();
            compositionDevice.CreateVisual(out compositionVisual).CheckError();
            compositionDevice.CreateScaleTransform(out scaleTransform).CheckError();
            compositionVisual.SetTransform(scaleTransform).CheckError();
            compositionTarget.SetRoot(compositionVisual).CheckError();
        }

        public IDXGISwapChain1? SwapChain => swapChain;

        public void EnsureSize(int width, int height, float scaleX, float scaleY)
        {
            width = Math.Max(1, width);
            height = Math.Max(1, height);
            if (swapChain is null)
            {
                using IDXGIDevice dxgiDevice = device.QueryInterface<IDXGIDevice>();
                using IDXGIAdapter adapter = dxgiDevice.GetAdapter();
                using IDXGIFactory2 factory = adapter.GetParent<IDXGIFactory2>();
                var description = new SwapChainDescription1(
                    width, height, Format.B8G8R8A8_UNorm, false,
                    Usage.RenderTargetOutput, 2, Scaling.Stretch,
                    SwapEffect.FlipSequential, AlphaMode.Ignore,
                    SwapChainFlags.None);
                swapChain = factory.CreateSwapChainForComposition(device, description, null);
                compositionVisual.SetContent(swapChain).CheckError();
            }
            else if (this.width != width || this.height != height)
            {
                swapChain.ResizeBuffers(2, width, height, Format.B8G8R8A8_UNorm, SwapChainFlags.None)
                    .CheckError();
            }

            this.width = width;
            this.height = height;
            scaleTransform.SetScaleX(scaleX).CheckError();
            scaleTransform.SetScaleY(scaleY).CheckError();
            compositionDevice.Commit().CheckError();
        }

        public void Present(ID3D11DeviceContext context, ID3D11Texture2D source)
        {
            if (swapChain is null) return;
            using ID3D11Texture2D backBuffer = swapChain.GetBuffer<ID3D11Texture2D>(0);
            context.CopyResource(backBuffer, source);
            swapChain.Present(1, PresentFlags.None).CheckError();
            Action? callback = firstFramePresented;
            firstFramePresented = null;
            callback?.Invoke();
        }

        public void Dispose()
        {
            compositionVisual.SetContent(null);
            compositionTarget.SetRoot(null);
            compositionDevice.Commit();
            swapChain?.Dispose();
            scaleTransform.Dispose();
            compositionVisual.Dispose();
            compositionTarget.Dispose();
            compositionDevice.Dispose();
        }
    }
}
