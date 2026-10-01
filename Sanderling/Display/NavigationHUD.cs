using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using Sanderling.Navigation;
using Sanderling.Settings;
using Rhino.Display;
using Rhino.Geometry;

namespace Sanderling.Display
{
    /// <summary>
    /// Viewport HUD shown during navigation: an info panel in the top-left corner.
    /// Only drawn in the viewport currently controlled by Sanderling, and only while HudVisible is true (H toggles it).
    ///
    /// Layout is measured every frame: all text items are laid out first, their real screen rectangles are
    /// measured with Measure2dText, and the panel is the union of those rectangles grown by the same padding
    /// on all four sides. This keeps the top / bottom / left / right padding identical regardless of font,
    /// DPI or text length.
    /// </summary>
    internal sealed class NavigationHUD : DisplayConduit
    {
        private static readonly Color Accent = Color.FromArgb(255, 232, 142, 48);
        private static readonly Color PanelFill = Color.FromArgb(175, 16, 18, 22);
        private static readonly Color TextMain = Color.FromArgb(255, 236, 236, 236);
        private static readonly Color TextDim = Color.FromArgb(255, 160, 165, 172);
        private static readonly Color TextDisabled = Color.FromArgb(255, 105, 110, 118);
        private static readonly Color Warning = Color.FromArgb(255, 255, 200, 70);

        private const string FontFace = "Consolas";
        private const int TitleSize = 18;
        private const int BodySize = 13;

        /// <summary>Inner padding between the text and the panel edge, identical on all four sides.</summary>
        private const int Padding = 14;
        /// <summary>Distance from the viewport's left edge to the panel.</summary>
        private const int MarginLeft = 14;
        /// <summary>Distance from the viewport's top edge to the panel (leaves room for the viewport title).</summary>
        private const int MarginTop = 34;

        private const int LineHeight = 18;
        private const int TitleGap = 24;
        private const int SectionGap = 8;
        private const int KeyColumn = 96;

        private static readonly string[,] KeyHints =
        {
            { "WASD",      "Move" },
            { "Q / E",     "Up / Down" },
            { "Shift",     "Boost" },
            { "Ctrl",      "Precision" },
            { "Wheel",     "Speed" },
            { "Alt+Wheel", "Lens" },
            { "Mouse",     "Look" },
            { "F",         "Fly / Walk" },
            { "C",         "Collision" },
            { "Tab",       "Look mode" },
            { "H",         "Hide HUD" },
            { "Esc",       "Exit" }
        };

        private struct TextItem
        {
            public string Text;
            public Color Color;
            public int Size;
            public int Left;
            public int Top;
        }

        private readonly NavigationState _state;
        private readonly NavigationSettings _settings;
        private readonly Guid _viewportId;
        private readonly List<TextItem> _items = new List<TextItem>();

        // The anchor point of Draw2dText may be top-left or bottom-left depending on the Rhino version.
        // We measure the text bounding box once per font size to compute an offset so text always lands where expected.
        private readonly Dictionary<int, int> _textTopOffset = new Dictionary<int, int>();

        public NavigationHUD(NavigationState state, NavigationSettings settings, Guid viewportId)
        {
            _state = state;
            _settings = settings;
            _viewportId = viewportId;
        }

        protected override void DrawForeground(DrawEventArgs e)
        {
            if (!_state.HudVisible) return;
            if (e.Viewport == null || e.Viewport.Id != _viewportId) return;

            DisplayPipeline d = e.Display;
            Rectangle b = e.Viewport.Bounds;

            // ---- 1. Lay out all text relative to the content origin ----
            int originX = b.Left + MarginLeft + Padding;
            int originY = b.Top + MarginTop + Padding;
            BuildLayout(originX, originY);

            // ---- 2. Measure the text rectangles (falls back to an estimate if measuring fails) ----
            Rectangle content = Rectangle.Empty;
            for (int i = 0; i < _items.Count; i++)
            {
                Rectangle r = MeasureItem(d, _items[i]);
                content = i == 0 ? r : Rectangle.Union(content, r);
            }
            if (_items.Count == 0) return;

            // ---- 3. Panel = content grown by the same padding on every side ----
            Rectangle panel = Rectangle.Inflate(content, Padding, Padding);
            d.Draw2dRectangle(panel, PanelFill, 1, PanelFill); // no visible outline

            // ---- 4. Draw the text ----
            foreach (TextItem item in _items)
            {
                d.Draw2dText(item.Text, item.Color, AnchorFor(d, item), false, item.Size, FontFace);
            }
        }

        private void BuildLayout(int x, int y)
        {
            _items.Clear();

            Add("SANDERLING", TextMain, TitleSize, x, y);
            y += TitleGap;
            Add(_state.Mode == NavigationMode.Walk ? "WALK MODE" : "FLY MODE", TextMain, BodySize, x, y);
            y += LineHeight + SectionGap;

            Add("Speed: " + FormatSpeed(_state.BaseSpeed) + " m/s", TextMain, BodySize, x, y);
            y += LineHeight;
            Add("Lens:  " + _state.LensLength.ToString("0.#", CultureInfo.InvariantCulture) + " mm", TextMain, BodySize, x, y);
            y += LineHeight;

            if (_state.Mode == NavigationMode.Walk)
            {
                Add("Eye:   " + _settings.EyeHeight.ToString("0.00", CultureInfo.InvariantCulture) + " m", TextMain, BodySize, x, y);
                y += LineHeight;
                Add("Collision: " + (_state.CollisionEnabled ? "On" : "Off"),
                    _state.CollisionEnabled ? TextMain : Warning, BodySize, x, y);
                y += LineHeight;
            }

            // The modifier line always reserves its space so the panel does not jump when Shift / Ctrl is pressed
            if (_state.Precision)
            {
                Add(string.Format(CultureInfo.InvariantCulture, "PRECISION x{0}  -> {1} m/s",
                    _settings.PrecisionMultiplier, FormatSpeed(_state.EffectiveSpeed(_settings))), Accent, BodySize, x, y);
            }
            else if (_state.Boost)
            {
                Add(string.Format(CultureInfo.InvariantCulture, "BOOST x{0}  -> {1} m/s",
                    _settings.BoostMultiplier, FormatSpeed(_state.EffectiveSpeed(_settings))), Accent, BodySize, x, y);
            }
            y += LineHeight + SectionGap;

            int keyCount = KeyHints.GetLength(0);
            bool walk = _state.Mode == NavigationMode.Walk;
            for (int i = 0; i < keyCount; i++)
            {
                string key = KeyHints[i, 0];
                string action = KeyHints[i, 1];

                if (walk && key == "Q / E")
                {
                    // Height follows the ground in Walk mode
                    Add(key, TextDisabled, BodySize, x, y);
                    Add("Fly only", TextDisabled, BodySize, x + KeyColumn, y);
                }
                else if (!walk && key == "C")
                {
                    // Collision only exists in Walk mode
                    Add(key, TextDisabled, BodySize, x, y);
                    Add("Walk only", TextDisabled, BodySize, x + KeyColumn, y);
                }
                else
                {
                    Add(key, TextMain, BodySize, x, y);
                    Add(action, TextDim, BodySize, x + KeyColumn, y);
                }
                y += LineHeight;
            }
            y += SectionGap;

            string look = _state.LookMode == LookMode.Free ? "Free" : "Drag (hold LMB/RMB)";
            Add("Look: " + look, TextDim, BodySize, x, y);
            y += LineHeight;

            string fps = _state.Fps > 0.0 ? _state.Fps.ToString("0", CultureInfo.InvariantCulture) : "--";
            Add("FPS: " + fps + "   Units: " + _state.UnitSystemName, TextDim, BodySize, x, y);
            y += LineHeight;

            if (walk && _state.ShowStats)
            {
                Add(string.Format(CultureInfo.InvariantCulture, "Physics: {0:0.00} ms   Tris: {1}",
                    _state.PhysicsMilliseconds, _state.PhysicsTriangles), TextDim, BodySize, x, y);
                y += LineHeight;
            }

            if (walk && !_state.HasGround)
            {
                Add("NO GROUND - walking at current height", Warning, BodySize, x, y);
                y += LineHeight;
            }

            if (_state.IsPaused)
            {
                Add("PAUSED - click Rhino to resume", Warning, BodySize, x, y);
            }
        }

        private void Add(string text, Color color, int size, int left, int top)
        {
            if (string.IsNullOrEmpty(text)) return;
            TextItem item = new TextItem();
            item.Text = text;
            item.Color = color;
            item.Size = size;
            item.Left = left;
            item.Top = top;
            _items.Add(item);
        }

        /// <summary>
        /// Screen rectangle of a text item. Uses Measure2dText when it returns a plausible result;
        /// otherwise estimates the size from the font metrics so the panel is always drawn.
        /// </summary>
        private Rectangle MeasureItem(DisplayPipeline d, TextItem item)
        {
            Rectangle r = Rectangle.Empty;
            try
            {
                r = d.Measure2dText(item.Text, AnchorFor(d, item), false, 0.0, item.Size, FontFace);
            }
            catch
            {
                r = Rectangle.Empty;
            }

            if (IsPlausible(r, item.Left, item.Top, item.Size)) return r;

            // Fallback: Consolas advance width is about 0.55 em, line box about 1.2 em
            int width = (int)Math.Ceiling(item.Text.Length * item.Size * 0.55);
            int height = (int)Math.Ceiling(item.Size * 1.2);
            return new Rectangle(item.Left, item.Top, width, height);
        }

        /// <summary>
        /// A measured rectangle is only trusted if it is non-empty and located near where the text is placed
        /// (guards against empty results or rectangles returned in a different coordinate space).
        /// </summary>
        private static bool IsPlausible(Rectangle r, int left, int top, int size)
        {
            if (r.Width <= 0 || r.Height <= 0) return false;
            if (r.Height > size * 4) return false;
            if (Math.Abs(r.Left - left) > size * 2) return false;
            if (Math.Abs(r.Top - top) > size * 2) return false;
            return true;
        }

        /// <summary>Draw2dText anchor that places the top of the text box at item.Top.</summary>
        private Point2d AnchorFor(DisplayPipeline d, TextItem item)
        {
            int offset;
            if (!_textTopOffset.TryGetValue(item.Size, out offset))
            {
                offset = 0; // assume a top-left anchor if the measurement is unusable
                try
                {
                    Rectangle r = d.Measure2dText("Ag", new Point2d(0, 0), false, 0.0, item.Size, FontFace);
                    if (IsPlausible(r, 0, 0, item.Size))
                    {
                        offset = -r.Top;
                        _textTopOffset[item.Size] = offset; // only cache a valid measurement
                    }
                }
                catch
                {
                    offset = 0;
                }
            }
            return new Point2d(item.Left, item.Top + offset);
        }

        private static string FormatSpeed(double v)
        {
            return v.ToString(v < 1.0 ? "0.00" : "0.0", CultureInfo.InvariantCulture);
        }
    }
}
