using System;
using Eto.Drawing;
using Eto.Forms;

namespace Sanderling.UI
{
    /// <summary>
    /// Colours and fonts of Sanderling's own windows: black ink on white, matching the toolbar icons.
    /// </summary>
    internal static class InkTheme
    {
        public static readonly Color Ink = Color.FromArgb(0x1d, 0x1f, 0x23);
        public static readonly Color InkHover = Color.FromArgb(0x3a, 0x3d, 0x42);
        public static readonly Color Paper = Colors.White;
        public static readonly Color PaperHover = Color.FromArgb(0xf1, 0xf2, 0xf3);
        public static readonly Color Muted = Color.FromArgb(0x8a, 0x8d, 0x93);
        public static readonly Color Rule = Color.FromArgb(0xe2, 0xe3, 0xe6);
        public static readonly Color Track = Color.FromArgb(0xd4, 0xd6, 0xda);

        public const int RowHeight = 34;

        public static Font Body(float size = 9f, FontStyle style = FontStyle.None)
        {
            return new Font(SystemFonts.Default().Family, size, style);
        }

        /// <summary>Monospaced font for numbers; falls back to the platform's monospace family.</summary>
        public static Font Mono(float size = 9f)
        {
            try
            {
                return new Font("Consolas", size);
            }
            catch (Exception)
            {
                return new Font(FontFamilies.Monospace, size);
            }
        }
    }

    /// <summary>Pixel-snapped frame drawing shared by the ink controls.</summary>
    internal static class InkFrame
    {
        public static void Draw(Graphics g, Color color, RectangleF r, float scale)
        {
            bool antiAlias = g.AntiAlias;
            g.AntiAlias = false;
            float px = 1f / scale; // one device pixel in logical units
            float left = Snap(r.Left, scale), top = Snap(r.Top, scale);
            float right = Snap(r.Right, scale) - px, bottom = Snap(r.Bottom, scale) - px;
            g.FillRectangle(color, left, top, right - left + px, px);      // top
            g.FillRectangle(color, left, bottom, right - left + px, px);   // bottom
            g.FillRectangle(color, left, top, px, bottom - top + px);      // left
            g.FillRectangle(color, right, top, px, bottom - top + px);     // right
            g.AntiAlias = antiAlias;
        }

        private static float Snap(float v, float scale)
        {
            return (float)Math.Round(v * scale) / scale;
        }
    }

    /// <summary>White box with a 1 pixel ink frame that hosts another control (the number text boxes).</summary>
    internal sealed class InkBox : Drawable
    {
        public InkBox(Control content)
        {
            BackgroundColor = InkTheme.Paper;
            Padding = new Padding(5, 4);
            Content = content;
            Paint += (sender, e) =>
            {
                Window window = ParentWindow;
                float scale = window != null && window.Screen != null ? window.Screen.LogicalPixelSize : 1f;
                InkFrame.Draw(e.Graphics, InkTheme.Ink, new RectangleF(0, 0, Width, Height), scale > 0f ? scale : 1f);
            };
        }
    }

    /// <summary>Base class of the custom drawn controls: anti-aliased painting and a hover flag.</summary>
    internal abstract class InkDrawable : Drawable
    {
        protected bool Hover { get; private set; }

        protected InkDrawable()
        {
            BackgroundColor = InkTheme.Paper;
            Paint += (sender, e) =>
            {
                e.Graphics.AntiAlias = true;
                Draw(e.Graphics, new RectangleF(0, 0, Width, Height));
            };
            MouseEnter += (sender, e) => { Hover = true; Invalidate(); };
            MouseLeave += (sender, e) => { Hover = false; Invalidate(); };
        }

        protected abstract void Draw(Graphics g, RectangleF bounds);

        /// <summary>Logical pixel size of the screen the control is on (1.25 at 125 % display scaling).</summary>
        protected float PixelScale
        {
            get
            {
                Window window = ParentWindow;
                Screen screen = window != null ? window.Screen : null;
                float scale = screen != null ? screen.LogicalPixelSize : 1f;
                return scale > 0f ? scale : 1f;
            }
        }

        /// <summary>
        /// Draws a 1 device pixel frame just inside the rectangle, snapped to the pixel grid and without
        /// anti-aliasing, so all four sides are equally thick at any display scaling.
        /// </summary>
        protected void DrawFrame(Graphics g, Color color, RectangleF r)
        {
            InkFrame.Draw(g, color, r, PixelScale);
        }

        protected static void DrawCentered(Graphics g, Font font, Color color, RectangleF box, string text)
        {
            SizeF size = g.MeasureString(font, text);
            g.DrawText(font, color, box.X + (box.Width - size.Width) / 2f, box.Y + (box.Height - size.Height) / 2f, text);
        }
    }

    /// <summary>
    /// Slider: a thin track, the part up to the value in ink, and a round ink thumb.
    /// Click or drag anywhere on the control to set the value. Optionally logarithmic for wide ranges.
    /// </summary>
    internal sealed class InkSlider : InkDrawable
    {
        private const float Radius = 6f;

        private readonly double _min;
        private readonly double _max;
        private readonly bool _logarithmic;
        private double _value;
        private bool _dragging;

        public event EventHandler ValueChanged;

        public InkSlider(double min, double max, bool logarithmic)
        {
            _min = min;
            _max = max;
            _logarithmic = logarithmic && min > 0.0;
            _value = min;
            Height = InkTheme.RowHeight;
            Cursor = Cursors.Pointer;

            MouseDown += (sender, e) =>
            {
                if (e.Buttons != MouseButtons.Primary) return;
                _dragging = true;
                SetFromX(e.Location.X);
                e.Handled = true;
            };
            MouseMove += (sender, e) =>
            {
                if (!_dragging) return;
                SetFromX(e.Location.X);
                e.Handled = true;
            };
            MouseUp += (sender, e) =>
            {
                if (!_dragging) return;
                _dragging = false;
                Invalidate();
                e.Handled = true;
            };
        }

        public double Value
        {
            get { return _value; }
            set
            {
                double v = Math.Max(_min, Math.Min(_max, value));
                if (v == _value) return;
                _value = v;
                Invalidate();
            }
        }

        private double ToFraction(double v)
        {
            if (_max <= _min) return 0.0;
            if (_logarithmic) return (Math.Log(v) - Math.Log(_min)) / (Math.Log(_max) - Math.Log(_min));
            return (v - _min) / (_max - _min);
        }

        private double FromFraction(double t)
        {
            if (_logarithmic) return Math.Exp(Math.Log(_min) + t * (Math.Log(_max) - Math.Log(_min)));
            return _min + t * (_max - _min);
        }

        private void SetFromX(float x)
        {
            float x0 = Radius + 1f, x1 = Width - Radius - 1f;
            double t = x1 > x0 ? Math.Max(0.0, Math.Min(1.0, (x - x0) / (x1 - x0))) : 0.0;
            _value = FromFraction(t);
            Invalidate();
            if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
        }

        protected override void Draw(Graphics g, RectangleF b)
        {
            float x0 = Radius + 1f, x1 = b.Width - Radius - 1f;
            float y = b.Height / 2f;
            float x = x0 + (float)ToFraction(_value) * (x1 - x0);

            g.FillRectangle(InkTheme.Track, x0, y - 1f, x1 - x0, 2f);
            g.FillRectangle(InkTheme.Ink, x0, y - 1f, x - x0, 2f);

            float r = (Hover || _dragging) ? Radius + 1f : Radius;
            g.FillEllipse(InkTheme.Ink, x - r, y - r, 2f * r, 2f * r);
        }
    }

    /// <summary>On / off switch, square: filled with ink and a white knob on the right when on, outlined with an ink knob on the left when off.</summary>
    internal sealed class InkToggle : InkDrawable
    {
        private bool _checked;

        public event EventHandler CheckedChanged;

        public InkToggle()
        {
            Size = new Size(36, 20);
            Cursor = Cursors.Pointer;
            MouseDown += (sender, e) =>
            {
                if (e.Buttons != MouseButtons.Primary) return;
                Checked = !Checked;
                if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
                e.Handled = true;
            };
        }

        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (value == _checked) return;
                _checked = value;
                Invalidate();
            }
        }

        protected override void Draw(Graphics g, RectangleF b)
        {
            g.AntiAlias = false;
            float knob = b.Height - 6f;
            if (_checked)
            {
                g.FillRectangle(Hover ? InkTheme.InkHover : InkTheme.Ink, b);
                g.FillRectangle(InkTheme.Paper, b.Right - knob - 3f, 3f, knob, knob);
            }
            else
            {
                g.FillRectangle(Hover ? InkTheme.PaperHover : InkTheme.Paper, b);
                DrawFrame(g, InkTheme.Ink, b);
                g.FillRectangle(InkTheme.Ink, 3f, 3f, knob, knob);
            }
        }
    }

    /// <summary>Segmented choice: a row of boxed options, the selected one filled with ink.</summary>
    internal sealed class InkSegmented : InkDrawable
    {
        private const int SegmentWidth = 64;

        private readonly string[] _options;
        private readonly Font _font = InkTheme.Body(8.5f);
        private int _selected;

        public event EventHandler SelectedIndexChanged;

        public InkSegmented(params string[] options)
        {
            _options = options;
            Size = new Size(SegmentWidth * options.Length, 24);
            Cursor = Cursors.Pointer;
            MouseDown += (sender, e) =>
            {
                if (e.Buttons != MouseButtons.Primary) return;
                int index = Math.Max(0, Math.Min(_options.Length - 1, (int)(e.Location.X / SegmentWidth)));
                if (index != _selected)
                {
                    SelectedIndex = index;
                    if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
                }
                e.Handled = true;
            };
        }

        public int SelectedIndex
        {
            get { return _selected; }
            set
            {
                if (value == _selected) return;
                _selected = value;
                Invalidate();
            }
        }

        protected override void Draw(Graphics g, RectangleF b)
        {
            for (int i = 0; i < _options.Length; i++)
            {
                RectangleF cell = new RectangleF(i * SegmentWidth, 0f, SegmentWidth, b.Height);
                bool selected = i == _selected;
                if (selected) g.FillRectangle(InkTheme.Ink, cell);
                DrawCentered(g, _font, selected ? InkTheme.Paper : InkTheme.Ink, cell, _options[i]);
            }
            DrawFrame(g, InkTheme.Ink, b);
        }
    }

    internal enum InkButtonStyle
    {
        Primary,  // filled with ink
        Outline,  // ink border
        Link      // text only, underlined on hover
    }

    /// <summary>Push button in one of the ink styles.</summary>
    internal sealed class InkButton : InkDrawable
    {
        private readonly string _text;
        private readonly InkButtonStyle _style;
        private readonly Font _font;

        public event EventHandler Click;

        public InkButton(string text, InkButtonStyle style)
        {
            _text = text;
            _style = style;
            _font = InkTheme.Body(9f, style == InkButtonStyle.Primary ? FontStyle.Bold : FontStyle.None);
            int textWidth = (int)Math.Ceiling(_font.MeasureString(text).Width);
            Size = new Size(style == InkButtonStyle.Link ? textWidth + 4 : Math.Max(84, textWidth + 32), 30);
            Cursor = Cursors.Pointer;
            MouseDown += (sender, e) =>
            {
                if (e.Buttons != MouseButtons.Primary) return;
                if (Click != null) Click(this, EventArgs.Empty);
                e.Handled = true;
            };
        }

        protected override void Draw(Graphics g, RectangleF b)
        {
            switch (_style)
            {
                case InkButtonStyle.Primary:
                    g.FillRectangle(Hover ? InkTheme.InkHover : InkTheme.Ink, b);
                    DrawCentered(g, _font, InkTheme.Paper, b, _text);
                    break;
                case InkButtonStyle.Outline:
                    if (Hover) g.FillRectangle(InkTheme.PaperHover, b);
                    DrawFrame(g, InkTheme.Ink, b);
                    DrawCentered(g, _font, InkTheme.Ink, b, _text);
                    break;
                default:
                    SizeF size = g.MeasureString(_font, _text);
                    float x = 2f, y = (b.Height - size.Height) / 2f;
                    g.DrawText(_font, Hover ? InkTheme.Ink : InkTheme.Muted, x, y, _text);
                    if (Hover) g.FillRectangle(InkTheme.Ink, x, y + size.Height, size.Width, 1f);
                    break;
            }
        }
    }

    /// <summary>Tab strip: plain labels, the selected one in bold ink with a 2 px underline.</summary>
    internal sealed class InkTabBar : InkDrawable
    {
        private const float Gap = 22f;

        private readonly string[] _labels;
        private readonly Font _font = InkTheme.Body(9.5f);
        private readonly Font _bold = InkTheme.Body(9.5f, FontStyle.Bold);
        private readonly float[] _left;
        private readonly float[] _width;
        private int _selected;

        public event EventHandler SelectedIndexChanged;

        public InkTabBar(params string[] labels)
        {
            _labels = labels;
            _left = new float[labels.Length];
            _width = new float[labels.Length];
            float x = 0f;
            for (int i = 0; i < labels.Length; i++)
            {
                _left[i] = x;
                _width[i] = _bold.MeasureString(labels[i]).Width;
                x += _width[i] + Gap;
            }
            Height = 34;
            Cursor = Cursors.Pointer;
            MouseDown += (sender, e) =>
            {
                if (e.Buttons != MouseButtons.Primary) return;
                for (int i = 0; i < _labels.Length; i++)
                {
                    if (e.Location.X >= _left[i] - Gap / 2f && e.Location.X < _left[i] + _width[i] + Gap / 2f && i != _selected)
                    {
                        _selected = i;
                        Invalidate();
                        if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
                        break;
                    }
                }
                e.Handled = true;
            };
        }

        public int SelectedIndex { get { return _selected; } }

        protected override void Draw(Graphics g, RectangleF b)
        {
            g.FillRectangle(InkTheme.Rule, 0f, b.Height - 1f, b.Width, 1f);
            for (int i = 0; i < _labels.Length; i++)
            {
                bool selected = i == _selected;
                Font font = selected ? _bold : _font;
                SizeF size = g.MeasureString(font, _labels[i]);
                g.DrawText(font, selected ? InkTheme.Ink : InkTheme.Muted, _left[i], (b.Height - 2f - size.Height) / 2f, _labels[i]);
                if (selected) g.FillRectangle(InkTheme.Ink, _left[i], b.Height - 2f, _width[i], 2f);
            }
        }
    }

    /// <summary>The Sanderling mark: the toolbar bird drawn as a vector path (24 x 24 units).</summary>
    internal sealed class BirdMark : InkDrawable
    {
        public BirdMark()
        {
            Size = new Size(24, 24);
        }

        protected override void Draw(Graphics g, RectangleF b)
        {
            g.SaveTransform();
            g.TranslateTransform(0f, -1.3f);

            Pen pen = new Pen(InkTheme.Ink, 1.1f);
            pen.LineCap = PenLineCap.Round;
            pen.LineJoin = PenLineJoin.Round;
            g.DrawLines(pen, new PointF(11.6f, 15.6f), new PointF(10.2f, 18.6f), new PointF(8.6f, 19.4f));
            g.DrawLines(pen, new PointF(14.6f, 15.6f), new PointF(16.2f, 18.4f), new PointF(18f, 18.8f));
            g.DrawLine(pen, 19.6f, 10.4f, 22.8f, 11.4f);

            GraphicsPath body = new GraphicsPath();
            body.FillMode = FillMode.Alternate; // the eye is a hole
            body.AddBezier(new PointF(2.5f, 12.6f), new PointF(6f, 11.8f), new PointF(9.5f, 10.2f), new PointF(13f, 9.8f));
            body.AddBezier(new PointF(13f, 9.8f), new PointF(14.4f, 7.9f), new PointF(16.9f, 7.3f), new PointF(18.5f, 8.3f));
            body.AddBezier(new PointF(18.5f, 8.3f), new PointF(19.6f, 9f), new PointF(20f, 10.2f), new PointF(19.8f, 11.3f));
            body.AddBezier(new PointF(19.8f, 11.3f), new PointF(19.2f, 13.7f), new PointF(17f, 15.6f), new PointF(13.6f, 15.8f));
            body.AddBezier(new PointF(13.6f, 15.8f), new PointF(10f, 16f), new PointF(6.3f, 14.3f), new PointF(2.5f, 12.6f));
            body.CloseFigure();
            body.AddEllipse(16.85f, 9.15f, 1.5f, 1.5f);
            g.FillPath(InkTheme.Ink, body);

            g.RestoreTransform();
        }
    }
}
