using Sanderling.Input.Native;

namespace Sanderling.Input
{
    /// <summary>
    /// Keyboard state, polled once per frame.
    ///
    /// Uses GetAsyncKeyState to read the physical key state instead of relying on KeyDown/KeyUp messages:
    /// KeyUp messages can be lost on focus changes (Alt+Tab, etc.), which would cause "stuck keys" and endless movement.
    /// The keyboard messages themselves are swallowed by InputController's thread hook so no text reaches the Rhino command line.
    /// </summary>
    internal sealed class KeyboardState
    {
        // Start true: a key must be released and pressed again to count as a toggle
        private bool _tabWasDown = true;
        private bool _hWasDown = true;
        private bool _fWasDown = true;
        private bool _cWasDown = true;

        public bool Forward { get; private set; }
        public bool Backward { get; private set; }
        public bool Left { get; private set; }
        public bool Right { get; private set; }
        public bool Up { get; private set; }
        public bool Down { get; private set; }
        public bool Boost { get; private set; }
        public bool Precision { get; private set; }
        public bool Escape { get; private set; }

        /// <summary>Tab was just pressed this frame (edge-triggered).</summary>
        public bool ToggleLookModePressed { get; private set; }

        /// <summary>H was just pressed this frame (edge-triggered).</summary>
        public bool ToggleHudPressed { get; private set; }

        /// <summary>F was just pressed this frame (edge-triggered).</summary>
        public bool ToggleModePressed { get; private set; }

        /// <summary>C was just pressed this frame (edge-triggered).</summary>
        public bool ToggleCollisionPressed { get; private set; }

        public int ForwardAxis { get { return (Forward ? 1 : 0) - (Backward ? 1 : 0); } }
        public int StrafeAxis { get { return (Right ? 1 : 0) - (Left ? 1 : 0); } }
        public int VerticalAxis { get { return (Up ? 1 : 0) - (Down ? 1 : 0); } }

        public void Update(bool hasFocus)
        {
            if (!hasFocus)
            {
                Clear();
                return;
            }

            Forward = Win32.IsKeyDown(Win32.VK_W);
            Backward = Win32.IsKeyDown(Win32.VK_S);
            Left = Win32.IsKeyDown(Win32.VK_A);
            Right = Win32.IsKeyDown(Win32.VK_D);
            Up = Win32.IsKeyDown(Win32.VK_Q);
            Down = Win32.IsKeyDown(Win32.VK_E);
            Boost = Win32.IsKeyDown(Win32.VK_SHIFT);
            Precision = Win32.IsKeyDown(Win32.VK_CONTROL);
            Escape = Win32.IsKeyDown(Win32.VK_ESCAPE);

            bool tabDown = Win32.IsKeyDown(Win32.VK_TAB);
            ToggleLookModePressed = tabDown && !_tabWasDown;
            _tabWasDown = tabDown;

            bool hDown = Win32.IsKeyDown(Win32.VK_H);
            ToggleHudPressed = hDown && !_hWasDown;
            _hWasDown = hDown;

            bool fDown = Win32.IsKeyDown(Win32.VK_F);
            ToggleModePressed = fDown && !_fWasDown;
            _fWasDown = fDown;

            bool cDown = Win32.IsKeyDown(Win32.VK_C);
            ToggleCollisionPressed = cDown && !_cWasDown;
            _cWasDown = cDown;
        }

        public void Clear()
        {
            Forward = Backward = Left = Right = Up = Down = false;
            Boost = Precision = Escape = false;
            ToggleLookModePressed = false;
            ToggleHudPressed = false;
            ToggleModePressed = false;
            ToggleCollisionPressed = false;
            // e.g. Tab may still be held when Alt+Tab-ing back into Rhino; that should not trigger a toggle
            _tabWasDown = true;
            _hWasDown = true;
            _fWasDown = true;
            _cWasDown = true;
        }
    }
}
