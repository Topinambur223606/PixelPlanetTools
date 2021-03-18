using System;
using System.Runtime.InteropServices;

namespace PixelPlanetBot.Captcha
{
    class User32
    {
        //http://www.pinvoke.net/default.aspx/user32.SetWindowPos

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, int hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        public static void SendWindowToBackground(IntPtr hWnd)
        {
            SetWindowPos(hWnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        private const int HWND_BOTTOM = 1;

        private const uint
            SWP_NOSIZE = 0x0001,
            SWP_NOMOVE = 0x0002,
            SWP_NOACTIVATE = 0x0010;
    }
}
