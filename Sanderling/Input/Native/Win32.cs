using System;
using System.Runtime.InteropServices;

namespace Sanderling.Input.Native
{
    /// <summary>
    /// Win32 APIs used by Sanderling.
    /// All hooks are thread-level hooks (Rhino main UI thread only), not global keyboard/mouse hooks.
    /// </summary>
    internal static class Win32
    {
        // ---- Hooks ----
        public const int WH_KEYBOARD = 2;
        public const int WH_GETMESSAGE = 3;
        public const int WH_MOUSE = 7;
        public const int HC_ACTION = 0;
        public const int HC_NOREMOVE = 3;
        public const int PM_REMOVE = 0x0001;
        public const int WM_NULL = 0x0000;

        // ---- Mouse messages (client area) ----
        public const int WM_MOUSEFIRST = 0x0200;
        public const int WM_MOUSEWHEEL = 0x020A;
        public const int WM_MOUSEHWHEEL = 0x020E;
        public const int WM_MOUSELAST = 0x020E;

        // Client-area button messages
        public const int WM_LBUTTONDOWN = 0x0201;
        public const int WM_LBUTTONUP = 0x0202;
        public const int WM_LBUTTONDBLCLK = 0x0203;
        public const int WM_RBUTTONDOWN = 0x0204;
        public const int WM_RBUTTONUP = 0x0205;
        public const int WM_RBUTTONDBLCLK = 0x0206;
        public const int WM_MBUTTONDOWN = 0x0207;
        public const int WM_MBUTTONUP = 0x0208;
        public const int WM_MBUTTONDBLCLK = 0x0209;
        public const int WM_XBUTTONDOWN = 0x020B;
        public const int WM_XBUTTONUP = 0x020C;
        public const int WM_XBUTTONDBLCLK = 0x020D;

        // Non-client button messages (title bar, menu bar, window borders)
        public const int WM_NCLBUTTONDOWN = 0x00A1;
        public const int WM_NCLBUTTONUP = 0x00A2;
        public const int WM_NCLBUTTONDBLCLK = 0x00A3;
        public const int WM_NCRBUTTONDOWN = 0x00A4;
        public const int WM_NCRBUTTONUP = 0x00A5;
        public const int WM_NCRBUTTONDBLCLK = 0x00A6;
        public const int WM_NCMBUTTONDOWN = 0x00A7;
        public const int WM_NCMBUTTONUP = 0x00A8;
        public const int WM_NCMBUTTONDBLCLK = 0x00A9;
        public const int WM_NCXBUTTONDOWN = 0x00AB;
        public const int WM_NCXBUTTONUP = 0x00AC;
        public const int WM_NCXBUTTONDBLCLK = 0x00AD;

        public const int XBUTTON1 = 0x0001;
        public const int XBUTTON2 = 0x0002;
        public const int WHEEL_DELTA = 120;

        // ---- Virtual key codes ----
        public const int VK_LBUTTON = 0x01;
        public const int VK_RBUTTON = 0x02;
        public const int VK_TAB = 0x09;
        public const int VK_SHIFT = 0x10;
        public const int VK_CONTROL = 0x11;
        public const int VK_MENU = 0x12; // Alt
        public const int VK_ESCAPE = 0x1B;
        public const int VK_A = 0x41;
        public const int VK_C = 0x43;
        public const int VK_D = 0x44;
        public const int VK_E = 0x45;
        public const int VK_F = 0x46;
        public const int VK_H = 0x48;
        public const int VK_Q = 0x51;
        public const int VK_S = 0x53;
        public const int VK_V = 0x56;
        public const int VK_W = 0x57;

        public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;

            public int Width { get { return Right - Left; } }
            public int Height { get { return Bottom - Top; } }
            public bool IsEmpty { get { return Width <= 0 || Height <= 0; } }
            public int CenterX { get { return Left + Width / 2; } }
            public int CenterY { get { return Top + Height / 2; } }

            public bool Contains(int x, int y)
            {
                return x >= Left && x < Right && y >= Top && y < Bottom;
            }
        }

        /// <summary>lParam of a WH_MOUSE hook (MOUSEHOOKSTRUCT + mouseData).</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEHOOKSTRUCTEX
        {
            public POINT pt;
            public IntPtr hwnd;
            public uint wHitTestCode;
            public UIntPtr dwExtraInfo;
            public uint mouseData;
        }

        /// <summary>Message structure passed to a WH_GETMESSAGE hook.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public POINT pt;
            public uint lPrivate;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        public static extern int ShowCursor([MarshalAs(UnmanagedType.Bool)] bool bShow);

        [DllImport("user32.dll", EntryPoint = "ClipCursor")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ClipCursor(ref RECT lpRect);

        /// <summary>Pass IntPtr.Zero to release the cursor clip.</summary>
        [DllImport("user32.dll", EntryPoint = "ClipCursor")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ClipCursorRelease(IntPtr lpRect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        public const uint GA_ROOT = 2;

        /// <summary>GA_ROOT returns the top-level window that contains hwnd.</summary>
        [DllImport("user32.dll")]
        public static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsChild(IntPtr hWndParent, IntPtr hWnd);

        /// <summary>A window is disabled while a modal dialog it owns is open.</summary>
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowEnabled(IntPtr hWnd);

        // ---- Synthetic input (used to replay a click outside the viewport after navigation has ended) ----
        public const uint INPUT_MOUSE = 0;
        public const uint MOUSEEVENTF_MOVE = 0x0001;
        public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        public const uint MOUSEEVENTF_LEFTUP = 0x0004;
        public const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        public const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        public const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        public const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
        public const uint MOUSEEVENTF_XDOWN = 0x0080;
        public const uint MOUSEEVENTF_XUP = 0x0100;
        public const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;
        public const uint MOUSEEVENTF_ABSOLUTE = 0x8000;

        public const int SM_XVIRTUALSCREEN = 76;
        public const int SM_YVIRTUALSCREEN = 77;
        public const int SM_CXVIRTUALSCREEN = 78;
        public const int SM_CYVIRTUALSCREEN = 79;

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int nIndex);

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        /// <summary>INPUT with the mouse member only (MOUSEINPUT is the largest member of the union).</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT
        {
            public uint type;
            public MOUSEINPUT mi;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        public static bool IsKeyDown(int vk)
        {
            return (GetAsyncKeyState(vk) & 0x8000) != 0;
        }

        /// <summary>Gets a window's client rectangle in screen coordinates.</summary>
        public static bool TryGetClientScreenRect(IntPtr hwnd, out RECT rect)
        {
            rect = new RECT();
            if (hwnd == IntPtr.Zero) return false;

            RECT client;
            if (!GetClientRect(hwnd, out client)) return false;

            POINT origin = new POINT { X = 0, Y = 0 };
            if (!ClientToScreen(hwnd, ref origin)) return false;

            rect.Left = origin.X;
            rect.Top = origin.Y;
            rect.Right = origin.X + client.Width;
            rect.Bottom = origin.Y + client.Height;
            return !rect.IsEmpty;
        }
    }
}
