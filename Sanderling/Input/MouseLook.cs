using Sanderling.Input.Native;

namespace Sanderling.Input
{
    /// <summary>
    /// Mouse look: captures the cursor, reads relative motion, and re-centers the cursor.
    ///
    /// How it works (same as most FPS games):
    ///   1. Hide the cursor and confine it (ClipCursor) to the viewport rectangle;
    ///   2. Each frame, read the cursor offset from the viewport center = mouse motion for this frame;
    ///   3. Move the cursor back to the viewport center.
    /// This allows unlimited rotation without being stopped by the screen edges.
    /// </summary>
    internal sealed class MouseLook
    {
        private bool _captured;
        private bool _cursorHidden;
        private int _hideCalls;
        private bool _hasRestorePos;
        private Win32.POINT _restorePos;

        public bool IsCaptured { get { return _captured; } }

        /// <param name="viewRect">Viewport client area (screen coordinates)</param>
        /// <param name="rememberPosition">Whether to restore the cursor to its original position on release</param>
        public void Capture(Win32.RECT viewRect, bool rememberPosition)
        {
            if (_captured || viewRect.IsEmpty) return;

            _hasRestorePos = false;
            Win32.POINT p;
            if (rememberPosition && Win32.GetCursorPos(out p))
            {
                _restorePos = p;
                _hasRestorePos = true;
            }

            Win32.RECT clip = viewRect;
            Win32.ClipCursor(ref clip);
            Win32.SetCursorPos(viewRect.CenterX, viewRect.CenterY);
            HideCursor();
            _captured = true;
        }

        /// <param name="restorePosition">
        /// Move the cursor back to where it was when captured. Pass false when another window (e.g. a dialog)
        /// takes over, so the cursor does not jump away while the user is about to click in that window.
        /// </param>
        public void Release(bool restorePosition = true)
        {
            if (!_captured) return;

            Win32.ClipCursorRelease(System.IntPtr.Zero);
            if (_hasRestorePos && restorePosition)
            {
                Win32.SetCursorPos(_restorePos.X, _restorePos.Y);
            }
            ShowCursorAgain();
            _captured = false;
            _hasRestorePos = false;
        }

        /// <summary>Reads this frame's mouse delta (pixels) and moves the cursor back to the center.</summary>
        public void ReadDelta(Win32.RECT viewRect, out int dx, out int dy)
        {
            dx = 0;
            dy = 0;
            if (!_captured || viewRect.IsEmpty) return;

            // The viewport may be moved/resized during navigation, so re-apply the clip every frame
            Win32.RECT clip = viewRect;
            Win32.ClipCursor(ref clip);

            Win32.POINT p;
            if (!Win32.GetCursorPos(out p)) return;

            int cx = viewRect.CenterX;
            int cy = viewRect.CenterY;
            dx = p.X - cx;
            dy = p.Y - cy;

            if (dx != 0 || dy != 0)
            {
                Win32.SetCursorPos(cx, cy);
            }
        }

        private void HideCursor()
        {
            if (_cursorHidden) return;

            // ShowCursor maintains a display counter; the cursor is hidden when the counter is < 0
            _hideCalls = 0;
            int count;
            do
            {
                count = Win32.ShowCursor(false);
                _hideCalls++;
            }
            while (count >= 0 && _hideCalls < 16);

            _cursorHidden = true;
        }

        private void ShowCursorAgain()
        {
            if (!_cursorHidden) return;

            for (int i = 0; i < _hideCalls; i++)
            {
                Win32.ShowCursor(true);
            }
            _hideCalls = 0;
            _cursorHidden = false;
        }
    }
}
