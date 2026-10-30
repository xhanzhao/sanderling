using System;
using Eto.Drawing;
using Eto.Forms;

namespace Sanderling.UI
{
    /// <summary>
    /// Frame shared by Sanderling's windows: black and white, no system frame, a 1 pixel ink border,
    /// a header with the Sanderling mark and the window name (drag it to move the window, x closes),
    /// the body, a rule, and a footer with Cancel / Save on the right.
    /// Esc closes without saving; Saved tells the caller whether the user clicked Save.
    /// </summary>
    internal abstract class InkDialog : Dialog
    {
        protected const int Side = 22;

        private readonly string _name;
        private bool _dragging;
        private PointF _dragOffset;

        protected InkDialog(string name)
        {
            _name = name;
            Title = "Sanderling " + name;
            WindowStyle = WindowStyle.None;
            Resizable = false;
            Minimizable = false;
            Maximizable = false;
            ShowInTaskbar = false;
            BackgroundColor = InkTheme.Paper;

            KeyDown += (sender, e) =>
            {
                if (e.Key == Keys.Escape)
                {
                    Close();
                    e.Handled = true;
                }
            };
        }

        /// <summary>Whether the user closed the window with Save.</summary>
        public bool Saved { get; private set; }

        /// <summary>
        /// Builds the window: header, body, rule and footer.
        /// </summary>
        /// <param name="width">Inner width of the window</param>
        /// <param name="body">Main content; gets the side padding</param>
        /// <param name="footerLeft">Optional control at the left of the footer (e.g. Reset defaults)</param>
        protected void SetLayout(int width, Control body, Control footerLeft)
        {
            InkButton cancel = new InkButton("Cancel", InkButtonStyle.Outline);
            InkButton save = new InkButton("Save", InkButtonStyle.Primary);
            cancel.Click += (sender, e) => Close();
            save.Click += (sender, e) =>
            {
                OnSaving();
                Saved = true;
                Close();
            };

            TableLayout footer = new TableLayout
            {
                Padding = new Padding(Side, 14, Side, 16),
                Spacing = new Size(10, 0),
                Rows = { new TableRow(footerLeft, null, cancel, save) }
            };

            // The 1 pixel frame is drawn snapped to the pixel grid, so all four sides are equally thick.
            // The header, body and footer containers have no background of their own: one that reaches the edge
            // of the window could otherwise paint over the frame, which is drawn underneath them.
            Content = new InkBox(
                new TableLayout
                {
                    Width = width,
                    Rows =
                    {
                        BuildHeader(),
                        new TableLayout { Padding = new Padding(Side, 0, Side, 6), Rows = { body } },
                        new Panel { Height = 1, BackgroundColor = InkTheme.Rule },
                        footer
                    }
                },
                new Padding(1));
        }

        /// <summary>Called when Save is clicked, before the window closes (e.g. to commit a text box being edited).</summary>
        protected virtual void OnSaving()
        {
        }

        /// <summary>Shows the dialog centered over the owner window.</summary>
        public void ShowCentered(Window owner)
        {
            // Without a system frame, Windows sizes the window a few pixels too small for its content and cuts off
            // the bottom edge. Size the client area to the content explicitly instead.
            LoadComplete += (sender, e) =>
            {
                SizeF size = Content.GetPreferredSize(SizeF.PositiveInfinity);
                ClientSize = new Size((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height));
            };

            if (owner != null)
            {
                LoadComplete += (sender, e) =>
                {
                    Rectangle o = owner.Bounds;
                    Location = new Point(o.X + (o.Width - Width) / 2, o.Y + (o.Height - Height) / 3);
                };
                ShowModal(owner);
            }
            else
            {
                ShowModal();
            }
        }

        private Control BuildHeader()
        {
            InkTitle title = new InkTitle(_name);
            InkButton close = new InkButton("✕", InkButtonStyle.Link);
            close.Click += (sender, e) => Close();

            TableLayout header = new TableLayout
            {
                Padding = new Padding(Side, 14, Side - 4, 6),
                Spacing = new Size(8, 0),
                Rows = { new TableRow(title, null, close) }
            };

            // The header moves the window (there is no system title bar)
            foreach (Control c in new Control[] { header, title })
            {
                c.MouseDown += (sender, e) =>
                {
                    if (e.Buttons != MouseButtons.Primary) return;
                    _dragging = true;
                    _dragOffset = Mouse.Position - (PointF)Location;
                    e.Handled = true;
                };
                c.MouseMove += (sender, e) =>
                {
                    if (!_dragging) return;
                    Location = Point.Round(Mouse.Position - _dragOffset);
                };
                c.MouseUp += (sender, e) => _dragging = false;
            }
            return header;
        }
    }
}
