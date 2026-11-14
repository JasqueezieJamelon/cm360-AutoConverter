namespace SensConverter.App.Gui;

/// <summary>Dark theme in one place, plus small factories so every screen looks the same.</summary>
internal static class Theme
{
    public static readonly Color Background = Color.FromArgb(24, 24, 28);
    public static readonly Color Surface = Color.FromArgb(34, 34, 40);
    public static readonly Color SurfaceAlt = Color.FromArgb(44, 44, 52);
    public static readonly Color Border = Color.FromArgb(62, 62, 74);
    public static readonly Color Text = Color.FromArgb(236, 236, 240);
    public static readonly Color Muted = Color.FromArgb(152, 152, 166);
    public static readonly Color Accent = Color.FromArgb(92, 170, 255);
    public static readonly Color Good = Color.FromArgb(96, 204, 134);
    public static readonly Color Warn = Color.FromArgb(240, 192, 92);
    public static readonly Color Bad = Color.FromArgb(240, 112, 112);
    public static readonly Color Selection = Color.FromArgb(52, 72, 102);

    public static readonly Font Base = new("Segoe UI", 9.75f);
    public static readonly Font Strong = new("Segoe UI Semibold", 9.75f);
    public static readonly Font Heading = new("Segoe UI Semibold", 12f);
    public static readonly Font Huge = new("Segoe UI", 30f, FontStyle.Bold);
    public static readonly Font Mono = new("Consolas", 9.75f);

    public static void ApplyToForm(Form form)
    {
        ArgumentNullException.ThrowIfNull(form);

        form.BackColor = Background;
        form.ForeColor = Text;
        form.Font = Base;
        Apply(form.Controls);
    }

    private static void Apply(Control.ControlCollection controls)
    {
        foreach (Control control in controls)
        {
            switch (control)
            {
                case Button button:
                    StyleButton(button);
                    break;

                case TextBox textBox:
                    textBox.BackColor = SurfaceAlt;
                    textBox.ForeColor = Text;
                    textBox.BorderStyle = BorderStyle.FixedSingle;
                    break;

                case NumericUpDown numeric:
                    numeric.BackColor = SurfaceAlt;
                    numeric.ForeColor = Text;
                    numeric.BorderStyle = BorderStyle.FixedSingle;
                    break;

                case ListView listView:
                    listView.BackColor = Surface;
                    listView.ForeColor = Text;
                    listView.BorderStyle = BorderStyle.FixedSingle;
                    break;

                case CheckBox checkBox:
                    // Left as the system style on purpose: a flat dark checkbox hides its own tick.
                    checkBox.ForeColor = Text;
                    checkBox.BackColor = Color.Transparent;
                    checkBox.FlatStyle = FlatStyle.System;
                    break;

                case Label label:
                    label.BackColor = Color.Transparent;
                    break;

                case Panel panel:
                    if (panel.BackColor == SystemColors.Control)
                    {
                        panel.BackColor = Background;
                    }

                    panel.ForeColor = Text;
                    break;
            }

            if (control.HasChildren)
            {
                Apply(control.Controls);
            }
        }
    }

    public static void StyleButton(Button button)
    {
        ArgumentNullException.ThrowIfNull(button);

        button.FlatStyle = FlatStyle.Flat;
        button.BackColor = SurfaceAlt;
        button.ForeColor = Text;
        button.FlatAppearance.BorderColor = Border;
        button.FlatAppearance.MouseOverBackColor = Selection;
        button.FlatAppearance.MouseDownBackColor = Border;
        button.UseVisualStyleBackColor = false;
        button.Cursor = Cursors.Hand;
        button.AutoSize = false;
        button.Height = Math.Max(button.Height, 30);
    }

    public static Button MakeButton(string text, int width = 120, bool primary = false)
    {
        Button button = new()
        {
            Text = text,
            Width = width,
            Height = 32,
        };

        StyleButton(button);

        if (primary)
        {
            button.BackColor = Color.FromArgb(38, 92, 150);
            button.FlatAppearance.BorderColor = Accent;
            button.Font = Strong;
        }

        return button;
    }

    public static Label MakeLabel(string text, Font? font = null, Color? color = null)
        => new()
        {
            Text = text,
            AutoSize = true,
            Font = font ?? Base,
            ForeColor = color ?? Text,
            BackColor = Color.Transparent,
        };

    public static Panel MakeCard()
        => new()
        {
            BackColor = Surface,
            Padding = new Padding(14),
        };

    /// <summary>
    /// A row of buttons that keeps its place when the window resizes. Hand-placed coordinates do
    /// not survive docking - the panel is still its default width when the buttons go in - so the
    /// buttons live in flow panels pinned to each edge instead.
    /// </summary>
    public static Panel MakeButtonBar(Button[]? left, Button[]? right, int height = 48)
    {
        Panel bar = new() { Dock = DockStyle.Bottom, Height = height };

        if (right is { Length: > 0 })
        {
            FlowLayoutPanel strip = MakeButtonStrip(right, rightToLeft: true);
            strip.Dock = DockStyle.Right;
            bar.Controls.Add(strip);
        }

        if (left is { Length: > 0 })
        {
            FlowLayoutPanel strip = MakeButtonStrip(left, rightToLeft: false);
            strip.Dock = DockStyle.Left;
            bar.Controls.Add(strip);
        }

        return bar;
    }

    /// <summary>
    /// Lays buttons out in a row. With rightToLeft, the first button given ends up furthest right.
    /// </summary>
    public static FlowLayoutPanel MakeButtonStrip(Button[] buttons, bool rightToLeft = true)
    {
        ArgumentNullException.ThrowIfNull(buttons);

        FlowLayoutPanel strip = new()
        {
            FlowDirection = rightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Padding = new Padding(0, 8, 0, 0),
        };

        foreach (Button button in buttons)
        {
            button.Margin = rightToLeft ? new Padding(8, 0, 0, 0) : new Padding(0, 0, 8, 0);
            strip.Controls.Add(button);
        }

        return strip;
    }

    /// <summary>Keeps the last column of a details-view list filling the width, so no bare strip
    /// of unpainted header shows at the right.</summary>
    public static void AutoFillLastColumn(ListView list)
    {
        ArgumentNullException.ThrowIfNull(list);

        void Resize(object? sender, EventArgs e)
        {
            if (list.Columns.Count == 0)
            {
                return;
            }

            int used = 0;
            for (int i = 0; i < list.Columns.Count - 1; i++)
            {
                used += list.Columns[i].Width;
            }

            int remaining = list.ClientSize.Width - used;
            if (remaining > 60)
            {
                list.Columns[^1].Width = remaining;
            }
        }

        list.Resize += Resize;
        Resize(list, EventArgs.Empty);
    }

    /// <summary>A 1px separator line.</summary>
    public static Panel MakeDivider()
        => new()
        {
            Height = 1,
            Dock = DockStyle.Top,
            BackColor = Border,
        };
}
