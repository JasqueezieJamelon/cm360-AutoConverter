using SensConverter.Core;

namespace SensConverter.App.Gui;

/// <summary>
/// Your profile: mouse DPI, the sweep distance you want, and a couple of knobs. Doubles as the
/// first-run setup screen.
/// </summary>
internal sealed class SettingsForm : Form
{
    private readonly TextBox dpiBox = new() { Width = 120 };
    private readonly TextBox cmBox = new() { Width = 120 };
    private readonly TextBox inchBox = new() { Width = 120 };
    private readonly TextBox hotkeyBox = new() { Width = 160, ReadOnly = true, Cursor = Cursors.Hand };
    private readonly NumericUpDown spinsBox = new() { Width = 120, Minimum = 1, Maximum = 20 };
    private readonly Label errorLabel = Theme.MakeLabel(string.Empty, Theme.Base, Theme.Bad);

    private bool updatingDistance;
    private Hotkey hotkey;

    public SettingsForm(AppSettings settings, bool firstRun)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Result = settings.Clone();
        hotkey = Hotkey.ParseOrDefault(settings.CalibrationHotkey);

        Text = firstRun ? "Setup" : "Your settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(640, 512);
        Padding = new Padding(18);

        BuildLayout(firstRun);

        dpiBox.Text = NumberInput.FormatDistance(settings.Dpi);
        SetDistance(settings.TargetCm360);
        hotkeyBox.Text = hotkey.Display;
        spinsBox.Value = Math.Clamp(settings.SpinsPerCalibration, 1, 20);

        Theme.ApplyToForm(this);
    }

    /// <summary>The edited settings. Only meaningful when the dialog returned OK.</summary>
    public AppSettings Result { get; private set; }

    private void BuildLayout(bool firstRun)
    {
        Label title = Theme.MakeLabel(
            firstRun ? "Welcome - two numbers to start" : "Your mouse and the feel you want",
            Theme.Heading);
        title.Dock = DockStyle.Top;
        title.Padding = new Padding(0, 0, 0, 2);

        Label blurb = Theme.MakeLabel(
            "cm/360 is how far the mouse has to travel on the pad to turn a full circle in game.\n" +
            "Lower means more sensitive. These two numbers describe how you want every game to feel.",
            Theme.Base,
            Theme.Muted);
        blurb.Dock = DockStyle.Top;
        blurb.MaximumSize = new Size(600, 0);
        blurb.Padding = new Padding(0, 0, 0, 10);

        TableLayoutPanel grid = new()
        {
            Dock = DockStyle.Top,
            ColumnCount = 3,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0, 4, 0, 4),
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        AddRow(grid, "Mouse DPI", dpiBox, "as set on the mouse itself");
        AddRow(grid, "Target distance per 360", cmBox, "cm");
        AddRow(grid, "same thing in inches", inchBox, "in");
        AddRow(grid, "Spin recording hotkey", hotkeyBox, "click, then press a key");
        AddRow(grid, "Spins per calibration", spinsBox, "more spins is steadier");

        dpiBox.TextChanged += (_, _) => errorLabel.Text = string.Empty;
        cmBox.TextChanged += OnCmChanged;
        inchBox.TextChanged += OnInchChanged;
        hotkeyBox.KeyDown += OnHotkeyKeyDown;
        hotkeyBox.Enter += (_, _) => hotkeyBox.BackColor = Theme.Selection;
        hotkeyBox.Leave += (_, _) => hotkeyBox.BackColor = Theme.SurfaceAlt;

        Panel warning = Theme.MakeCard();
        warning.Dock = DockStyle.Top;
        warning.Height = 108;
        warning.Margin = new Padding(0, 10, 0, 0);

        Label warningTitle = Theme.MakeLabel("Before you calibrate anything", Theme.Strong, Theme.Warn);
        warningTitle.Location = new Point(14, 12);

        Label warningBody = Theme.MakeLabel(
            "Turn off mouse acceleration - in Windows (Mouse settings, untick Enhance pointer\n" +
            "precision) and in every game. With it on, the same sweep turns a different amount\n" +
            "each time, and no converter can be accurate.",
            Theme.Base,
            Theme.Muted);
        warningBody.Location = new Point(14, 38);

        warning.Controls.Add(warningTitle);
        warning.Controls.Add(warningBody);

        errorLabel.Dock = DockStyle.Top;
        errorLabel.Padding = new Padding(0, 10, 0, 0);
        errorLabel.MaximumSize = new Size(600, 0);

        Button ok = Theme.MakeButton(firstRun ? "Start" : "Save", 110, primary: true);
        Button cancel = Theme.MakeButton("Cancel", 110);
        ok.Click += OnOk;
        cancel.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };

        Panel buttons = Theme.MakeButtonBar(null, new[] { cancel, ok });

        AcceptButton = ok;
        CancelButton = cancel;

        // Added bottom-first so the docked order reads top to bottom.
        Controls.Add(buttons);
        Controls.Add(errorLabel);
        Controls.Add(warning);
        Controls.Add(grid);
        Controls.Add(blurb);
        Controls.Add(title);
    }

    private static void AddRow(TableLayoutPanel grid, string label, Control control, string hint)
    {
        int row = grid.RowCount;
        grid.RowCount = row + 1;
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        Label caption = Theme.MakeLabel(label);
        caption.Anchor = AnchorStyles.Left;
        caption.Margin = new Padding(0, 8, 8, 8);

        control.Margin = new Padding(0, 5, 8, 5);

        Label hintLabel = Theme.MakeLabel(hint, Theme.Base, Theme.Muted);
        hintLabel.Anchor = AnchorStyles.Left;
        hintLabel.Margin = new Padding(0, 8, 0, 8);

        grid.Controls.Add(caption, 0, row);
        grid.Controls.Add(control, 1, row);
        grid.Controls.Add(hintLabel, 2, row);
    }

    private void SetDistance(double cm)
    {
        updatingDistance = true;
        cmBox.Text = NumberInput.FormatDistance(cm);
        inchBox.Text = NumberInput.FormatDistance(SensMath.CmToInches(cm));
        updatingDistance = false;
    }

    private void OnCmChanged(object? sender, EventArgs e)
    {
        errorLabel.Text = string.Empty;
        if (updatingDistance)
        {
            return;
        }

        if (NumberInput.TryParsePositive(cmBox.Text, out double cm))
        {
            updatingDistance = true;
            inchBox.Text = NumberInput.FormatDistance(SensMath.CmToInches(cm));
            updatingDistance = false;
        }
    }

    private void OnInchChanged(object? sender, EventArgs e)
    {
        errorLabel.Text = string.Empty;
        if (updatingDistance)
        {
            return;
        }

        if (NumberInput.TryParsePositive(inchBox.Text, out double inches))
        {
            updatingDistance = true;
            cmBox.Text = NumberInput.FormatDistance(SensMath.InchesToCm(inches));
            updatingDistance = false;
        }
    }

    private void OnHotkeyKeyDown(object? sender, KeyEventArgs e)
    {
        e.Handled = true;
        e.SuppressKeyPress = true;

        Keys key = e.KeyCode;
        if (key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin
            or Keys.LControlKey or Keys.RControlKey or Keys.LShiftKey or Keys.RShiftKey
            or Keys.LMenu or Keys.RMenu or Keys.None)
        {
            return;
        }

        string? keyName = null;
        foreach (KeyValuePair<string, uint> pair in Hotkey.KeyCodes)
        {
            if (pair.Value == (uint)key)
            {
                keyName = pair.Key;
                break;
            }
        }

        if (keyName is null)
        {
            errorLabel.Text = $"{key} cannot be used as the hotkey. Function keys work well.";
            return;
        }

        List<string> parts = new();
        if (e.Control)
        {
            parts.Add("Ctrl");
        }

        if (e.Alt)
        {
            parts.Add("Alt");
        }

        if (e.Shift)
        {
            parts.Add("Shift");
        }

        parts.Add(keyName);

        if (Hotkey.TryParse(string.Join("+", parts), out Hotkey? parsed, out string error))
        {
            hotkey = parsed;
            hotkeyBox.Text = parsed.Display;
            errorLabel.Text = string.Empty;
        }
        else
        {
            errorLabel.Text = error;
        }
    }

    private void OnOk(object? sender, EventArgs e)
    {
        if (!NumberInput.TryParsePositive(dpiBox.Text, out double dpi))
        {
            Fail("Mouse DPI needs to be a number above zero, for example 800.", dpiBox);
            return;
        }

        if (dpi is < 50 or > 50_000)
        {
            Fail("That DPI looks wrong. Real mice sit between about 100 and 32000.", dpiBox);
            return;
        }

        if (!NumberInput.TryParsePositive(cmBox.Text, out double cm))
        {
            Fail("Target distance needs to be a number above zero, for example 30.", cmBox);
            return;
        }

        if (cm is < 0.5 or > 500)
        {
            Fail("That target distance is not realistic. Most people are between 10 and 80 cm.", cmBox);
            return;
        }

        Result.Dpi = dpi;
        Result.TargetCm360 = cm;
        Result.CalibrationHotkey = hotkey.Display;
        Result.SpinsPerCalibration = (int)spinsBox.Value;
        Result.Configured = true;
        Result.Normalize();

        DialogResult = DialogResult.OK;
        Close();
    }

    private void Fail(string message, Control focus)
    {
        errorLabel.Text = message;
        focus.Focus();
    }
}
