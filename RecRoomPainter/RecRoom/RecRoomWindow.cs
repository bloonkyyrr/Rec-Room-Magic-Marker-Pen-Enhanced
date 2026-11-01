using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RecRoomPainter
{
    public static class RecRoomWindow
    {
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr SetFocus(IntPtr hWnd);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        public static IntPtr Find()
        {
            IntPtr handle = FindWindow(null, "Rec Room");
            if (handle == IntPtr.Zero)
                handle = FindWindow(null, "Rec Room VR");
            return handle;
        }

        public static bool Focus()
        {
            IntPtr handle = Find();
            if (handle == IntPtr.Zero)
                return false;
            SetForegroundWindow(handle);
            SetFocus(handle);
            return true;
        }

        public static Rectangle Bounds()
        {
            IntPtr handle = Find();
            if (handle != IntPtr.Zero && GetWindowRect(handle, out RECT rect))
            {
                int w = rect.Right - rect.Left;
                int h = rect.Bottom - rect.Top;
                if (w > 50 && h > 50)
                    return new Rectangle(rect.Left, rect.Top, w, h);
            }
            return Screen.PrimaryScreen.Bounds;
        }
    }
}
