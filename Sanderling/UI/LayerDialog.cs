using System;
using System.Collections.Generic;
using System.Globalization;
using Eto.Drawing;
using Eto.Forms;
using Sanderling.Settings;

namespace Sanderling.UI
{
    /// <summary>One document layer as shown in the layer list (in layer panel order, children after their parent).</summary>
    internal sealed class LayerItem
    {
        public Guid Id;
        public string Name;
        public int Depth;
        public int Parent = -1;  // index of the parent item, -1 for a top-level layer
        public bool Visible;     // false if the layer or one of its parents is hidden
    }

    /// <summary>
    /// Layer filter window opened by SanderlingLayers, in the same style as the settings window:
    /// the mode (All / Exclude / Only) and the layer tree with square check boxes. A checked layer includes its
    /// sub-layers, which are shown with a grey check. The caller writes Mode and Checked back to the document
    /// only when Saved is true.
    /// </summary>
    internal sealed class LayerDialog : InkDialog
    {
        private const int DialogWidth = 440;

        private static readonly string[] Hints =
        {
            "All visible layers take part in Walk mode ground and collision.",
            "Checked layers and their sub-layers are ignored, e.g. glass, trees, people or site context.",
            "Only checked layers and their sub-layers take part. Useful for very large models."
        };

        private readonly LayerList _list;
        private readonly Label _hint;
        private readonly Label _count;

        public LayerDialog(IList<LayerItem> layers, CollisionLayerMode mode, IEnumerable<Guid> checkedIds)
            : base("Layers")
        {
            Mode = mode;
            Checked = new HashSet<Guid>(checkedIds);

            InkSegmented modes = new InkSegmented("All", "Exclude", "Only") { SelectedIndex = (int)mode };
            _hint = new Label { TextColor = InkTheme.Muted, Font = InkTheme.Body(8.5f), Wrap = WrapMode.Word, Height = 34 };
            _count = new Label { TextColor = InkTheme.Muted, Font = InkTheme.Body(8.5f), VerticalAlignment = VerticalAlignment.Center };
            _list = new LayerList(layers, Checked);

            InkButton clear = new InkButton("Clear", InkButtonStyle.Link);
            clear.Click += (sender, e) =>
            {
                Checked.Clear();
                _list.Invalidate();
                UpdateState();
            };

            modes.SelectedIndexChanged += (sender, e) =>
            {
                Mode = (CollisionLayerMode)modes.SelectedIndex;
                UpdateState();
            };
            _list.CheckedChanged += (sender, e) => UpdateState();

            Label modeLabel = new Label { Text = "Mode", TextColor = InkTheme.Ink, Font = InkTheme.Body(9f), VerticalAlignment = VerticalAlignment.Center };

            SetLayout(DialogWidth,
                new TableLayout
                {
                    BackgroundColor = InkTheme.Paper,
                    Spacing = new Size(0, 6),
                    Rows =
                    {
                        new TableLayout { Height = InkTheme.RowHeight, Rows = { new TableRow(new TableCell(modeLabel, true), modes) } },
                        _hint,
                        new InkBox(_list, new Padding(1)),
                        new TableLayout { Height = 26, Rows = { new TableRow(new TableCell(_count, true), clear) } }
                    }
                },
                null);

            UpdateState();
        }

        public CollisionLayerMode Mode { get; private set; }

        /// <summary>Ids of the checked layers (their sub-layers are included implicitly).</summary>
        public HashSet<Guid> Checked { get; private set; }

        private void UpdateState()
        {
            _hint.Text = Hints[(int)Mode];
            _list.Active = Mode != CollisionLayerMode.All;

            int n = Checked.Count;
            if (Mode == CollisionLayerMode.All)
            {
                _count.Text = "Layer checks are not used in All mode.";
                _count.TextColor = InkTheme.Muted;
            }
            else if (Mode == CollisionLayerMode.Only && n == 0)
            {
                _count.Text = "No layers checked: Walk mode will have no ground or walls.";
                _count.TextColor = InkTheme.Ink;
            }
            else
            {
                _count.Text = n.ToString(CultureInfo.InvariantCulture) + (n == 1 ? " layer checked" : " layers checked");
                _count.TextColor = InkTheme.Muted;
            }
        }
    }

    /// <summary>
    /// Scrollable layer tree with square check boxes, fully custom drawn (thin ink scroll bar, mouse wheel,
    /// drag the bar). Clicking a row toggles its layer. Rows of layers whose parent is checked show a grey check.
    /// </summary>
    internal sealed class LayerList : InkDrawable
    {
        private const int RowHeight = 26;
        private const int VisibleRows = 10;
        private const int Indent = 16;
        private const float Box = 13f;
        private const float BarWidth = 4f;

        private readonly IList<LayerItem> _layers;
        private readonly HashSet<Guid> _checked;
        private readonly Font _font = InkTheme.Body(9f);
        private readonly Font _small = InkTheme.Body(8f);
        private float _scroll;
        private int _hoverRow = -1;
        private bool _draggingBar;
        private float _dragStartY;
        private float _dragStartScroll;
        private bool _active = true;

        public event EventHandler CheckedChanged;

        public LayerList(IList<LayerItem> layers, HashSet<Guid> checkedIds)
        {
            _layers = layers;
            _checked = checkedIds;
            Height = RowHeight * VisibleRows;

            MouseWheel += (sender, e) =>
            {
                ScrollTo(_scroll - e.Delta.Height * RowHeight * 3f);
                e.Handled = true;
            };
            MouseMove += (sender, e) =>
            {
                if (_draggingBar)
                {
                    float track = Height - ThumbLength;
                    float range = MaxScroll;
                    if (track > 0f) ScrollTo(_dragStartScroll + (e.Location.Y - _dragStartY) * range / track);
                    return;
                }
                int row = RowAt(e.Location);
                if (row != _hoverRow)
                {
                    _hoverRow = row;
                    Cursor = row >= 0 && _active && !Inherited(row) ? Cursors.Pointer : Cursors.Default;
                    Invalidate();
                }
            };
            MouseLeave += (sender, e) => { _hoverRow = -1; Invalidate(); };
            MouseDown += (sender, e) =>
            {
                if (e.Buttons != MouseButtons.Primary) return;
                e.Handled = true;
                if (MaxScroll > 0f && e.Location.X >= Width - 12f)
                {
                    _draggingBar = true;
                    _dragStartY = e.Location.Y;
                    _dragStartScroll = _scroll;
                    return;
                }
                int row = RowAt(e.Location);
                if (row < 0 || !_active || Inherited(row)) return;
                Guid id = _layers[row].Id;
                if (!_checked.Remove(id)) _checked.Add(id);
                Invalidate();
                if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            };
            MouseUp += (sender, e) => _draggingBar = false;
        }

        /// <summary>False in All mode: the list is greyed out and does not react to clicks.</summary>
        public bool Active
        {
            get { return _active; }
            set
            {
                if (value == _active) return;
                _active = value;
                Invalidate();
            }
        }

        private float ContentHeight { get { return _layers.Count * RowHeight; } }
        private float MaxScroll { get { return Math.Max(0f, ContentHeight - Height); } }
        private float ThumbLength { get { return ContentHeight > 0f ? Math.Max(24f, Height * Height / ContentHeight) : Height; } }

        private void ScrollTo(float y)
        {
            float v = Math.Max(0f, Math.Min(MaxScroll, y));
            if (v == _scroll) return;
            _scroll = v;
            Invalidate();
        }

        private int RowAt(PointF p)
        {
            int row = (int)Math.Floor((p.Y + _scroll) / RowHeight);
            return row >= 0 && row < _layers.Count ? row : -1;
        }

        /// <summary>Whether one of the layer's parents is checked (the layer is then included anyway).</summary>
        private bool Inherited(int row)
        {
            for (int p = _layers[row].Parent; p >= 0; p = _layers[p].Parent)
            {
                if (_checked.Contains(_layers[p].Id)) return true;
            }
            return false;
        }

        protected override void Draw(Graphics g, RectangleF b)
        {
            if (_layers.Count == 0)
            {
                DrawCentered(g, _font, InkTheme.Muted, b, "This document has no layers.");
                return;
            }

            int first = Math.Max(0, (int)(_scroll / RowHeight));
            int last = Math.Min(_layers.Count - 1, (int)((_scroll + b.Height) / RowHeight));
            for (int i = first; i <= last; i++)
            {
                LayerItem layer = _layers[i];
                float top = i * RowHeight - _scroll;
                if (i == _hoverRow && _active) g.FillRectangle(InkTheme.PaperHover, 0f, top, b.Width, RowHeight);

                float x = 10f + layer.Depth * Indent;
                RectangleF box = new RectangleF(x, top + (RowHeight - Box) / 2f, Box, Box);
                bool own = _checked.Contains(layer.Id);
                bool inherited = !own && Inherited(i);

                Color ink = _active ? InkTheme.Ink : InkTheme.Track;
                if (own || inherited)
                {
                    g.AntiAlias = false;
                    g.FillRectangle(own ? ink : InkTheme.Track, box);
                    g.AntiAlias = true;
                    Pen tick = new Pen(InkTheme.Paper, 1.6f);
                    tick.LineCap = PenLineCap.Round;
                    tick.LineJoin = PenLineJoin.Round;
                    g.DrawLines(tick, new PointF(box.X + 3f, box.Y + 6.8f), new PointF(box.X + 5.6f, box.Y + 9.4f), new PointF(box.X + 10f, box.Y + 4f));
                }
                else
                {
                    DrawFrame(g, ink, box);
                }

                Color text = _active && layer.Visible ? InkTheme.Ink : InkTheme.Muted;
                SizeF size = g.MeasureString(_font, layer.Name);
                float textX = box.Right + 9f;
                g.DrawText(_font, text, textX, top + (RowHeight - size.Height) / 2f, layer.Name);
                if (!layer.Visible)
                {
                    SizeF tag = g.MeasureString(_small, "hidden");
                    g.DrawText(_small, InkTheme.Muted, textX + size.Width + 8f, top + (RowHeight - tag.Height) / 2f, "hidden");
                }
            }

            if (MaxScroll > 0f)
            {
                float thumbY = (b.Height - ThumbLength) * _scroll / MaxScroll;
                g.AntiAlias = false;
                g.FillRectangle(_draggingBar ? InkTheme.InkHover : InkTheme.Ink, b.Width - BarWidth - 2f, thumbY + 2f, BarWidth, ThumbLength - 4f);
                g.AntiAlias = true;
            }
        }
    }
}
