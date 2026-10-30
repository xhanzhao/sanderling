using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Sanderling.Input.Native;
using Sanderling.Navigation;

namespace Sanderling.Input
{
    /// <summary>
    /// Input snapshot for a single frame. NavigationController depends only on this struct
    /// and never touches Win32 directly - gamepad / SpaceMouse support only needs to produce the same InputFrame.
    /// </summary>
    internal struct InputFrame
    {
        public bool HasFocus;
        public bool ExitRequested;

        /// <summary>+1 forward (W) / -1 backward (S)</summary>
        public int Forward;
        /// <summary>+1 right (D) / -1 left (A)</summary>
        public int Strafe;
        /// <summary>+1 up (Q) / -1 down (E)</summary>
        public int Vertical;

        public bool Boost;
        public bool Precision;

        /// <summary>Mouse delta in pixels, screen coordinates: right is +X, down is +Y</summary>
        public double LookDeltaX;
        public double LookDeltaY;

        /// <summary>Mouse wheel notches without Alt (positive = up): adjusts speed</summary>
        public int SpeedSteps;

        /// <summary>Mouse wheel notches with Alt held (positive = up): adjusts lens length</summary>
        public int LensSteps;

        public bool ToggleLookMode;
        public bool ToggleHud;
        public bool ToggleMode;
        public bool ToggleCollision;
        public bool SaveView;

        /// <summary>The user clicked Rhino's UI outside the viewport (toolbar, panel, menu, command line): end navigation.</summary>
        public bool ClickedOutside;
    }

    /// <summary>
    /// Input controller:
    ///  - Installs keyboard/mouse hooks on the Rhino UI thread and swallows key presses and in-viewport
    ///    mouse messages while navigating (prevents WASD typing into the command line, wheel zoom, RMB rotate, etc.);
    ///  - Polls keyboard state, reads mouse deltas, accumulates wheel input;
    ///  - Produces one InputFrame per frame.
    /// </summary>
    internal sealed class InputController : IDisposable
    {
        private readonly IntPtr _viewHandle;
        private IntPtr _rootHandle; // top-level window containing the navigation view (main window, or a floating view frame)
        private readonly KeyboardState _keyboard = new KeyboardState();
        private readonly MouseLook _mouse = new MouseLook();

        // Delegates must be stored in fields, otherwise the GC may collect them and crash the hook callbacks
        private Win32.HookProc _keyboardProc;
        private Win32.HookProc _mouseProc;
        private Win32.HookProc _messageProc;
        private IntPtr _keyboardHook = IntPtr.Zero;
        private IntPtr _mouseHook = IntPtr.Zero;
        private IntPtr _messageHook = IntPtr.Zero;

        private Win32.RECT _viewRect;
        private bool _viewRectValid;
        private int _wheelAccumulator;      // wheel without Alt -> speed
        private int _lensWheelAccumulator;  // Alt + wheel -> lens
        private bool _escapeFromHook;

        // Click outside the viewport -> end navigation, then replay the click so it works as usual.
        // The click is held back completely while navigation is still running: menus, title-bar drags and
        // panel actions would otherwise start inside the running Sanderling command (menus even block the
        // navigation loop until they close).
        private bool _clickedOutside;
        private int _clickButton;           // 0 none, 1 left, 2 right, 3 middle, 4 X1, 5 X2
        private Win32.POINT _clickPoint;    // screen position of the click
        private bool _clickReleased;        // the button was released before navigation had ended
        private bool _stopIntercepting;     // set on Dispose so a late hook call never swallows anything
        private bool _disposed;

        public InputController(IntPtr viewHandle)
        {
            _viewHandle = viewHandle;
            _rootHandle = Win32.GetAncestor(viewHandle, Win32.GA_ROOT);
        }

        /// <summary>Installs the hooks. Must be called on the Rhino main UI thread (i.e. inside RunCommand).</summary>
        public bool Start()
        {
            uint threadId = Win32.GetCurrentThreadId();

            _keyboardProc = KeyboardHookProc;
            _mouseProc = MouseHookProc;
            _messageProc = GetMessageHookProc;

            _keyboardHook = Win32.SetWindowsHookEx(Win32.WH_KEYBOARD, _keyboardProc, IntPtr.Zero, threadId);
            _mouseHook = Win32.SetWindowsHookEx(Win32.WH_MOUSE, _mouseProc, IntPtr.Zero, threadId);
            _messageHook = Win32.SetWindowsHookEx(Win32.WH_GETMESSAGE, _messageProc, IntPtr.Zero, threadId);

            RefreshViewRect();

            if (_keyboardHook == IntPtr.Zero || _mouseHook == IntPtr.Zero || _messageHook == IntPtr.Zero)
            {
                RemoveHooks();
                return false;
            }
            return true;
        }

        /// <summary>Esc was pressed in the navigation window (seen by the keyboard hook) since navigation started.</summary>
        public bool EscapePressed { get { return _escapeFromHook; } }

        /// <summary>A click outside the viewport is being held back and will end navigation.</summary>
        public bool OutsideClickPending { get { return _clickedOutside; } }

        public InputFrame Update(LookMode lookMode)
        {
            InputFrame frame = new InputFrame();

            RefreshViewRect();
            bool focus = IsNavigationForeground && _viewRectValid;
            frame.HasFocus = focus;

            _keyboard.Update(focus);

            frame.ExitRequested = _escapeFromHook || _keyboard.Escape;
            frame.Forward = _keyboard.ForwardAxis;
            frame.Strafe = _keyboard.StrafeAxis;
            frame.Vertical = _keyboard.VerticalAxis;
            frame.Boost = _keyboard.Boost;
            frame.Precision = _keyboard.Precision;
            frame.ToggleLookMode = _keyboard.ToggleLookModePressed;
            frame.ToggleHud = _keyboard.ToggleHudPressed;
            frame.ToggleMode = _keyboard.ToggleModePressed;
            frame.ToggleCollision = _keyboard.ToggleCollisionPressed;
            frame.SaveView = _keyboard.SaveViewPressed;
            frame.ClickedOutside = _clickedOutside;

            // Wheel: 120 units per notch; the fractional remainder of high-resolution wheels carries over to the next frame
            if (focus)
            {
                int steps = _wheelAccumulator / Win32.WHEEL_DELTA;
                _wheelAccumulator -= steps * Win32.WHEEL_DELTA;
                frame.SpeedSteps = steps;

                int lensSteps = _lensWheelAccumulator / Win32.WHEEL_DELTA;
                _lensWheelAccumulator -= lensSteps * Win32.WHEEL_DELTA;
                frame.LensSteps = lensSteps;
            }
            else
            {
                _wheelAccumulator = 0;
                _lensWheelAccumulator = 0;
            }

            UpdateMouseCapture(lookMode, focus);
            if (_mouse.IsCaptured)
            {
                int dx, dy;
                _mouse.ReadDelta(_viewRect, out dx, out dy);
                frame.LookDeltaX = dx;
                frame.LookDeltaY = dy;
            }

            return frame;
        }

        /// <summary>
        /// Free mode: the mouse is always captured while navigating (game style).
        /// Drag mode: look around only while holding the left or right mouse button (similar to Enscape's default).
        /// </summary>
        private void UpdateMouseCapture(LookMode lookMode, bool focus)
        {
            if (!focus)
            {
                _mouse.Release();
                return;
            }

            if (lookMode == LookMode.Free)
            {
                if (!_mouse.IsCaptured)
                {
                    _mouse.Capture(_viewRect, true);
                }
                return;
            }

            // Drag mode
            bool buttonDown = Win32.IsKeyDown(Win32.VK_LBUTTON) || Win32.IsKeyDown(Win32.VK_RBUTTON);
            if (buttonDown)
            {
                if (!_mouse.IsCaptured && CursorInView())
                {
                    _mouse.Capture(_viewRect, true);
                }
            }
            else if (_mouse.IsCaptured)
            {
                _mouse.Release();
            }
        }

        private bool CursorInView()
        {
            Win32.POINT p;
            return _viewRectValid && Win32.GetCursorPos(out p) && _viewRect.Contains(p.X, p.Y);
        }

        private void RefreshViewRect()
        {
            Win32.RECT r;
            _viewRectValid = Win32.TryGetClientScreenRect(_viewHandle, out r);
            if (_viewRectValid) _viewRect = r;
        }

        /// <summary>
        /// True only while the window that contains the navigation view is the active window.
        ///
        /// Any other window - another application, but also Rhino's own dialogs, floating panels and
        /// message boxes - makes this false. Navigation then pauses and stops intercepting input, so that
        /// window works normally (e.g. Named Views > Save As opened during navigation). It resumes automatically
        /// when the view's window is active again.
        /// </summary>
        public bool IsNavigationForeground
        {
            get
            {
                if (_rootHandle == IntPtr.Zero) _rootHandle = Win32.GetAncestor(_viewHandle, Win32.GA_ROOT);
                IntPtr fg = Win32.GetForegroundWindow();
                return fg != IntPtr.Zero && fg == _rootHandle;
            }
        }

        /// <summary>Whether a window is the navigation view or one of its child windows.</summary>
        private bool IsViewWindow(IntPtr hwnd)
        {
            return hwnd != IntPtr.Zero && (hwnd == _viewHandle || Win32.IsChild(_viewHandle, hwnd));
        }

        /// <summary>
        /// Whether a mouse message is meant for the navigation view: sent to the view (or a child of it), or to a
        /// container window that holds the view while the cursor is over the viewport. Dialogs and floating panels
        /// are never ancestors of the view, so they always keep their mouse input.
        /// </summary>
        private bool IsViewTarget(IntPtr hwnd, int x, int y)
        {
            if (IsViewWindow(hwnd)) return true;
            return hwnd != IntPtr.Zero && Win32.IsChild(hwnd, _viewHandle) && _viewRect.Contains(x, y);
        }

        /// <summary>Whether a window belongs to the navigation view's top-level window (excludes dialogs and floating panels).</summary>
        private bool IsInNavigationWindow(IntPtr hwnd)
        {
            return hwnd != IntPtr.Zero && _rootHandle != IntPtr.Zero
                && (hwnd == _rootHandle || Win32.IsChild(_rootHandle, hwnd));
        }

        /// <summary>
        /// Called from the hooks when another window has become active. Releases a captured cursor immediately,
        /// because the real-time loop may be blocked while a modal dialog is open and cannot do it itself.
        /// </summary>
        private void YieldToOtherWindow()
        {
            if (_mouse.IsCaptured) _mouse.Release(false);
        }

        // ------------------------------------------------------------------
        // Hook callbacks: keep them as simple as possible and never let them throw
        // ------------------------------------------------------------------

        private IntPtr KeyboardHookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode == Win32.HC_ACTION || nCode == Win32.HC_NOREMOVE)
                {
                    // Another Rhino window (dialog, floating panel) is active: let it receive the keys
                    if (!IsNavigationForeground)
                    {
                        YieldToOtherWindow();
                        return Win32.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
                    }

                    int vk = wParam.ToInt32();
                    bool keyUp = ((lParam.ToInt64() >> 31) & 1) != 0;

                    if (vk == Win32.VK_ESCAPE && !keyUp && nCode == Win32.HC_ACTION)
                    {
                        _escapeFromHook = true;
                    }

                    // Swallow all keys while navigating: prevents input reaching the command line or triggering Rhino shortcuts
                    // (system combinations such as Alt+Tab and Ctrl+Alt+Del are not affected by thread hooks)
                    return new IntPtr(1);
                }
            }
            catch
            {
                // Ignore and pass on to the next hook
            }
            return Win32.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
        }

        private IntPtr MouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if ((nCode == Win32.HC_ACTION || nCode == Win32.HC_NOREMOVE) && _viewRectValid && !_stopIntercepting)
                {
                    int msg = wParam.ToInt32();

                    // Wheel messages must NOT be swallowed here: if this hook discards them,
                    // the WH_GETMESSAGE hook never sees them and the speed cannot change.
                    // They are read and neutralized in GetMessageHookProc instead.
                    bool isWheel = msg == Win32.WM_MOUSEWHEEL || msg == Win32.WM_MOUSEHWHEEL;
                    bool isClient = msg >= Win32.WM_MOUSEFIRST && msg <= Win32.WM_MOUSELAST;
                    bool isNonClient = msg >= Win32.WM_NCLBUTTONDOWN && msg <= Win32.WM_NCXBUTTONDBLCLK;

                    if (!isWheel && (isClient || isNonClient))
                    {
                        Win32.MOUSEHOOKSTRUCTEX data =
                            (Win32.MOUSEHOOKSTRUCTEX)Marshal.PtrToStructure(lParam, typeof(Win32.MOUSEHOOKSTRUCTEX));

                        bool viewTarget = isClient && IsViewTarget(data.hwnd, data.pt.X, data.pt.Y);
                        int down = ButtonDown(msg, data.mouseData);
                        int up = ButtonUp(msg, data.mouseData);

                        // A click outside the viewport has been held back and navigation is about to end:
                        // hold back all further mouse input until it has; the click is replayed afterwards.
                        if (_clickedOutside)
                        {
                            if (up != 0 && up == _clickButton) _clickReleased = true;
                            return new IntPtr(1);
                        }

                        // ---- Rule: a click on Rhino's UI outside the viewport ends navigation ----
                        // Applies to toolbars, panels, the menu bar, the title bar, the command line and other
                        // viewports alike. Clicks in a modal dialog (main window disabled) never end navigation;
                        // navigation only pauses while such a dialog is open.
                        if (down != 0 && !viewTarget && !IsModalDialogOpen())
                        {
                            _clickedOutside = true;
                            _clickButton = down;
                            _clickPoint = data.pt;
                            _clickReleased = false;
                            return new IntPtr(1);
                        }

                        if (!IsNavigationForeground)
                        {
                            // A dialog or floating panel is active: release the cursor and let the mouse work normally
                            YieldToOtherWindow();
                        }
                        else if (viewTarget)
                        {
                            // Swallow mouse messages that target the navigation view itself:
                            // RMB no longer rotates, LMB no longer selects objects.
                            // Decided by target window, not screen position, so dialogs shown on top of
                            // the viewport still receive their clicks.
                            return new IntPtr(1);
                        }
                    }
                }
            }
            catch
            {
                // Ignore
            }
            return Win32.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        /// <summary>True while a modal dialog is open (it disables the window that owns it).</summary>
        private bool IsModalDialogOpen()
        {
            if (_rootHandle != IntPtr.Zero && !Win32.IsWindowEnabled(_rootHandle)) return true;
            IntPtr main = Rhino.RhinoApp.MainWindowHandle();
            return main != IntPtr.Zero && !Win32.IsWindowEnabled(main);
        }

        /// <summary>Button pressed by a mouse message: 0 none, 1 left, 2 right, 3 middle, 4 X1, 5 X2.</summary>
        private static int ButtonDown(int msg, uint mouseData)
        {
            switch (msg)
            {
                case Win32.WM_LBUTTONDOWN:
                case Win32.WM_LBUTTONDBLCLK:
                case Win32.WM_NCLBUTTONDOWN:
                case Win32.WM_NCLBUTTONDBLCLK:
                    return 1;
                case Win32.WM_RBUTTONDOWN:
                case Win32.WM_RBUTTONDBLCLK:
                case Win32.WM_NCRBUTTONDOWN:
                case Win32.WM_NCRBUTTONDBLCLK:
                    return 2;
                case Win32.WM_MBUTTONDOWN:
                case Win32.WM_MBUTTONDBLCLK:
                case Win32.WM_NCMBUTTONDOWN:
                case Win32.WM_NCMBUTTONDBLCLK:
                    return 3;
                case Win32.WM_XBUTTONDOWN:
                case Win32.WM_XBUTTONDBLCLK:
                case Win32.WM_NCXBUTTONDOWN:
                case Win32.WM_NCXBUTTONDBLCLK:
                    return XButton(mouseData);
                default:
                    return 0;
            }
        }

        /// <summary>Button released by a mouse message: 0 none, 1 left, 2 right, 3 middle, 4 X1, 5 X2.</summary>
        private static int ButtonUp(int msg, uint mouseData)
        {
            switch (msg)
            {
                case Win32.WM_LBUTTONUP:
                case Win32.WM_NCLBUTTONUP:
                    return 1;
                case Win32.WM_RBUTTONUP:
                case Win32.WM_NCRBUTTONUP:
                    return 2;
                case Win32.WM_MBUTTONUP:
                case Win32.WM_NCMBUTTONUP:
                    return 3;
                case Win32.WM_XBUTTONUP:
                case Win32.WM_NCXBUTTONUP:
                    return XButton(mouseData);
                default:
                    return 0;
            }
        }

        private static int XButton(uint mouseData)
        {
            int which = (int)((mouseData >> 16) & 0xFFFF);
            return which == Win32.XBUTTON2 ? 5 : 4;
        }

        /// <summary>
        /// Replays the click outside the viewport that ended navigation: moves the cursor back to where the user
        /// clicked, presses the button and, if it was already released, releases it again.
        /// Call after Dispose (hooks removed). The synthetic input is processed once the Sanderling command has
        /// returned, so the menu bar, toolbar button or panel reacts exactly as to a normal click.
        /// If the user is still holding the button, only the press is replayed and the real release follows.
        /// </summary>
        public void ReplayOutsideClick()
        {
            int button = _clickButton;
            _clickButton = 0;
            if (!_clickedOutside || button == 0) return;

            uint downFlag, upFlag, data = 0;
            switch (button)
            {
                case 1: downFlag = Win32.MOUSEEVENTF_LEFTDOWN; upFlag = Win32.MOUSEEVENTF_LEFTUP; break;
                case 2: downFlag = Win32.MOUSEEVENTF_RIGHTDOWN; upFlag = Win32.MOUSEEVENTF_RIGHTUP; break;
                case 3: downFlag = Win32.MOUSEEVENTF_MIDDLEDOWN; upFlag = Win32.MOUSEEVENTF_MIDDLEUP; break;
                case 4: downFlag = Win32.MOUSEEVENTF_XDOWN; upFlag = Win32.MOUSEEVENTF_XUP; data = Win32.XBUTTON1; break;
                case 5: downFlag = Win32.MOUSEEVENTF_XDOWN; upFlag = Win32.MOUSEEVENTF_XUP; data = Win32.XBUTTON2; break;
                default: return;
            }

            List<Win32.INPUT> inputs = new List<Win32.INPUT>(3);

            // Move back to the click position (absolute coordinates normalized to the virtual desktop)
            int vx = Win32.GetSystemMetrics(Win32.SM_XVIRTUALSCREEN);
            int vy = Win32.GetSystemMetrics(Win32.SM_YVIRTUALSCREEN);
            int vw = Win32.GetSystemMetrics(Win32.SM_CXVIRTUALSCREEN);
            int vh = Win32.GetSystemMetrics(Win32.SM_CYVIRTUALSCREEN);
            if (vw > 1 && vh > 1)
            {
                Win32.INPUT move = new Win32.INPUT();
                move.type = Win32.INPUT_MOUSE;
                move.mi.dx = (int)Math.Round((_clickPoint.X - vx) * 65535.0 / (vw - 1));
                move.mi.dy = (int)Math.Round((_clickPoint.Y - vy) * 65535.0 / (vh - 1));
                move.mi.dwFlags = Win32.MOUSEEVENTF_MOVE | Win32.MOUSEEVENTF_ABSOLUTE | Win32.MOUSEEVENTF_VIRTUALDESK;
                inputs.Add(move);
            }

            Win32.INPUT press = new Win32.INPUT();
            press.type = Win32.INPUT_MOUSE;
            press.mi.dwFlags = downFlag;
            press.mi.mouseData = data;
            inputs.Add(press);

            if (_clickReleased)
            {
                Win32.INPUT release = new Win32.INPUT();
                release.type = Win32.INPUT_MOUSE;
                release.mi.dwFlags = upFlag;
                release.mi.mouseData = data;
                inputs.Add(release);
            }

            try
            {
                Win32.SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf(typeof(Win32.INPUT)));
            }
            catch
            {
                // Worst case the click has to be repeated
            }
        }

        /// <summary>
        /// Reads mouse wheel input. The wheel delta is taken from MSG.wParam (HIWORD), which is always
        /// populated, and the message is then turned into WM_NULL so Rhino does not zoom the view.
        /// The wheel adjusts speed (or lens length with Alt held) anywhere in the navigation view's window
        /// (main window or floating view frame); dialogs and floating panels are not affected.
        /// </summary>
        private IntPtr GetMessageHookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode == Win32.HC_ACTION && lParam != IntPtr.Zero)
                {
                    Win32.MSG m = (Win32.MSG)Marshal.PtrToStructure(lParam, typeof(Win32.MSG));

                    bool isWheel = m.message == Win32.WM_MOUSEWHEEL || m.message == Win32.WM_MOUSEHWHEEL;

                    // Only take wheel input meant for the navigation window; dialogs and floating panels keep their scrolling
                    if (isWheel && IsNavigationForeground && IsInNavigationWindow(m.hwnd))
                    {
                        // Count each message only once, when it is actually removed from the queue
                        if (m.message == Win32.WM_MOUSEWHEEL && (wParam.ToInt64() & Win32.PM_REMOVE) != 0)
                        {
                            short delta = unchecked((short)((m.wParam.ToInt64() >> 16) & 0xFFFF));

                            // The Alt state is checked when the message arrives, so each notch is routed correctly
                            if (Win32.IsKeyDown(Win32.VK_MENU)) _lensWheelAccumulator += delta;
                            else _wheelAccumulator += delta;
                        }

                        m.message = Win32.WM_NULL;
                        Marshal.StructureToPtr(m, lParam, false);
                    }
                }
            }
            catch
            {
                // Ignore
            }
            return Win32.CallNextHookEx(_messageHook, nCode, wParam, lParam);
        }

        private void RemoveHooks()
        {
            if (_keyboardHook != IntPtr.Zero)
            {
                Win32.UnhookWindowsHookEx(_keyboardHook);
                _keyboardHook = IntPtr.Zero;
            }
            if (_mouseHook != IntPtr.Zero)
            {
                Win32.UnhookWindowsHookEx(_mouseHook);
                _mouseHook = IntPtr.Zero;
            }
            if (_messageHook != IntPtr.Zero)
            {
                Win32.UnhookWindowsHookEx(_messageHook);
                _messageHook = IntPtr.Zero;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _stopIntercepting = true;

            RemoveHooks();
            _mouse.Release();
            _keyboard.Clear();
        }
    }
}
