using Microsoft.UI.Xaml.Controls;
using SharpGen.Runtime;
using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Format = Vortice.DXGI.Format;

namespace Lyricify.Backgrounds.AppleMusicInspired.WinUI;

internal sealed class SwapChainPanelPresenter : IDisposable
{
    private static readonly Guid NativeInterfaceId =
        new("63aad0b8-7c24-40ff-85a8-640d944cc325");

    private readonly SwapChainPanel panel;
    private readonly Action? firstFramePresented;
    private ID3D11Device? device;
    private IntPtr panelReference;
    private IntPtr panelNative;
    private IDXGISwapChain1? swapChain;
    private int width;
    private int height;
    private bool firstFrameRaised;

    public SwapChainPanelPresenter(
        SwapChainPanel panel,
        Action? firstFramePresented = null)
    {
        this.panel = panel ?? throw new ArgumentNullException(nameof(panel));
        this.firstFramePresented = firstFramePresented;
        AttachNativePanel();
    }

    public void Initialize(ID3D11Device value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (device?.NativePointer == value.NativePointer) return;
        ReleaseSwapChain();
        device = value;
    }

    public void EnsureSize(int pixelWidth, int pixelHeight, float scaleX, float scaleY)
    {
        pixelWidth = Math.Max(1, pixelWidth);
        pixelHeight = Math.Max(1, pixelHeight);
        if (swapChain == null)
        {
            if (device == null) throw new InvalidOperationException("Presenter is not initialized.");
            using IDXGIDevice dxgiDevice = device.QueryInterface<IDXGIDevice>();
            using IDXGIAdapter adapter = dxgiDevice.GetAdapter();
            using IDXGIFactory2 factory = adapter.GetParent<IDXGIFactory2>();
            var description = new SwapChainDescription1(
                pixelWidth,
                pixelHeight,
                Format.B8G8R8A8_UNorm,
                false,
                Usage.RenderTargetOutput,
                2,
                Scaling.Stretch,
                SwapEffect.FlipSequential,
                AlphaMode.Ignore,
                SwapChainFlags.None);
            swapChain = factory.CreateSwapChainForComposition(device, description, null);
            SetSwapChain(swapChain.NativePointer);
        }
        else if (width != pixelWidth || height != pixelHeight)
        {
            swapChain.ResizeBuffers(
                2,
                pixelWidth,
                pixelHeight,
                Format.B8G8R8A8_UNorm,
                SwapChainFlags.None).CheckError();
        }

        width = pixelWidth;
        height = pixelHeight;
        using IDXGISwapChain2 swapChain2 = swapChain.QueryInterface<IDXGISwapChain2>();
        SetMatrixTransform(
            swapChain2.NativePointer,
            Matrix3x2.CreateScale(
                1f / Math.Max(0.001f, scaleX),
                1f / Math.Max(0.001f, scaleY)));
    }

    public void Present(ID3D11DeviceContext context, ID3D11Texture2D source)
    {
        if (swapChain == null) return;
        using ID3D11Texture2D backBuffer = swapChain.GetBuffer<ID3D11Texture2D>(0);
        context.CopyResource(backBuffer, source);
        swapChain.Present(1, PresentFlags.None).CheckError();
        if (firstFrameRaised) return;
        firstFrameRaised = true;
        firstFramePresented?.Invoke();
    }

    private void AttachNativePanel()
    {
        panelReference = WinRT.MarshalInspectable<SwapChainPanel>.FromManaged(panel);
        Guid iid = NativeInterfaceId;
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(
            panelReference,
            ref iid,
            out panelNative));
    }

    private unsafe void SetSwapChain(IntPtr value)
    {
        void** vtable = *(void***)panelNative;
        var setSwapChain = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)vtable[3];
        Marshal.ThrowExceptionForHR(setSwapChain(panelNative, value));
    }

    private static unsafe void SetMatrixTransform(IntPtr swapChain2, Matrix3x2 value)
    {
        // IDXGISwapChain2::SetMatrixTransform is vtable slot 34. Vortice 3.2
        // exposes IDXGISwapChain2 but does not wrap this method.
        void** vtable = *(void***)swapChain2;
        var setMatrixTransform =
            (delegate* unmanaged[Stdcall]<IntPtr, Matrix3x2*, int>)vtable[34];
        Marshal.ThrowExceptionForHR(setMatrixTransform(swapChain2, &value));
    }

    public void Dispose()
    {
        ReleaseSwapChain();
        if (panelNative != IntPtr.Zero)
        {
            Marshal.Release(panelNative);
            panelNative = IntPtr.Zero;
        }
        if (panelReference != IntPtr.Zero)
        {
            Marshal.Release(panelReference);
            panelReference = IntPtr.Zero;
        }
    }

    private void ReleaseSwapChain()
    {
        if (panelNative != IntPtr.Zero && swapChain != null)
        {
            SetSwapChain(IntPtr.Zero);
        }
        swapChain?.Dispose();
        swapChain = null;
        width = 0;
        height = 0;
        firstFrameRaised = false;
    }
}
