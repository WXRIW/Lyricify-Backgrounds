using System.Runtime.InteropServices;

namespace Lyricify.Backgrounds.Hosting.Win32
{
    public sealed class OwnedBackgroundWindowTracker
    {
        private const int GwlExStyle = -20;
        private const int SwHide = 0;
        private const int SwShowNoActivate = 4;
        private const long WsExAppWindow = 0x00040000L;
        private const long WsExToolWindow = 0x00000080L;
        private const long WsExNoActivate = 0x08000000L;
        private const long WsExNoRedirectionBitmap = 0x00200000L;
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpShowWindow = 0x0040;
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpFrameChanged = 0x0020;

        private readonly IntPtr ownerHandle;
        private readonly IntPtr backgroundHandle;

        public OwnedBackgroundWindowTracker(IntPtr ownerHandle, IntPtr backgroundHandle)
        {
            this.ownerHandle = ownerHandle;
            this.backgroundHandle = backgroundHandle;
            HideFromSwitcher();
        }

        public bool Sync()
        {
            if (ownerHandle == IntPtr.Zero || backgroundHandle == IntPtr.Zero ||
                !IsWindow(ownerHandle) || !IsWindow(backgroundHandle))
            {
                return false;
            }

            HideFromSwitcher();
            if (!IsWindowVisible(ownerHandle) || IsIconic(ownerHandle))
            {
                ShowWindow(backgroundHandle, SwHide);
                return true;
            }

            if (!GetWindowRect(ownerHandle, out NativeRect rect)) return false;
            ShowWindow(backgroundHandle, SwShowNoActivate);
            return SetWindowPos(
                backgroundHandle,
                ownerHandle,
                rect.Left,
                rect.Top,
                rect.Right - rect.Left,
                rect.Bottom - rect.Top,
                SwpNoActivate | SwpShowWindow);
        }

        public void Hide() => ShowWindow(backgroundHandle, SwHide);

        private void HideFromSwitcher()
        {
            long style = GetWindowLongPtr(backgroundHandle, GwlExStyle).ToInt64();
            long desired = (style & ~WsExAppWindow) |
                WsExToolWindow | WsExNoActivate | WsExNoRedirectionBitmap;
            if (desired == style) return;
            SetWindowLongPtr(backgroundHandle, GwlExStyle, new IntPtr(desired));
            SetWindowPos(
                backgroundHandle, IntPtr.Zero, 0, 0, 0, 0,
                SwpNoSize | SwpNoMove | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
        }

        private static IntPtr GetWindowLongPtr(IntPtr hwnd, int index) =>
            IntPtr.Size == 8 ? GetWindowLongPtr64(hwnd, index) : new IntPtr(GetWindowLong32(hwnd, index));

        private static IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value) =>
            IntPtr.Size == 8 ? SetWindowLongPtr64(hwnd, index, value) :
                new IntPtr(SetWindowLong32(hwnd, index, value.ToInt32()));

        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong32(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr64(IntPtr hwnd, int index);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong32(IntPtr hwnd, int index, int value);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr64(IntPtr hwnd, int index, IntPtr value);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
    }
}
