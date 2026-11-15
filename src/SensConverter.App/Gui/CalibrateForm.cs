using System.Globalization;
using SensConverter.App.Input;
using SensConverter.Core;

namespace SensConverter.App.Gui;

/// <summary>
/// Measures a game: the user spins a full circle a few times while the program counts mouse movement,
/// which gives the counts-per-360 at a known in-game sens, and from that the game's constant.
/// </summary>
internal sealed class CalibrateForm : Form
{
    private sealed class Attempt
    {
        public required SpinRecord Record { get; init; }

        public bool Kept { get; set; } = true;
    }

    private readonly GlobalInput input;
    private readonly AppSettings settings;
    private readonly string exePath;
    private readonly string exeHash;
    private readonly GameEntry? existing;

    private readonly List<Attempt> attempts = new();
    private readonly SpinRecorder recorder = new();
    private readonly System.Windows.Forms.Timer liveTimer = new() { Interval = 50 };

    private readonly TextBox nameBox = new() { Width = 260 };
    private readonly TextBox sensBox = new() { Width = 90 };
    private readonly Label statusLabel = Theme.MakeLabel(string.Empty, Theme.Heading);
    private readonly Label liveLabel = Theme.MakeLabel(string.Empty, Theme.Huge, Theme.Accent);
    private readonly Label summaryLabel = Theme.MakeLabel(string.Empty, Theme.Base, Theme.Muted);
    private readonly Label warningLabel = Theme.MakeLabel(string.Empty, Theme.Strong, Theme.Warn);
    private readonly ListView spinList = new();
    private readonly CheckBox onTopBox = new() { Text = "Keep this window on top", Checked = false, AutoSize = true };
    private readonly CheckBox beepBox = new() { Text = "Beep on start and stop", Checked = true, AutoSize = true };
    private readonly Button toggleButton = Theme.MakeButton("Start spin", 150);
    private readonly Button dropButton = Theme.MakeButton("Drop selected", 130);
    private readonly Button clearButton = Theme.MakeButton("Clear all", 110);
    private readonly Button saveButton = Theme.MakeButton("Save", 110, primary: true);

    private bool updatingList;

    public CalibrateForm(
        GlobalInput input,
        AppSettings settings,
        string exePath,
        string exeHash,
        GameEntry? existing = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(settings);

        this.input = input;
        this.settings = settings;
        this.exePath = exePath;
        this.exeHash = exeHash;
        this.existing = existing;

        Text = existing is null ? "Calibrate a game" : "Recalibrate " + existing.Name;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = true;
        ClientSize = new Size(720, 806);
        Padding = new Padding(18);
        TopMost = onTopBox.Checked;

        BuildLayout();

        nameBox.Text = existing?.Name ?? ExeIdentity.PrettyNameFromPath(exePath);
        sensBox.Text = NumberInput.FormatSens(existing?.SensUsedDuringCalibration ?? 1.0);

        input.MouseMoved += OnMouseMoved;
        input.HotkeyPressed += OnHotkeyPressed;
        liveTimer.Tick += (_, _) => UpdateLive();
        liveTimer.Start();

        Theme.ApplyToForm(this);
        RefreshEverything();
    }

    /// <summary>The finished entry, once the dialog has returned OK.</summary>
    public GameEntry? Entry { get; private set; }

    private string HotkeyName => input.CurrentHotkey?.Display ?? "(no hotkey)";

    private void BuildLayout()
    {
        Label heading = Theme.MakeLabel("Measure this game", Theme.Heading);
        heading.Dock = DockStyle.Top;

        Label pathLabel = Theme.MakeLabel(exePath, Theme.Base, Theme.Muted);
        pathLabel.Dock = DockStyle.Top;
        pathLabel.MaximumSize = new Size(660, 0);
        pathLabel.Padding = new Padding(0, 2, 0, 8);

        TableLayoutPanel fields = new()
        {
            Dock = DockStyle.Top,
            ColumnCount = 4,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        fields.Controls.Add(Pad(Theme.MakeLabel("Game name")), 0, 0);
        fields.Controls.Add(Pad(nameBox), 1, 0);
        fields.Controls.Add(Pad(Theme.MakeLabel("In-game sens used")), 2, 0);
        fields.Controls.Add(Pad(sensBox), 3, 0);

        Panel steps = Theme.MakeCard();
        steps.Dock = DockStyle.Top;
        steps.Height = 132;
        steps.Margin = new Padding(0, 8, 0, 8);

        Label stepsTitle = Theme.MakeLabel("How to do a spin", Theme.Strong, Theme.Accent);
        stepsTitle.Location = new Point(14, 12);

        Label stepsBody = Theme.MakeLabel(string.Empty, Theme.Base, Theme.Text);
        stepsBody.Name = "stepsBody";
        stepsBody.Location = new Point(14, 36);
        stepsBody.MaximumSize = new Size(640, 0);

        steps.Controls.Add(stepsTitle);
        steps.Controls.Add(stepsBody);

        Panel statusPanel = new() { Dock = DockStyle.Top, Height = 118 };
        statusLabel.Location = new Point(0, 4);
        liveLabel.Location = new Point(0, 30);
        statusPanel.Controls.Add(statusLabel);
        statusPanel.Controls.Add(liveLabel);

        spinList.View = View.Details;
        spinList.FullRowSelect = true;
        spinList.CheckBoxes = true;
        spinList.HideSelection = false;
        spinList.MultiSelect = true;
        spinList.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        spinList.OwnerDraw = true;
        spinList.Height = 180;
        spinList.Dock = DockStyle.Top;
        spinList.Columns.Add("#", 44);
        spinList.Columns.Add("Counts per 360", 130);
        spinList.Columns.Add("Off median", 95);
        spinList.Columns.Add("Note", 370);
        Theme.AutoFillLastColumn(spinList);
        spinList.DrawColumnHeader += OnDrawHeader;
        spinList.DrawItem += (_, e) => e.DrawDefault = true;
        spinList.DrawSubItem += (_, e) => e.DrawDefault = true;
        spinList.ItemChecked += OnItemChecked;

        Panel summaryPanel = new() { Dock = DockStyle.Top, Height = 126 };
        summaryLabel.Location = new Point(0, 8);
        summaryLabel.MaximumSize = new Size(660, 0);
        warningLabel.Location = new Point(0, 62);
        warningLabel.MaximumSize = new Size(660, 0);
        summaryPanel.Controls.Add(summaryLabel);
        summaryPanel.Controls.Add(warningLabel);

        Panel options = new() { Dock = DockStyle.Top, Height = 30 };
        onTopBox.Location = new Point(0, 4);
        beepBox.Location = new Point(220, 4);
        onTopBox.CheckedChanged += (_, _) => TopMost = onTopBox.Checked;
        options.Controls.Add(onTopBox);
        options.Controls.Add(beepBox);

        Button cancel = Theme.MakeButton("Cancel", 110);

        toggleButton.Click += (_, _) => ToggleRecording();
        dropButton.Click += (_, _) => DropSelected();
        clearButton.Click += (_, _) => ClearAll();
        saveButton.Click += OnSave;
        cancel.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };
        CancelButton = cancel;

        Panel buttons = Theme.MakeButtonBar(
            new[] { toggleButton, dropButton, clearButton },
            new[] { cancel, saveButton });

        Controls.Add(buttons);
        Controls.Add(options);
        Controls.Add(summaryPanel);
        Controls.Add(spinList);
        Controls.Add(statusPanel);
        Controls.Add(steps);
        Controls.Add(fields);
        Controls.Add(pathLabel);
        Controls.Add(heading);

        sensBox.TextChanged += (_, _) => RefreshSummary();
        UpdateStepsText(stepsBody);
    }

    private static Control Pad(Control control)
    {
        control.Margin = new Padding(0, 6, 12, 6);
        if (control is Label)
        {
            control.Padding = new Padding(0, 4, 0, 0);
        }

        return control;
    }

    private void UpdateStepsText(Label body)
    {
        body.Text =
            $"1.  In the game, set mouse sensitivity to the value above and turn acceleration off.\n" +
            $"2.  Stand still and line up a landmark dead centre on screen.\n" +
            $"3.  Press {HotkeyName}, sweep exactly one full circle back to that landmark, press {HotkeyName} again.\n" +
            $"4.  Repeat {settings.SpinsPerCalibration} times. Overshooting and correcting is fine; the counts cancel out.";
    }

    private void OnDrawHeader(object? sender, DrawListViewColumnHeaderEventArgs e)
    {
        using SolidBrush background = new(Theme.SurfaceAlt);
        e.Graphics.FillRectangle(background, e.Bounds);

        using Pen pen = new(Theme.Border);
        e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);

        TextRenderer.DrawText(
            e.Graphics,
            e.Header?.Text ?? string.Empty,
            Theme.Strong,
            Rectangle.Inflate(e.Bounds, -6, 0),
            Theme.Muted,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
    }

    private void OnMouseMoved(int dx, int dy) => recorder.Add(dx, dy);

    private void OnHotkeyPressed() => ToggleRecording();

    private void ToggleRecording()
    {
        if (recorder.IsRecording)
        {
            SpinRecord record = recorder.Stop();
            Beep(start: false);

            if (!record.IsUsable)
            {
                statusLabel.Text = $"Ignored: only {NumberInput.FormatCounts(record.Counts)} counts of movement.";
                statusLabel.ForeColor = Theme.Warn;
                RefreshEverything();
                return;
            }

            attempts.Add(new Attempt { Record = record });
            RefreshEverything();
            return;
        }

        recorder.Start();
        Beep(start: true);
        RefreshEverything();
    }

    private void Beep(bool start)
    {
        if (!beepBox.Checked)
        {
            return;
        }

        // Off the UI thread: Console.Beep blocks for the duration of the tone.
        int frequency = start ? 1040 : 620;
        _ = Task.Run(() =>
        {
            try
            {
                Console.Beep(frequency, 110);
            }
            catch (Exception)
            {
                // No speaker, or the platform refused - the tone is a nicety, not a feature.
            }
        });
    }

    private void UpdateLive()
    {
        if (!recorder.IsRecording)
        {
            return;
        }

        double counts = recorder.CurrentCounts;
        double cm = SensMath.Cm360FromCounts(Math.Max(counts, 1), settings.Dpi);
        liveLabel.Text = $"{NumberInput.FormatCounts(counts)} counts";
        summaryLabel.Text = $"That is {NumberInput.FormatDistance(cm)} cm of mouse travel so far, " +
                            $"over {recorder.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.CurrentCulture)}s.";
    }

    private void RefreshEverything()
    {
        RefreshList();
        RefreshStatus();
        RefreshSummary();
    }

    private void RefreshStatus()
    {
        if (recorder.IsRecording)
        {
            statusLabel.Text = $"Recording - spin now, then press {HotkeyName} to stop.";
            statusLabel.ForeColor = Theme.Good;
            toggleButton.Text = "Stop spin";
            liveLabel.ForeColor = Theme.Good;
            return;
        }

        int done = attempts.Count;
        int wanted = settings.SpinsPerCalibration;
        statusLabel.ForeColor = Theme.Text;
        toggleButton.Text = "Start spin";
        liveLabel.ForeColor = Theme.Accent;
        liveLabel.Text = done == 0 ? "Ready" : $"{done} spin{(done == 1 ? string.Empty : "s")} recorded";

        statusLabel.Text = done >= wanted
            ? $"Done - press {HotkeyName} for another spin, or Save."
            : $"Press {HotkeyName} to start spin {done + 1} of {wanted}.";

        if (input.CurrentHotkey is null)
        {
            statusLabel.Text = "No hotkey available - use the Start spin button, or pick another key in settings.";
            statusLabel.ForeColor = Theme.Warn;
        }
    }

    private void RefreshList()
    {
        updatingList = true;
        try
        {
            SpinAnalysis analysis = CurrentAnalysis();

            spinList.BeginUpdate();
            spinList.Items.Clear();

            for (int i = 0; i < attempts.Count; i++)
            {
                Attempt attempt = attempts[i];
                double counts = attempt.Record.Counts;
                bool outlier = attempt.Kept && analysis.IsOutlier(counts);

                string note = attempt.Record.Warning ?? string.Empty;
                if (outlier)
                {
                    note = string.IsNullOrEmpty(note)
                        ? "Out of line with the others - consider dropping it."
                        : note;
                }

                ListViewItem item = new((i + 1).ToString(CultureInfo.CurrentCulture))
                {
                    Checked = attempt.Kept,
                    Tag = attempt,
                    ForeColor = outlier ? Theme.Bad : Theme.Text,
                };
                item.SubItems.Add(NumberInput.FormatCounts(counts));
                item.SubItems.Add(analysis.KeptCount > 1
                    ? NumberInput.FormatPercent(analysis.DeviationFromMedian(counts))
                    : "-");
                item.SubItems.Add(note);
                spinList.Items.Add(item);
            }

            spinList.EndUpdate();
        }
        finally
        {
            updatingList = false;
        }
    }

    private void RefreshSummary()
    {
        SpinAnalysis analysis = CurrentAnalysis();
        warningLabel.Text = string.Empty;

        if (!analysis.HasAnySpins)
        {
            summaryLabel.Text = "No spins kept yet.";
            saveButton.Enabled = false;
            return;
        }

        if (!NumberInput.TryParsePositive(sensBox.Text, out double sensUsed))
        {
            summaryLabel.Text = "Enter the in-game sens you are using (a number above zero).";
            saveButton.Enabled = false;
            return;
        }

        double coefficient = SensMath.CoefficientFromCalibration(analysis.Mean, sensUsed);
        double cmAtCalibration = SensMath.Cm360FromCounts(analysis.Mean, settings.Dpi);
        double targetSens = SensMath.SensForCm360(coefficient, settings.TargetCm360, settings.Dpi);

        summaryLabel.Text =
            $"Average of {analysis.KeptCount} kept spin{(analysis.KeptCount == 1 ? string.Empty : "s")}: " +
            $"{NumberInput.FormatCounts(analysis.Mean)} counts per 360 at sens {NumberInput.FormatSens(sensUsed)}.\n" +
            $"At {NumberInput.FormatDistance(settings.Dpi)} DPI that sens gives " +
            $"{NumberInput.FormatDistance(cmAtCalibration)} cm/360.\n" +
            $"For your {NumberInput.FormatDistance(settings.TargetCm360)} cm target, this game wants sens " +
            $"{NumberInput.FormatSens(targetSens)}.";

        if (analysis.HasHighSpread)
        {
            warningLabel.Text =
                $"Your spins vary by {NumberInput.FormatPercent(analysis.SpreadRatio)}. Drop the odd ones out and " +
                "record a few more. If it stays this scattered, the game probably has mouse acceleration on, " +
                "and no converter can be accurate until that is off.";
            warningLabel.ForeColor = Theme.Bad;
        }
        else if (!analysis.HasEnoughSpins)
        {
            warningLabel.Text = $"Record at least {SpinAnalysis.RecommendedSpins} spins for a steady average.";
            warningLabel.ForeColor = Theme.Warn;
        }

        saveButton.Enabled = true;
    }

    private SpinAnalysis CurrentAnalysis()
        => SpinAnalysis.AnalyzeCounts(attempts.Where(a => a.Kept).Select(a => a.Record.Counts));

    private void OnItemChecked(object? sender, ItemCheckedEventArgs e)
    {
        if (updatingList)
        {
            return;
        }

        if (e.Item.Tag is Attempt attempt)
        {
            attempt.Kept = e.Item.Checked;
        }

        BeginInvoke(RefreshEverything);
    }

    private void DropSelected()
    {
        if (spinList.SelectedItems.Count == 0)
        {
            return;
        }

        foreach (ListViewItem item in spinList.SelectedItems)
        {
            if (item.Tag is Attempt attempt)
            {
                attempts.Remove(attempt);
            }
        }

        RefreshEverything();
    }

    private void ClearAll()
    {
        if (attempts.Count == 0)
        {
            return;
        }

        attempts.Clear();
        RefreshEverything();
    }

    private void OnSave(object? sender, EventArgs e)
    {
        string name = nameBox.Text.Trim();
        if (name.Length == 0)
        {
            MessageBox.Show(this, "Give the game a name.", "Name needed", MessageBoxButtons.OK, MessageBoxIcon.Information);
            nameBox.Focus();
            return;
        }

        if (!NumberInput.TryParsePositive(sensBox.Text, out double sensUsed))
        {
            MessageBox.Show(
                this,
                "The in-game sens used has to be a number above zero - whatever the game was set to while you span.",
                "Sens needed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            sensBox.Focus();
            return;
        }

        double[] kept = attempts.Where(a => a.Kept).Select(a => a.Record.Counts).ToArray();
        if (kept.Length == 0)
        {
            MessageBox.Show(this, "No spins are kept, so there is nothing to save.", "Nothing to save", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SpinAnalysis analysis = SpinAnalysis.AnalyzeCounts(kept);

        if (!analysis.HasEnoughSpins)
        {
            DialogResult answer = MessageBox.Show(
                this,
                $"Only {kept.Length} spin{(kept.Length == 1 ? string.Empty : "s")} kept. " +
                $"{SpinAnalysis.RecommendedSpins} or more gives a much steadier result. Save anyway?",
                "Few spins",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (answer != DialogResult.Yes)
            {
                return;
            }
        }

        if (analysis.HasHighSpread)
        {
            DialogResult answer = MessageBox.Show(
                this,
                $"The kept spins vary by {NumberInput.FormatPercent(analysis.SpreadRatio)}, which is a lot. " +
                "The saved sens will be off by roughly that much. Save anyway?",
                "Scattered spins",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (answer != DialogResult.Yes)
            {
                return;
            }
        }

        GameEntry entry = GameEntry.FromCalibration(name, exePath, exeHash, sensUsed, kept, existing?.Notes ?? string.Empty);
        if (existing is not null)
        {
            entry.Id = existing.Id;
        }

        Entry = entry;
        DialogResult = DialogResult.OK;
        Close();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        liveTimer.Stop();
        liveTimer.Dispose();
        input.MouseMoved -= OnMouseMoved;
        input.HotkeyPressed -= OnHotkeyPressed;
        recorder.Reset();
        base.OnFormClosed(e);
    }
}
