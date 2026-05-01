using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace DBD_Trans.Helpers
{
    public static class DarkTitleBarHelper
    {
        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        public static void ApplyDarkTitleBar(Window window)
        {
            if (window == null) return;

            window.SourceInitialized += (s, e) =>
            {
                var handle = new WindowInteropHelper(window).Handle;
                int useDarkMode = 1;
                DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDarkMode, sizeof(int));
            };
        }
    }
}