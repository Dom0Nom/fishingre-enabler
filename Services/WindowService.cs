using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace MinecraftMonitor.Services
{
    public class WindowService
    {
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern IntPtr PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        [DllImport("user32.dll")]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        public bool TryFindWindowByPid(int pid, out IntPtr hwnd)
        {
            IntPtr foundHandle = IntPtr.Zero;
            IntPtr fallbackHandle = IntPtr.Zero;

            EnumWindows((hWnd, lParam) =>
            {
                GetWindowThreadProcessId(hWnd, out uint windowPid);
                if (windowPid == pid)
                {
                    // Store first match as fallback
                    if (fallbackHandle == IntPtr.Zero)
                    {
                        fallbackHandle = hWnd;
                    }

                    // Check if this is a visible window with a title
                    if (IsWindowVisible(hWnd))
                    {
                        int length = GetWindowTextLength(hWnd);
                        if (length > 0)
                        {
                            var sb = new StringBuilder(length + 1);
                            GetWindowText(hWnd, sb, sb.Capacity);
                            string title = sb.ToString();

                            // Prefer windows with "Minecraft" or "Nebula" in the title
                            if (title.Contains("Minecraft", StringComparison.OrdinalIgnoreCase) ||
                                title.Contains("Nebula", StringComparison.OrdinalIgnoreCase))
                            {
                                foundHandle = hWnd;
                                return false; // Stop searching
                            }

                            // If we haven't found a better match, use any visible window with a title
                            if (foundHandle == IntPtr.Zero)
                            {
                                foundHandle = hWnd;
                            }
                        }
                    }
                }
                return true;
            }, IntPtr.Zero);

            // Use the best match we found, or fallback to the first window
            hwnd = foundHandle != IntPtr.Zero ? foundHandle : fallbackHandle;
            return hwnd != IntPtr.Zero;
        }

        public bool SendKey(IntPtr hwnd, string key)
        {
            if (hwnd == IntPtr.Zero)
                return false;

            var vkCode = GetVirtualKeyCode(key);
            if (vkCode == 0)
                return false;

            PostMessage(hwnd, WM_KEYDOWN, (IntPtr)vkCode, IntPtr.Zero);
            System.Threading.Thread.Sleep(50);
            PostMessage(hwnd, WM_KEYUP, (IntPtr)vkCode, IntPtr.Zero);

            return true;
        }

        private int GetVirtualKeyCode(string key)
        {
            if (string.IsNullOrEmpty(key))
                return 0;

            key = key.ToUpper();

            if (key.Length == 1 && char.IsLetterOrDigit(key[0]))
            {
                return (int)Enum.Parse(typeof(Keys), key);
            }

            if (Enum.TryParse<Keys>(key, true, out var keyCode))
            {
                return (int)keyCode;
            }

            return 0;
        }
    }
}
