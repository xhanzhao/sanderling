using System;
using System.Collections.Generic;
using System.Globalization;
using Eto.Drawing;
using Eto.Forms;
using Sanderling.Navigation;
using Sanderling.Settings;

namespace Sanderling.UI
{
    /// <summary>
    /// Settings window opened by SanderlingSettings. Black and white, custom drawn, without the system frame:
    /// a header with the Sanderling mark (drag it to move the window), four tabs, and Reset / Cancel / Save.
    /// Numeric settings have a slider and a number box that stay in sync.
    ///
    /// The dialog edits the settings objects it is given; the caller saves them only when Saved is true.
    /// </summary>
    internal sealed class SettingsDialog : Dialog
    {
        private const int DialogWidth = 440;
        private const int PageHeight = 7 * InkTheme.RowHeight + 8;
        private const int LabelWidth = 150;
        private const int Side = 22;

        private readonly NavigationSettings _nav;
        private readonly CollisionSettings _col;
        private readonly List<Action> _refreshers = new List<Action>(); // re-read every control from the settings (after Reset)
        private readonly Panel _page = new Panel();
        private readonly Control[] _pages;

        private bool _dragging;
        private PointF _dragOffset;

        public SettingsDialog(NavigationSettings nav, CollisionSettings col)
        {
            _nav = nav;
            _col = col;

            Title = "Sanderling Settings";
            WindowStyle = WindowStyle.None;
            Resizable = false;
            Minimizable = false;
            Maximizable = false;
            ShowInTaskbar = false;
            BackgroundColor = InkTheme.Ink; // shows as the 1 px frame around the white content

            _pages = new Control[]
            {
                BuildGeneralPage(),
                BuildMovementPage(),
                BuildWalkPage(),
                BuildObjectsPage()
            };

            InkTabBar tabs = new InkTabBar("General", "Movement", "Walk", "Objects");
            tabs.SelectedIndexChanged += (sender, e) =>
            {
                _page.Content = _pages[tabs.SelectedIndex];
                tabs.Focus(); // keep the keyboard focus off the number boxes of the new page
            };
            // Open with the focus on the tab strip, not in the first number box
            tabs.CanFocus = true;
            Shown += (sender, e) => tabs.Focus();
            _page.Content = _pages[0];
            _page.Height = PageHeight;
            _page.BackgroundColor = InkTheme.Paper;

            Content = new Panel
            {
                Padding = new Padding(1),
                Content = new TableLayout
                {
                    BackgroundColor = InkTheme.Paper,
                    Width = DialogWidth,
                    Rows =
                    {
                        BuildHeader(),
                        new TableLayout { Padding = new Padding(Side, 0, Side, 0), Rows = { tabs } },
                        new TableLayout { Padding = new Padding(Side, 10, Side, 6), Rows = { _page } },
                        new Panel { Height = 1, BackgroundColor = InkTheme.Rule },
                        BuildFooter()
                    }
                }
            };

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

        /// <summary>Shows the dialog centered over the owner window.</summary>
        public void ShowCentered(Window owner)
        {
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

        // ------------------------------------------------------------------
        // Frame
        // ------------------------------------------------------------------

        private Control BuildHeader()
        {
            Label title = new Label { Text = "SANDERLING", TextColor = InkTheme.Ink, Font = InkTheme.Body(10.5f, FontStyle.Bold), VerticalAlignment = VerticalAlignment.Center };
            Label subtitle = new Label { Text = "Settings", TextColor = InkTheme.Muted, Font = InkTheme.Body(10.5f), VerticalAlignment = VerticalAlignment.Center };
            InkButton close = new InkButton("✕", InkButtonStyle.Link);
            close.Click += (sender, e) => Close();

            TableLayout header = new TableLayout
            {
                Padding = new Padding(Side, 14, Side - 4, 6),
                Spacing = new Size(8, 0),
                BackgroundColor = InkTheme.Paper,
                Rows = { new TableRow(new BirdMark(), title, subtitle, null, close) }
            };

            // The header moves the window (there is no system title bar)
            foreach (Control c in new Control[] { header, title, subtitle })
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

        private Control BuildFooter()
        {
            InkButton reset = new InkButton("Reset defaults", InkButtonStyle.Link);
            InkButton cancel = new InkButton("Cancel", InkButtonStyle.Outline);
            InkButton save = new InkButton("Save", InkButtonStyle.Primary);

            reset.Click += (sender, e) =>
            {
                _nav.ResetToDefaults();
                _col.ResetToDefaults();
                foreach (Action refresh in _refreshers) refresh();
            };
            cancel.Click += (sender, e) => Close();
            save.Click += (sender, e) =>
            {
                Saved = true;
                Close();
            };

            return new TableLayout
            {
                Padding = new Padding(Side, 14, Side, 16),
                Spacing = new Size(10, 0),
                BackgroundColor = InkTheme.Paper,
                Rows = { new TableRow(reset, null, cancel, save) }
            };
        }

        // ------------------------------------------------------------------
        // Pages
        // ------------------------------------------------------------------

        private Control BuildGeneralPage()
        {
            return Page(
                ChoiceRow("Start mode", "Mode used by the Sanderling command. F switches modes during navigation.",
                          new[] { "Fly", "Walk" }, () => (int)_nav.DefaultMode, i => _nav.DefaultMode = (NavigationMode)i),
                ChoiceRow("Look mode", "Drag: hold a mouse button to look around. Free: the cursor is locked, like a game. Tab switches during navigation.",
                          new[] { "Drag", "Free" }, () => (int)_nav.LookMode, i => _nav.LookMode = (LookMode)i),
                ToggleRow("Show HUD", "Show the info panel when navigation starts. H hides it during navigation.",
                          () => _nav.ShowHud, v => _nav.ShowHud = v),
                NumberRow("Max FPS", "Frame rate cap, 0 = unlimited.", 0, 500, 0, false,
                          () => _nav.MaxFps, v => _nav.MaxFps = (int)Math.Round(v)));
        }

        private Control BuildMovementPage()
        {
            return Page(
                NumberRow("Mouse sensitivity", "Degrees per pixel of mouse movement.", 0.01, 2.0, 2, false,
                          () => _nav.MouseSensitivity, v => _nav.MouseSensitivity = v),
                ToggleRow("Invert Y", "Invert the vertical mouse axis.",
                          () => _nav.InvertY, v => _nav.InvertY = v),
                NumberRow("Look smoothing", "Higher is more responsive, 0 = off.", 0, 100, 0, false,
                          () => _nav.LookSharpness, v => _nav.LookSharpness = v),
                NumberRow("Move smoothing", "Higher is snappier, 0 = off.", 0, 100, 0, false,
                          () => _nav.MoveSharpness, v => _nav.MoveSharpness = v),
                NumberRow("Boost (Shift)", "Speed multiplier while Shift is held.", 1, 20, 1, false,
                          () => _nav.BoostMultiplier, v => _nav.BoostMultiplier = v),
                NumberRow("Precision (Ctrl)", "Speed multiplier while Ctrl is held.", 0.01, 1, 2, false,
                          () => _nav.PrecisionMultiplier, v => _nav.PrecisionMultiplier = v));
        }

        private Control BuildWalkPage()
        {
            return Page(
                NumberRow("Eye height (m)", "Camera height above the ground.", 0.3, 3, 2, false,
                          () => _nav.EyeHeight, v => _nav.EyeHeight = v),
                NumberRow("Max step (m)", "Largest step climbed automatically.", 0, 1, 2, false,
                          () => _nav.MaxStepHeight, v => _nav.MaxStepHeight = v),
                NumberRow("Ground search (m)", "How far below the camera to look for ground.", 1, 1000, 0, true,
                          () => _nav.GroundSearchDistance, v => _nav.GroundSearchDistance = v),
                NumberRow("Ground follow", "Height smoothing, 0 = snap to the ground instantly.", 0, 100, 0, false,
                          () => _nav.GroundFollow, v => _nav.GroundFollow = v),
                ToggleRow("Collision", "Collision is on when a Walk session starts. C toggles it during navigation.",
                          () => _col.CollisionEnabled, v => _col.CollisionEnabled = v),
                NumberRow("Body radius (m)", "Radius of the collision capsule.", 0.05, 1, 2, false,
                          () => _col.BodyRadius, v => _col.BodyRadius = v),
                NumberRow("Body height (m)", "Height of the collision capsule.", 0.5, 3, 2, false,
                          () => _col.BodyHeight, v => _col.BodyHeight = v));
        }

        private Control BuildObjectsPage()
        {
            const string types = "Object types used for Walk mode ground and collision.";
            return Page(
                ToggleRow("Meshes", types, () => _col.CollideMeshes, v => _col.CollideMeshes = v),
                ToggleRow("Surfaces and polysurfaces", types, () => _col.CollideBreps, v => _col.CollideBreps = v),
                ToggleRow("Extrusions", types, () => _col.CollideExtrusions, v => _col.CollideExtrusions = v),
                ToggleRow("SubD", types, () => _col.CollideSubD, v => _col.CollideSubD = v),
                ToggleRow("Blocks", types, () => _col.CollideBlocks, v => _col.CollideBlocks = v),
                ToggleRow("Locked objects", "Whether locked objects and objects on locked layers take part.",
                          () => _col.CollideLocked, v => _col.CollideLocked = v),
                ToggleRow("Physics stats in HUD", "Show collision timing and triangle counts in the HUD.",
                          () => _col.ShowStats, v => _col.ShowStats = v));
        }

        private static Control Page(params Control[] rows)
        {
            StackLayout page = new StackLayout
            {
                Orientation = Orientation.Vertical,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                BackgroundColor = InkTheme.Paper
            };
            foreach (Control row in rows) page.Items.Add(row);
            return page;
        }

        // ------------------------------------------------------------------
        // Rows
        // ------------------------------------------------------------------

        private static Label RowLabel(string text, string tooltip)
        {
            return new Label
            {
                Text = text,
                ToolTip = tooltip,
                TextColor = InkTheme.Ink,
                Font = InkTheme.Body(9f),
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        private static TableLayout Row(params TableCell[] cells)
        {
            TableLayout row = new TableLayout { Height = InkTheme.RowHeight, Spacing = new Size(12, 0), BackgroundColor = InkTheme.Paper };
            row.Rows.Add(new TableRow(cells));
            return row;
        }

        /// <summary>Label, slider and number box. The slider and the box always show the same value.</summary>
        private Control NumberRow(string label, string tooltip, double min, double max, int decimals, bool logarithmic,
                                  Func<double> get, Action<double> set)
        {
            Label name = RowLabel(label, tooltip);
            name.Width = LabelWidth;
            InkSlider slider = new InkSlider(min, max, logarithmic);
            slider.ToolTip = tooltip;

            TextBox box = new TextBox
            {
                ShowBorder = false,
                TextAlignment = TextAlignment.Right,
                Font = InkTheme.Mono(9.5f),
                TextColor = InkTheme.Ink,
                BackgroundColor = InkTheme.Paper,
                Width = 54
            };
            InkBox boxFrame = new InkBox(box);

            Func<double, double> round = v => Math.Round(Math.Max(min, Math.Min(max, v)), decimals);
            Func<double, string> format = v => v.ToString("F" + decimals, CultureInfo.InvariantCulture);

            Action refresh = () =>
            {
                double v = get();
                slider.Value = v;
                box.Text = format(v);
            };
            _refreshers.Add(refresh);
            refresh();

            slider.ValueChanged += (sender, e) =>
            {
                double v = round(slider.Value);
                set(v);
                box.Text = format(v);
            };

            Action commit = () =>
            {
                double v;
                string text = (box.Text ?? string.Empty).Trim().Replace(',', '.');
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                {
                    set(round(v));
                }
                refresh(); // also restores the old value after invalid input
            };
            box.LostFocus += (sender, e) => commit();
            box.KeyDown += (sender, e) =>
            {
                if (e.Key == Keys.Enter)
                {
                    commit();
                    box.SelectAll();
                    e.Handled = true;
                }
                else if (e.Key == Keys.Escape)
                {
                    refresh();
                    e.Handled = true;
                }
            };

            return Row(new TableCell(name), new TableCell(slider, true), new TableCell(Middle(boxFrame)));
        }

        private Control ToggleRow(string label, string tooltip, Func<bool> get, Action<bool> set)
        {
            InkToggle toggle = new InkToggle { ToolTip = tooltip };
            Action refresh = () => toggle.Checked = get();
            _refreshers.Add(refresh);
            refresh();
            toggle.CheckedChanged += (sender, e) => set(toggle.Checked);
            return Row(new TableCell(RowLabel(label, tooltip), true), new TableCell(Middle(toggle)));
        }

        private Control ChoiceRow(string label, string tooltip, string[] options, Func<int> get, Action<int> set)
        {
            InkSegmented choice = new InkSegmented(options) { ToolTip = tooltip };
            Action refresh = () => choice.SelectedIndex = get();
            _refreshers.Add(refresh);
            refresh();
            choice.SelectedIndexChanged += (sender, e) => set(choice.SelectedIndex);
            return Row(new TableCell(RowLabel(label, tooltip), true), new TableCell(Middle(choice)));
        }

        /// <summary>Centers a fixed-size control vertically in its row.</summary>
        private static Control Middle(Control control)
        {
            return new TableLayout
            {
                BackgroundColor = InkTheme.Paper,
                Rows = { new TableRow { ScaleHeight = true }, new TableRow(control), new TableRow { ScaleHeight = true } }
            };
        }
    }
}
