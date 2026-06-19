using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Plugins
{
    /// <summary>
    /// Wraps a raw HWND so it can be passed as a Form.Show owner.
    /// </summary>
    internal sealed class WindowWrapper : IWin32Window
    {
        public WindowWrapper(IntPtr handle)
        {
            Handle = handle;
        }

        public IntPtr Handle { get; private set; }
    }

    /// <summary>
    /// Resolves the UCCNC main window handle and applies owned-window relationships.
    /// </summary>
    internal static class UccncWindow
    {
        private const int GwlHwndParent = -8;

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern IntPtr GetWindowLong32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern IntPtr SetWindowLong32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        /// <summary>
        /// Returns the UCCNC main window handle for the current process.
        /// </summary>
        public static IntPtr GetMainHandle()
        {
            var process = Process.GetCurrentProcess();
            IntPtr handle = process.MainWindowHandle;
            if (handle != IntPtr.Zero)
                return handle;

            return FindLargestVisibleTopLevelWindow(process.Id);
        }

        /// <summary>
        /// Sets the owner HWND for a window via GWL_HWNDPARENT.
        /// </summary>
        public static void SetOwner(IntPtr childHandle, IntPtr ownerHandle)
        {
            if (childHandle == IntPtr.Zero || ownerHandle == IntPtr.Zero)
                return;

            SetWindowLong(childHandle, GwlHwndParent, ownerHandle);
        }

        private static IntPtr FindLargestVisibleTopLevelWindow(int processId)
        {
            IntPtr best = IntPtr.Zero;
            long bestArea = 0;

            EnumWindows((hWnd, lParam) =>
            {
                uint pid;
                GetWindowThreadProcessId(hWnd, out pid);
                if (pid != (uint)processId || !IsWindowVisible(hWnd))
                    return true;

                Rect rect;
                if (!GetWindowRect(hWnd, out rect))
                    return true;

                long width = rect.Right - rect.Left;
                long height = rect.Bottom - rect.Top;
                if (width <= 0 || height <= 0)
                    return true;

                long area = width * height;
                if (area > bestArea)
                {
                    bestArea = area;
                    best = hWnd;
                }

                return true;
            }, IntPtr.Zero);

            return best;
        }

        private static IntPtr GetWindowLong(IntPtr hWnd, int nIndex)
        {
            if (IntPtr.Size == 8)
                return GetWindowLongPtr64(hWnd, nIndex);
            return GetWindowLong32(hWnd, nIndex);
        }

        private static void SetWindowLong(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
        {
            if (IntPtr.Size == 8)
                SetWindowLongPtr64(hWnd, nIndex, dwNewLong);
            else
                SetWindowLong32(hWnd, nIndex, dwNewLong);
        }
    }
}
