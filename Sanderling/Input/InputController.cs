using System;
using System.Diagnostics;
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
        private readonly uint _processId;
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
        private bool _disposed;

        public InputController(IntPtr viewHandle)
        {
            _viewHandle = viewHandle;
            _processId = (uint)Process.GetCurrentProcess().Id;
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

        public InputFrame Update(LookMode lookMode)
        {
            InputFrame frame = new InputFrame();

            RefreshViewRect();
            bool focus = RhinoHasFocus() && _viewRectValid;
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

        private bool RhinoHasFocus()
        {
            IntPtr fg = Win32.GetForegroundWindow();
            if (fg == IntPtr.Zero) return false;
            uint pid;
            Win32.GetWindowThreadProcessId(fg, out pid);
            return pid == _processId;
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
                if ((nCode == Win32.HC_ACTION || nCode == Win32.HC_NOREMOVE) && _viewRectValid)
                {
                    int msg = wParam.ToInt32();

                    // Wheel messages must NOT be swallowed here: if this hook discards them,
                    // the WH_GETMESSAGE hook never sees them and the speed cannot change.
                    // They are read and neutralized in GetMessageHookProc instead.
                    bool isWheel = msg == Win32.WM_MOUSEWHEEL || msg == Win32.WM_MOUSEHWHEEL;

                    if (!isWheel && msg >= Win32.WM_MOUSEFIRST && msg <= Win32.WM_MOUSELAST)
                    {
                        Win32.MOUSEHOOKSTRUCTEX data =
                            (Win32.MOUSEHOOKSTRUCTEX)Marshal.PtrToStructure(lParam, typeof(Win32.MOUSEHOOKSTRUCTEX));

                        if (_viewRect.Contains(data.pt.X, data.pt.Y))
                        {
                            // Swallow mouse button / move messages inside the viewport:
                            // RMB no longer rotates, LMB no longer selects objects
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

        /// <summary>
        /// Reads mouse wheel input. The wheel delta is taken from MSG.wParam (HIWORD), which is always
        /// populated, and the message is then turned into WM_NULL so Rhino does not zoom the view.
        /// The wheel adjusts speed (or lens length with Alt held) wherever the cursor is inside Rhino while navigation is active.
        /// </summary>
        private IntPtr GetMessageHookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode == Win32.HC_ACTION && lParam != IntPtr.Zero)
                {
                    Win32.MSG m = (Win32.MSG)Marshal.PtrToStructure(lParam, typeof(Win32.MSG));

                    if (m.message == Win32.WM_MOUSEWHEEL || m.message == Win32.WM_MOUSEHWHEEL)
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

            RemoveHooks();
            _mouse.Release();
            _keyboard.Clear();
        }
    }
}
