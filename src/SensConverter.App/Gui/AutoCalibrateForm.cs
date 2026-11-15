using System.Globalization;
using SensConverter.App.Input;
using SensConverter.Core;

namespace SensConverter.App.Gui;

/// <summary>
/// Measures a game without the user spinning anything: the program turns the view itself, watches
/// the screen, and works out the counts for a full 360 from how far it had to turn to get back to
/// where it started.
///
/// The one thing the user still has to do by hand is set the in-game sensitivity to a known value
/// first, and type that same value in here. There is no general way to read or write a game's own
/// settings, so that step cannot be automated.
/// </summary>
internal sealed class AutoCalibrateForm : Form
{
    /// <summary>One completed automatic measurement.</summary>
    private sealed class Run
    {
        public required double Counts360 { get; init; }

        public required double VerticalRatio { get; init; }

        public required bool LooksNonLinear { get; init; }

        public bool Kept { get; set; } = true;

        public bool HasVertical => VerticalRatio > 0;
    }

    private readonly string dataDirectory;
    private readonly GlobalInput input;
    private readonly AppSettings settings;
    private readonly string exePath;
    private readonly string exeHash;
    private readonly GameEntry? existing;

    private readonly List<Run> runs = new();

    private readonly TextBox nameBox = new() { Width = 260 };
    private readonly TextBox sensBox = new() { Width = 90 };
    private readonly TextBox sensYBox = new() { Width = 90 };
    private readonly CheckBox verticalBox = new()
    {
        Text = "This game has a separate vertical sensitivity",
        AutoSize = true,
    };

    private readonly Label statusLabel = Theme.MakeLabel(string.Empty, Theme.Heading);
    private readonly Label resultLabel = Theme.MakeLabel(string.Empty, Theme.Huge, Theme.Accent);
    private readonly Label summaryLabel = Theme.MakeLabel(string.Empty, Theme.Base, Theme.Muted);
    private readonly Label warningLabel = Theme.MakeLabel(string.Empty, Theme.Strong, Theme.Warn);
    private readonly ProgressBar progress = new() { Style = ProgressBarStyle.Continuous, Height = 8, Maximum = 1000 };
    private readonly ListView runList = new();

    private readonly Button measureButton = Theme.MakeButton("Measure now", 150);
    private readonly Button dropButton = Theme.MakeButton("Drop selected", 130);
    private readonly Button clearButton = Theme.MakeButton("Clear all", 110);
    private readonly Button detailsButton = Theme.MakeButton("Run details", 110);
    private readonly Button saveButton = Theme.MakeButton("Save", 110, primary: true);

    private CancellationTokenSource? cancellation;
    private MeasurementDiagnostics? lastDiagnostics;
    private bool measuring;
    private bool updatingList;

    public AutoCalibrateForm(
        GlobalInput input,
        AppSettings settings,
        string exePath,
        string exeHash,
        string dataDirectory,
        GameEntry? existing = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(settings);

        this.dataDirectory = dataDirectory;
        this.input = input;
        this.settings = settings;
        this.exePath = exePath;
        this.exeHash = exeHash;
        this.existing = existing;

        Text = existing is null ? "Measure a game automatically" : "Re-measure " + existing.Name;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = true;
        ClientSize = new Size(720, 780);
        Padding = new Padding(18);

        BuildLayout();

        nameBox.Text = existing?.Name ?? ExeIdentity.PrettyNameFromPath(exePath);
        sensBox.Text = NumberInput.FormatSens(existing?.SensUsedDuringCalibration ?? 1.0);
        sensYBox.Text = NumberInput.FormatSens(existing?.SensUsedDuringCalibrationY ?? 1.0);
        verticalBox.Checked = existing?.HasVertical ?? false;

        input.HotkeyPressed += OnHotkeyPressed;

        Theme.ApplyToForm(this);
        RefreshEverything();
    }

    /// <summary>The finished entry, once the dialog has returned OK.</summary>
    public GameEntry? Entry { get; private set; }

    private string HotkeyName => input.CurrentHotkey?.Display ?? "(no hotkey)";

    /// <summary>
    /// Shows the warning about moving the mouse, once, and records the answer.
    ///
    /// Automatic measurement is the only part of this program that sends input to a game rather
    /// than only listening, so it is the only part that an anti-cheat could object to. That is
    /// worth saying plainly, before anything happens, rather than burying it in a readme.
    /// </summary>
    public static bool EnsureConsent(IWin32Window owner, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.AutoMeasureAccepted)
        {
            return true;
        }

        DialogResult answer = MessageBox.Show(
            owner,
            "Automatic measurement moves the mouse itself.\n\n"
            + "Everything else this program does is passive - it only listens to your mouse. This "
            + "one feature does not: it sends mouse movement to the game, which is exactly the "
            + "thing anti-cheat software is built to detect.\n\n"
            + "In a single-player game that does not matter. In an online game with kernel-level "
            + "anti-cheat - Valorant, anything on EAC, BattlEye or FACEIT - it is a real risk of a "
            + "ban, not a theoretical one. Measure those games by hand instead.\n\n"
            + "Use automatic measurement anyway?",
            "Please read this first",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);

        if (answer != DialogResult.Yes)
        {
            return false;
        }

        settings.AutoMeasureAccepted = true;
        return true;
    }

    private void BuildLayout()
    {
        Label heading = Theme.MakeLabel("Measure this game automatically", Theme.Heading);
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
        fields.Controls.Add(Pad(Theme.MakeLabel("Sens set in game")), 2, 0);
        fields.Controls.Add(Pad(sensBox), 3, 0);
        fields.Controls.Add(Pad(verticalBox), 0, 1);
        fields.SetColumnSpan(verticalBox, 3);
        fields.Controls.Add(Pad(Theme.MakeLabel("Vertical sens set in game")), 2, 2);
        fields.Controls.Add(Pad(sensYBox), 3, 2);

        Panel steps = Theme.MakeCard();
        steps.Dock = DockStyle.Top;
        steps.Height = 142;
        steps.Margin = new Padding(0, 8, 0, 8);

        Label stepsTitle = Theme.MakeLabel("What to do", Theme.Strong, Theme.Accent);
        stepsTitle.Location = new Point(14, 12);

        Label stepsBody = Theme.MakeLabel(string.Empty, Theme.Base, Theme.Text);
        stepsBody.Name = "stepsBody";
        stepsBody.Location = new Point(14, 36);
        stepsBody.MaximumSize = new Size(650, 0);

        steps.Controls.Add(stepsTitle);
        steps.Controls.Add(stepsBody);

        Panel statusPanel = new() { Dock = DockStyle.Top, Height = 116 };
        statusLabel.Location = new Point(0, 4);
        resultLabel.Location = new Point(0, 28);
        statusPanel.Controls.Add(statusLabel);
        statusPanel.Controls.Add(resultLabel);

        Panel progressPanel = new() { Dock = DockStyle.Top, Height = 20 };
        progress.Dock = DockStyle.Top;
        progressPanel.Controls.Add(progress);

        runList.View = View.Details;
        runList.FullRowSelect = true;
        runList.CheckBoxes = true;
        runList.HideSelection = false;
        runList.MultiSelect = true;
        runList.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        runList.OwnerDraw = true;
        runList.Height = 168;
        runList.Dock = DockStyle.Top;
        runList.Columns.Add("#", 44);
        runList.Columns.Add("Counts per 360", 130);
        runList.Columns.Add("Off median", 95);
        runList.Columns.Add("Vertical", 90);
        runList.Columns.Add("Note", 280);
        Theme.AutoFillLastColumn(runList);
        runList.DrawColumnHeader += OnDrawHeader;
        runList.DrawItem += (_, e) => e.DrawDefault = true;
        runList.DrawSubItem += (_, e) => e.DrawDefault = true;
        runList.ItemChecked += OnItemChecked;
        runList.SelectedIndexChanged += (_, _) => UpdateButtons();

        Panel summaryPanel = new() { Dock = DockStyle.Top, Height = 132 };
        summaryLabel.Location = new Point(0, 8);
        summaryLabel.MaximumSize = new Size(660, 0);
        warningLabel.Location = new Point(0, 70);
        warningLabel.MaximumSize = new Size(660, 0);
        summaryPanel.Controls.Add(summaryLabel);
        summaryPanel.Controls.Add(warningLabel);

        Button cancel = Theme.MakeButton("Cancel", 110);
        cancel.Click += (_, _) =>
        {
            StopMeasuring();
            DialogResult = DialogResult.Cancel;
            Close();
        };
        CancelButton = cancel;

        measureButton.Click += (_, _) => StartOrStop();
        dropButton.Click += (_, _) => DropSelected();
        clearButton.Click += (_, _) => ClearAll();
        detailsButton.Click += (_, _) => SaveRunDetails();
        saveButton.Click += OnSave;

        Panel buttons = Theme.MakeButtonBar(
            new[] { measureButton, dropButton, clearButton, detailsButton },
            new[] { cancel, saveButton });

        Controls.Add(buttons);
        Controls.Add(summaryPanel);
        Controls.Add(runList);
        Controls.Add(progressPanel);
        Controls.Add(statusPanel);
        Controls.Add(steps);
        Controls.Add(fields);
        Controls.Add(pathLabel);
        Controls.Add(heading);

        sensBox.TextChanged += (_, _) => RefreshSummary();
        sensYBox.TextChanged += (_, _) => RefreshSummary();
        verticalBox.CheckedChanged += (_, _) => RefreshSummary();

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
            "1.  In the game, set mouse sensitivity to the value above, and turn off any mouse\n"
            + "     acceleration or smoothing the game offers.\n"
            + "2.  Stand still, face some scenery with detail in it, and look at the horizon.\n"
            + $"3.  Press {HotkeyName}. The view will turn by itself, which takes a few seconds.\n"
            + "4.  Repeat three or four times. Measurements that disagree are flagged below.";
    }

    // ---------------- measuring ----------------

    private void OnHotkeyPressed()
    {
        if (!measuring)
        {
            StartMeasuring();
        }
    }

    private void StartOrStop()
    {
        if (measuring)
        {
            StopMeasuring();
            return;
        }

        StartMeasuring();
    }

    private void StartMeasuring()
    {
        if (measuring)
        {
            return;
        }

        if (!TryReadSens(out double sensUsed, out _, out string error))
        {
            statusLabel.Text = error;
            statusLabel.ForeColor = Theme.Bad;
            return;
        }

        // Measuring our own window would move the mouse here and capture this dialog, so insist the
        // game has focus. Pressing the hotkey from inside the game is the normal way in.
        if (ScreenCapture.ForegroundWindow() == Handle)
        {
            statusLabel.Text = $"Switch to the game first, then press {HotkeyName}.";
            statusLabel.ForeColor = Theme.Warn;
            return;
        }

        if (SyntheticMouse.IsPointerAccelerationOn())
        {
            DialogResult answer = MessageBox.Show(
                this,
                "Windows pointer acceleration (\"Enhance pointer precision\") is switched on.\n\n"
                + "Games that read the mouse directly are unaffected, but games that follow the "
                + "pointer will be measured wrongly, and there is no way to tell afterwards which "
                + "kind this game is.\n\nTurn it off in Windows mouse settings first. Measure anyway?",
                "Pointer acceleration is on",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);

            if (answer != DialogResult.Yes)
            {
                return;
            }
        }

        measuring = true;
        cancellation = new CancellationTokenSource();
        bool wantVertical = verticalBox.Checked;
        CancellationToken token = cancellation.Token;

        measureButton.Text = "Stop";
        statusLabel.Text = "Measuring - do not touch the mouse.";
        statusLabel.ForeColor = Theme.Accent;
        resultLabel.Text = string.Empty;
        progress.Value = 0;
        UpdateButtons();

        MeasurementDiagnostics diagnostics = new();
        lastDiagnostics = diagnostics;

        Task.Run(() => AutoMeasureRunner.Run(wantVertical, ReportProgress, token, diagnostics), token)
            .ContinueWith(
                task => OnMeasurementFinished(task),
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default);
    }

    private void ReportProgress(double fraction)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        try
        {
            BeginInvoke(() => progress.Value = (int)Math.Clamp(fraction * 1000, 0, 1000));
        }
        catch (ObjectDisposedException)
        {
            // The dialog closed while a measurement was in flight; nothing to update.
        }
        catch (InvalidOperationException)
        {
            // Same: the handle went away between the check and the call.
        }
    }

    private void OnMeasurementFinished(Task<AutoMeasureResult> task)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        try
        {
            BeginInvoke(() => ApplyResult(task));
        }
        catch (ObjectDisposedException)
        {
            // Closed while measuring.
        }
        catch (InvalidOperationException)
        {
            // Same.
        }
    }

    private void ApplyResult(Task<AutoMeasureResult> task)
    {
        measuring = false;
        cancellation?.Dispose();
        cancellation = null;
        measureButton.Text = "Measure now";
        progress.Value = 0;

        AutoMeasureResult result = task.Status switch
        {
            TaskStatus.RanToCompletion => task.Result,
            TaskStatus.Canceled => AutoMeasureResult.Failed("Stopped part way through."),
            _ => AutoMeasureResult.Failed(
                task.Exception?.GetBaseException().Message ?? "Something went wrong while measuring."),
        };

        if (!result.Ok)
        {
            statusLabel.Text = "Could not measure.";
            statusLabel.ForeColor = Theme.Bad;

            // A failure here is nearly impossible to diagnose from the message alone, so the run
            // writes out what it actually saw and points the user at it.
            string? saved = lastDiagnostics?.Save(dataDirectory);
            string where = saved is null
                ? string.Empty
                : Environment.NewLine + Environment.NewLine
                  + "What the program saw while trying was saved to:" + Environment.NewLine + saved;

            warningLabel.Text = (result.Error ?? string.Empty) + where;
            Beep(success: false);
            RefreshEverything();
            return;
        }

        runs.Add(new Run
        {
            Counts360 = result.Counts360,
            VerticalRatio = result.VerticalRatio,
            LooksNonLinear = result.LooksNonLinear,
        });

        // Deliberately not saved here. A run that worked still has frames worth looking at when a
        // number comes out wrong, but writing half a megabyte of them after every measurement
        // would quietly fill the folder up, so it waits to be asked.
        statusLabel.Text = $"Measured: {NumberInput.FormatCounts(result.Counts360)} counts for a 360.";
        statusLabel.ForeColor = Theme.Good;
        Beep(success: true);
        RefreshEverything();
    }

    /// <summary>
    /// Writes out what the last run actually saw. Worth having even when a run succeeded: a
    /// measurement that stopped short of a full circle finishes with a number that looks perfectly
    /// reasonable, and the captured frames are the only way to tell.
    /// </summary>
    private void SaveRunDetails()
    {
        string? saved = lastDiagnostics?.Save(dataDirectory);

        if (saved is null)
        {
            warningLabel.Text = "There was nothing to save - measure something first.";
            return;
        }

        warningLabel.Text = "What the last run saw was saved to:" + Environment.NewLine + saved;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = saved,
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            // The path is on screen either way.
        }
    }

    private void StopMeasuring()
    {
        cancellation?.Cancel();
        measuring = false;
        measureButton.Text = "Measure now";
        progress.Value = 0;
        UpdateButtons();
    }

    private static void Beep(bool success)
    {
        // Off the UI thread: Console.Beep blocks for the duration of the tone.
        int frequency = success ? 880 : 300;
        _ = Task.Run(() =>
        {
            try
            {
                Console.Beep(frequency, 140);
            }
            catch (Exception ex) when (ex is PlatformNotSupportedException or IOException)
            {
                // No sound available; the window still says what happened.
            }
        });
    }

    // ---------------- list and summary ----------------

    private void OnItemChecked(object? sender, ItemCheckedEventArgs e)
    {
        if (updatingList)
        {
            return;
        }

        if (e.Item.Tag is Run run)
        {
            run.Kept = e.Item.Checked;
            RefreshSummary();
        }
    }

    private void DropSelected()
    {
        foreach (ListViewItem item in runList.SelectedItems)
        {
            if (item.Tag is Run run)
            {
                run.Kept = false;
            }
        }

        RefreshEverything();
    }

    private void ClearAll()
    {
        runs.Clear();
        statusLabel.Text = string.Empty;
        warningLabel.Text = string.Empty;
        RefreshEverything();
    }

    private SpinAnalysis Analysis
        => SpinAnalysis.AnalyzeCounts(runs.Where(r => r.Kept).Select(r => r.Counts360));

    private void RefreshEverything()
    {
        RefreshList();
        RefreshSummary();
    }

    private void RefreshList()
    {
        updatingList = true;
        try
        {
            runList.BeginUpdate();
            runList.Items.Clear();

            SpinAnalysis analysis = Analysis;

            for (int i = 0; i < runs.Count; i++)
            {
                Run run = runs[i];
                double deviation = analysis.DeviationFromMedian(run.Counts360);
                bool outlier = run.Kept && analysis.IsOutlier(run.Counts360);

                string note = run.LooksNonLinear
                    ? "The game did not scale movement evenly"
                    : outlier
                        ? "Well off the others - drop it and measure again"
                        : string.Empty;

                ListViewItem item = new((i + 1).ToString(CultureInfo.CurrentCulture))
                {
                    Checked = run.Kept,
                    Tag = run,
                };
                item.SubItems.Add(NumberInput.FormatCounts(run.Counts360));
                item.SubItems.Add(run.Kept ? NumberInput.FormatPercent(deviation) : "-");
                item.SubItems.Add(run.HasVertical ? run.VerticalRatio.ToString("0.00", CultureInfo.CurrentCulture) : "-");
                item.SubItems.Add(note);

                if (outlier || run.LooksNonLinear)
                {
                    item.ForeColor = Theme.Bad;
                }
                else if (!run.Kept)
                {
                    item.ForeColor = Theme.Muted;
                }

                runList.Items.Add(item);
            }
        }
        finally
        {
            runList.EndUpdate();
            updatingList = false;
        }
    }

    private void RefreshSummary()
    {
        SpinAnalysis analysis = Analysis;
        UpdateButtons();

        if (!analysis.HasAnySpins)
        {
            resultLabel.Text = string.Empty;
            summaryLabel.Text = $"No measurements yet. Switch to the game and press {HotkeyName}.";
            return;
        }

        if (!TryReadSens(out double sensUsed, out double sensYUsed, out string error))
        {
            resultLabel.Text = string.Empty;
            summaryLabel.Text = error;
            warningLabel.Text = string.Empty;
            return;
        }

        GameEntry preview = BuildEntry(analysis, sensUsed, sensYUsed);

        double sensX = preview.RoundedSensFor(settings);
        resultLabel.Text = NumberInput.FormatSens(sensX);

        List<string> lines = new()
        {
            $"{NumberInput.FormatCounts(analysis.Mean)} counts for a 360, averaged over "
            + $"{analysis.KeptCount} measurement{(analysis.KeptCount == 1 ? string.Empty : "s")}.",
            $"Set the sensitivity in {(string.IsNullOrWhiteSpace(nameBox.Text) ? "this game" : nameBox.Text.Trim())} "
            + $"to {NumberInput.FormatSens(sensX)}, which gives "
            + $"{NumberInput.FormatDistance(preview.ActualCm360For(settings))} cm per 360.",
        };

        if (preview.HasVertical)
        {
            double sensY = preview.RoundedSensYFor(settings);
            lines.Add($"Vertical sensitivity: {NumberInput.FormatSens(sensY)}, giving "
                      + $"{NumberInput.FormatDistance(preview.ActualCm360VerticalFor(settings))} cm per 360 "
                      + $"up and down. This game's vertical axis measured "
                      + $"{preview.MeasuredVerticalRatio.ToString("0.00", CultureInfo.CurrentCulture)}x its horizontal one.");
        }

        summaryLabel.Text = string.Join(Environment.NewLine, lines);
        warningLabel.Text = string.Join(Environment.NewLine, Warnings(analysis, preview));
    }

    private IEnumerable<string> Warnings(SpinAnalysis analysis, GameEntry preview)
    {
        if (runs.Any(r => r.Kept && r.LooksNonLinear))
        {
            yield return "This game did not turn in proportion to the mouse movement sent to it, which "
                         + "means it has its own acceleration or smoothing. Turn those off in the game "
                         + "and measure again - until then the number below is only right for movements "
                         + "the size of the ones used to measure it.";
        }

        if (analysis.HasHighSpread)
        {
            yield return $"The kept measurements vary by {NumberInput.FormatPercent(analysis.SpreadRatio)}, "
                         + "which is more than they should. Drop the odd ones out and measure again.";
        }

        if (!analysis.HasEnoughSpins)
        {
            yield return $"Only {analysis.KeptCount} measurement so far. "
                         + $"{SpinAnalysis.RecommendedSpins} or more is worth having, so one bad reading "
                         + "cannot pass unnoticed.";
        }

        if (preview.IsCalibrated)
        {
            double rounding = SensMath.RoundingErrorFraction(preview.Coefficient, settings.TargetCm360, settings.Dpi);
            if (rounding > 0.02)
            {
                yield return $"Two decimals is as fine as this game's sensitivity box goes, and the "
                             + $"nearest value is {NumberInput.FormatPercent(rounding)} off your target. "
                             + "There is no way around that from here; a small change to your target "
                             + "distance would land on a rounder number.";
            }
        }

        if (SensMath.NeedsMoreThanTwoDecimals(preview.SensFor(settings)))
        {
            yield return "This game needs a sensitivity below 0.01, so the number above has more than "
                         + "two decimals - at two it would round to zero and the game would not turn.";
        }
    }

    private void UpdateButtons()
    {
        bool idle = !measuring;
        saveButton.Enabled = idle && Analysis.HasAnySpins && TryReadSens(out _, out _, out _);
        dropButton.Enabled = idle && runList.SelectedItems.Count > 0;
        clearButton.Enabled = idle && runs.Count > 0;
        detailsButton.Enabled = idle && lastDiagnostics is not null;
        measureButton.Enabled = true;

        // The checkbox stays live even while measuring. A run decides whether to do the vertical
        // stage when it starts, so toggling this mid-run cannot confuse it, and leaving it alone
        // avoids Windows greying out its label into something unreadable on a dark background.
        Theme.SetInputEnabled(nameBox, idle);
        Theme.SetInputEnabled(sensBox, idle);
        Theme.SetInputEnabled(sensYBox, idle && verticalBox.Checked);
    }

    // ---------------- saving ----------------

    private bool TryReadSens(out double sensUsed, out double sensYUsed, out string error)
    {
        sensYUsed = 1.0;
        error = string.Empty;

        if (!NumberInput.TryParsePositive(sensBox.Text, out sensUsed))
        {
            error = "Type the sensitivity the game is set to - a positive number.";
            return false;
        }

        if (verticalBox.Checked && !NumberInput.TryParsePositive(sensYBox.Text, out sensYUsed))
        {
            error = "Type the vertical sensitivity the game is set to - a positive number.";
            return false;
        }

        return true;
    }

    /// <summary>Turns the kept measurements into an entry.</summary>
    private GameEntry BuildEntry(SpinAnalysis analysis, double sensUsed, double sensYUsed)
    {
        string name = string.IsNullOrWhiteSpace(nameBox.Text)
            ? ExeIdentity.PrettyNameFromPath(exePath)
            : nameBox.Text.Trim();

        GameEntry entry = GameEntry.FromCalibration(
            name,
            exePath,
            exeHash,
            sensUsed,
            runs.Where(r => r.Kept).Select(r => r.Counts360),
            existing?.Notes ?? string.Empty,
            MeasurementMethod.Automatic);

        if (existing is not null)
        {
            entry.Id = existing.Id;
        }

        // The vertical axis is measured as a ratio against the horizontal one, so it only becomes
        // a sensitivity once the horizontal measurement is in.
        double[] ratios = runs.Where(r => r.Kept && r.HasVertical).Select(r => r.VerticalRatio).ToArray();
        if (verticalBox.Checked && ratios.Length > 0 && analysis.Mean > 0)
        {
            const double referenceCounts = 1000;
            double degreesPerCountX = 360.0 / analysis.Mean;
            double degreesPerCountY = degreesPerCountX * ratios.Average();
            entry.SetVertical(degreesPerCountY * referenceCounts, referenceCounts, sensYUsed);
        }

        return entry;
    }

    private void OnSave(object? sender, EventArgs e)
    {
        SpinAnalysis analysis = Analysis;
        if (!analysis.HasAnySpins)
        {
            return;
        }

        if (!TryReadSens(out double sensUsed, out double sensYUsed, out string error))
        {
            MessageBox.Show(this, error, "Check the sensitivity", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (verticalBox.Checked && !runs.Any(r => r.Kept && r.HasVertical))
        {
            DialogResult answer = MessageBox.Show(
                this,
                "No vertical measurement came through, so only the horizontal sensitivity will be "
                + "saved.\n\nSave it anyway?",
                "No vertical measurement",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (answer != DialogResult.Yes)
            {
                return;
            }
        }

        Entry = BuildEntry(analysis, sensUsed, sensYUsed);
        DialogResult = DialogResult.OK;
        Close();
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

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        input.HotkeyPressed -= OnHotkeyPressed;
        cancellation?.Cancel();
        cancellation?.Dispose();
        cancellation = null;
        base.OnFormClosed(e);
    }
}
