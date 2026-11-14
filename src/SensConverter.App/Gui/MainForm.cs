using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using SensConverter.App.Input;
using SensConverter.Core;

namespace SensConverter.App.Gui;

/// <summary>The main window: your profile at the top, your calibrated games below, and for the
/// selected game the one number you came for.</summary>
internal sealed class MainForm : Form
{
    private readonly Storage storage;
    private readonly GlobalInput input;

    private AppSettings settings;
    private GameLibrary library;

    private readonly Label profileLabel = Theme.MakeLabel(string.Empty, Theme.Base, Theme.Muted);
    private readonly Label hotkeyLabel = Theme.MakeLabel(string.Empty, Theme.Base, Theme.Muted);
    private readonly ListView gameList = new();

    private readonly Label detailName = Theme.MakeLabel("No game selected", Theme.Heading);
    private readonly Label detailCaption = Theme.MakeLabel(string.Empty, Theme.Base, Theme.Muted);
    private readonly Label detailSens = Theme.MakeLabel(string.Empty, Theme.Huge, Theme.Good);
    private readonly Label detailBody = Theme.MakeLabel(string.Empty, Theme.Base, Theme.Muted);
    private readonly Button copyButton = Theme.MakeButton("Copy number", 130);
    private readonly Button relocateButton = Theme.MakeButton("Find exe again", 130);

    private readonly Button recalibrateButton = Theme.MakeButton("Recalibrate", 120);
    private readonly Button renameButton = Theme.MakeButton("Rename", 100);
    private readonly Button noteButton = Theme.MakeButton("Edit note", 100);
    private readonly Button removeButton = Theme.MakeButton("Remove", 100);

    public MainForm(Storage storage, GlobalInput input, AppSettings settings, GameLibrary library)
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(library);

        this.storage = storage;
        this.input = input;
        this.settings = settings;
        this.library = library;

        Text = "Sensitivity Converter";
        ClientSize = new Size(960, 620);
        MinimumSize = new Size(820, 540);
        StartPosition = FormStartPosition.CenterScreen;
        Padding = new Padding(16);

        BuildLayout();
        Theme.ApplyToForm(this);

        RefreshProfile();
        RefreshList(selectId: library.Games.FirstOrDefault()?.Id);
    }

    private GameEntry? Selected
        => gameList.SelectedItems.Count > 0 && gameList.SelectedItems[0].Tag is GameEntry entry ? entry : null;

    private void BuildLayout()
    {
        // ---- header ----
        Panel header = new() { Dock = DockStyle.Top, Height = 78 };

        Label title = Theme.MakeLabel("Sensitivity Converter", Theme.Heading);
        title.Location = new Point(0, 0);

        profileLabel.Location = new Point(0, 26);
        hotkeyLabel.Location = new Point(0, 48);

        Button settingsButton = Theme.MakeButton("Edit settings", 130);
        settingsButton.Click += (_, _) => EditSettings();

        Button addButton = Theme.MakeButton("Add a game", 130, primary: true);
        addButton.Click += (_, _) => AddGame();

        FlowLayoutPanel headerButtons = Theme.MakeButtonStrip(new[] { settingsButton, addButton });
        headerButtons.Dock = DockStyle.Right;

        header.Controls.Add(title);
        header.Controls.Add(profileLabel);
        header.Controls.Add(hotkeyLabel);
        header.Controls.Add(headerButtons);

        // ---- bottom buttons ----
        Button openFolder = Theme.MakeButton("Open data folder", 150);
        openFolder.Click += (_, _) => OpenDataFolder();

        recalibrateButton.Click += (_, _) => RecalibrateSelected();
        renameButton.Click += (_, _) => RenameSelected();
        noteButton.Click += (_, _) => EditNoteOfSelected();
        removeButton.Click += (_, _) => RemoveSelected();

        Panel bottom = Theme.MakeButtonBar(
            new[] { recalibrateButton, renameButton, noteButton, removeButton },
            new[] { openFolder });

        // ---- detail panel ----
        Panel detail = Theme.MakeCard();
        detail.Dock = DockStyle.Right;
        detail.Width = 330;
        detail.Padding = new Padding(16);

        detailName.Location = new Point(16, 14);
        detailName.MaximumSize = new Size(296, 0);
        detailCaption.Location = new Point(16, 54);
        detailSens.Location = new Point(14, 74);
        copyButton.Location = new Point(16, 138);
        copyButton.Click += (_, _) => CopySens();
        relocateButton.Location = new Point(154, 138);
        relocateButton.Click += (_, _) => RelocateSelected();
        detailBody.Location = new Point(16, 184);
        detailBody.MaximumSize = new Size(296, 0);

        detail.Controls.Add(detailName);
        detail.Controls.Add(detailCaption);
        detail.Controls.Add(detailSens);
        detail.Controls.Add(copyButton);
        detail.Controls.Add(relocateButton);
        detail.Controls.Add(detailBody);

        // ---- list ----
        gameList.View = View.Details;
        gameList.FullRowSelect = true;
        gameList.HideSelection = false;
        gameList.MultiSelect = false;
        gameList.Dock = DockStyle.Fill;
        gameList.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        gameList.OwnerDraw = true;
        gameList.Columns.Add("Game", 240);
        gameList.Columns.Add("Sens to use", 110);
        gameList.Columns.Add("Counts per 360", 120);
        gameList.Columns.Add("Calibrated", 110);
        gameList.DrawColumnHeader += OnDrawHeader;
        gameList.DrawItem += (_, e) => e.DrawDefault = true;
        gameList.DrawSubItem += (_, e) => e.DrawDefault = true;
        gameList.SelectedIndexChanged += (_, _) => RefreshDetail();
        gameList.DoubleClick += (_, _) => RecalibrateSelected();
        Theme.AutoFillLastColumn(gameList);

        Panel listHost = new() { Dock = DockStyle.Fill, Padding = new Padding(0, 8, 12, 8) };
        listHost.Controls.Add(gameList);

        // Docking is resolved from the last control added backwards, so the one that fills what is
        // left has to go in first or it swallows the space meant for the others.
        Controls.Add(listHost);
        Controls.Add(detail);
        Controls.Add(bottom);
        Controls.Add(header);
    }

    private static void OnDrawHeader(object? sender, DrawListViewColumnHeaderEventArgs e)
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

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        foreach (string warning in storage.Warnings)
        {
            MessageBox.Show(this, warning, "Saved data", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        storage.ClearWarnings();

        ApplyHotkey();

        if (!input.RawInputAvailable)
        {
            MessageBox.Show(
                this,
                "Windows would not let this program listen to raw mouse input, so calibration cannot count " +
                "your spins. Everything else still works.",
                "Mouse input unavailable",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    // ---------------- profile ----------------

    private void RefreshProfile()
    {
        profileLabel.Text =
            $"{NumberInput.FormatDistance(settings.Dpi)} DPI   -   target {NumberInput.FormatDistance(settings.TargetCm360)} cm/360 " +
            $"({NumberInput.FormatDistance(SensMath.CmToInches(settings.TargetCm360))} in/360)";

        hotkeyLabel.Text = input.CurrentHotkey is { } hotkey
            ? $"Spin hotkey: {hotkey.Display}"
            : "Spin hotkey unavailable - pick a different key in settings";
        hotkeyLabel.ForeColor = input.CurrentHotkey is null ? Theme.Warn : Theme.Muted;
    }

    private void EditSettings()
    {
        using SettingsForm form = new(settings, firstRun: false);
        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        bool hotkeyChanged = !string.Equals(form.Result.CalibrationHotkey, settings.CalibrationHotkey, StringComparison.OrdinalIgnoreCase);
        settings = form.Result;
        storage.SaveSettings(settings);

        if (hotkeyChanged || input.CurrentHotkey is null)
        {
            ApplyHotkey();
        }

        RefreshProfile();
        RefreshList(Selected?.Id);
    }

    /// <summary>Claims the configured hotkey, telling the user if Windows refuses.</summary>
    public void ApplyHotkey()
    {
        Hotkey hotkey = Hotkey.ParseOrDefault(settings.CalibrationHotkey);
        if (!input.TrySetHotkey(hotkey, out string error))
        {
            MessageBox.Show(this, error, "Hotkey", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        RefreshProfile();
    }

    // ---------------- list ----------------

    private void RefreshList(string? selectId)
    {
        library.SortByName();

        gameList.BeginUpdate();
        gameList.Items.Clear();

        foreach (GameEntry game in library.Games)
        {
            bool missing = !FileExists(game.ExePath);
            ListViewItem item = new(missing ? "(!) " + game.Name : game.Name)
            {
                Tag = game,
                ForeColor = missing ? Theme.Warn : Theme.Text,
            };

            item.SubItems.Add(game.IsCalibrated ? NumberInput.FormatSens(game.SensFor(settings)) : "-");
            item.SubItems.Add(NumberInput.FormatCounts(game.Counts360));
            item.SubItems.Add(game.CalibratedOn.ToLocalTime().ToString("d MMM yyyy", CultureInfo.CurrentCulture));
            gameList.Items.Add(item);
        }

        gameList.EndUpdate();

        if (gameList.Items.Count > 0)
        {
            int index = 0;
            if (selectId is not null)
            {
                for (int i = 0; i < gameList.Items.Count; i++)
                {
                    if (gameList.Items[i].Tag is GameEntry entry && entry.Id == selectId)
                    {
                        index = i;
                        break;
                    }
                }
            }

            gameList.Items[index].Selected = true;
            gameList.Items[index].Focused = true;
        }

        RefreshDetail();
    }

    private void RefreshDetail()
    {
        GameEntry? game = Selected;

        bool has = game is not null;
        recalibrateButton.Enabled = has;
        renameButton.Enabled = has;
        noteButton.Enabled = has;
        removeButton.Enabled = has;
        copyButton.Enabled = has;
        relocateButton.Enabled = has;

        if (game is null)
        {
            detailName.Text = library.Games.Count == 0 ? "Nothing calibrated yet" : "No game selected";
            detailCaption.Text = library.Games.Count == 0
                ? "Press \"Add a game\" to measure your first one."
                : string.Empty;
            detailSens.Text = string.Empty;
            detailBody.Text = string.Empty;
            return;
        }

        double sens = game.SensFor(settings);
        double actualCm = SensMath.Cm360ForSens(game.Coefficient, sens, settings.Dpi);

        detailName.Text = game.Name;
        detailCaption.Text = "Set the in-game sensitivity to";
        detailSens.Text = NumberInput.FormatSens(sens);

        string missing = FileExists(game.ExePath)
            ? string.Empty
            : "\n\nThe exe is no longer at the saved path. Use \"Find exe again\" if the game moved.";

        string note = string.IsNullOrWhiteSpace(game.Notes) ? string.Empty : $"\n\nNote: {game.Notes}";

        detailBody.Text =
            $"Measured at in-game sens {NumberInput.FormatSens(game.SensUsedDuringCalibration)}, " +
            $"where a 360 took {NumberInput.FormatCounts(game.Counts360)} counts " +
            $"({game.Spins.Count} spin{(game.Spins.Count == 1 ? string.Empty : "s")}).\n\n" +
            $"That gives {NumberInput.FormatDistance(actualCm)} cm/360 at your {NumberInput.FormatDistance(settings.Dpi)} DPI.\n\n" +
            $"Game constant: {game.Coefficient.ToString("0.######", CultureInfo.CurrentCulture)} degrees per count per unit of sens.\n\n" +
            $"Calibrated {game.CalibratedOn.ToLocalTime().ToString("d MMM yyyy, HH:mm", CultureInfo.CurrentCulture)}.\n\n" +
            $"{game.ExePath}{missing}{note}";
    }

    private static bool FileExists(string path)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(path) && File.Exists(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    // ---------------- actions ----------------

    private void AddGame()
    {
        string? exePath = PickExe("Point at the game's exe");
        if (exePath is null)
        {
            return;
        }

        string? hash = HashWithWaitCursor(exePath);
        if (hash is null)
        {
            return;
        }

        GameEntry? sameExe = library.FindByHash(hash);
        if (sameExe is not null)
        {
            DialogResult answer = MessageBox.Show(
                this,
                $"That is the same exe as \"{sameExe.Name}\", which is already calibrated.\n\n" +
                "Recalibrate it now?",
                "Already in your library",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (answer == DialogResult.Yes)
            {
                sameExe.ExePath = exePath;
                Recalibrate(sameExe);
            }

            return;
        }

        GameEntry? samePath = library.FindByPath(exePath);
        if (samePath is not null)
        {
            DialogResult answer = MessageBox.Show(
                this,
                $"\"{samePath.Name}\" was calibrated from this same path, but the exe has changed - " +
                "the game was probably updated.\n\n" +
                "Yes: keep the old calibration and just note the new exe.\n" +
                "No: recalibrate from scratch.",
                "Game updated",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question);

            if (answer == DialogResult.Yes)
            {
                samePath.ExeHash = hash;
                Save();
                RefreshList(samePath.Id);
                return;
            }

            if (answer == DialogResult.No)
            {
                Recalibrate(samePath);
            }

            return;
        }

        using CalibrateForm form = new(input, settings, exePath, hash);
        if (form.ShowDialog(this) == DialogResult.OK && form.Entry is not null)
        {
            library.Upsert(form.Entry);
            Save();
            RefreshList(form.Entry.Id);
        }
    }

    private void RecalibrateSelected()
    {
        if (Selected is { } game)
        {
            Recalibrate(game);
        }
    }

    private void Recalibrate(GameEntry game)
    {
        using CalibrateForm form = new(input, settings, game.ExePath, game.ExeHash, game);
        if (form.ShowDialog(this) == DialogResult.OK && form.Entry is not null)
        {
            library.Upsert(form.Entry);
            Save();
            RefreshList(form.Entry.Id);
        }
    }

    private void RelocateSelected()
    {
        if (Selected is not { } game)
        {
            return;
        }

        string? exePath = PickExe($"Where is {game.Name} now?");
        if (exePath is null)
        {
            return;
        }

        string? hash = HashWithWaitCursor(exePath);
        if (hash is null)
        {
            return;
        }

        if (!string.Equals(hash, game.ExeHash, StringComparison.OrdinalIgnoreCase))
        {
            DialogResult answer = MessageBox.Show(
                this,
                "That exe is not identical to the one this game was calibrated from, which usually just means " +
                "the game was updated.\n\nKeep the existing calibration?",
                "Different exe",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (answer != DialogResult.Yes)
            {
                return;
            }

            game.ExeHash = hash;
        }

        game.ExePath = exePath;
        Save();
        RefreshList(game.Id);
    }

    private void RenameSelected()
    {
        if (Selected is not { } game)
        {
            return;
        }

        string? name = Prompt.Text(this, "Rename", "What should this game be called?", game.Name);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        game.Name = name.Trim();
        Save();
        RefreshList(game.Id);
    }

    private void EditNoteOfSelected()
    {
        if (Selected is not { } game)
        {
            return;
        }

        string? note = Prompt.Text(
            this,
            "Note",
            "Anything worth remembering about this game's sensitivity?",
            game.Notes,
            multiline: true);

        if (note is null)
        {
            return;
        }

        game.Notes = note;
        Save();
        RefreshList(game.Id);
    }

    private void RemoveSelected()
    {
        if (Selected is not { } game)
        {
            return;
        }

        DialogResult answer = MessageBox.Show(
            this,
            $"Remove \"{game.Name}\" and its calibration?",
            "Remove game",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (answer != DialogResult.Yes)
        {
            return;
        }

        library.Remove(game.Id);
        Save();
        RefreshList(selectId: null);
    }

    private void CopySens()
    {
        if (Selected is not { } game)
        {
            return;
        }

        try
        {
            Clipboard.SetText(NumberInput.FormatSens(game.SensFor(settings)));
            detailCaption.Text = "Copied - set the in-game sensitivity to";
        }
        catch (Exception ex) when (ex is ExternalException or ThreadStateException)
        {
            MessageBox.Show(this, "Windows would not give up the clipboard. Try again.", "Copy failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void OpenDataFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = storage.DataDirectory,
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            MessageBox.Show(this, storage.DataDirectory, "Data folder", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private string? PickExe(string title)
    {
        using OpenFileDialog dialog = new()
        {
            Title = title,
            Filter = "Programs (*.exe)|*.exe|All files (*.*)|*.*",
            CheckFileExists = true,
        };

        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
    }

    private string? HashWithWaitCursor(string path)
    {
        try
        {
            Cursor = Cursors.WaitCursor;
            return ExeIdentity.Sha256File(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(
                this,
                "That file could not be read:\n\n" + ex.Message,
                "Cannot read the exe",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return null;
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void Save()
    {
        try
        {
            storage.SaveLibrary(library);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(
                this,
                "Your games could not be saved:\n\n" + ex.Message,
                "Save failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
