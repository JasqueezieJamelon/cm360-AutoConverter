namespace SensConverter.App.Gui;

/// <summary>A one-line text prompt, themed like the rest of the program.</summary>
internal static class Prompt
{
    /// <summary>Returns the typed text, or null when cancelled.</summary>
    public static string? Text(IWin32Window owner, string title, string question, string initial, bool multiline = false)
    {
        using Form form = new()
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false,
            MinimizeBox = false,
            ClientSize = new Size(440, multiline ? 220 : 160),
            Padding = new Padding(16),
        };

        Label label = Theme.MakeLabel(question, Theme.Base, Theme.Muted);
        label.Dock = DockStyle.Top;
        label.MaximumSize = new Size(400, 0);
        label.Padding = new Padding(0, 0, 0, 8);

        TextBox box = new()
        {
            Text = initial,
            Dock = DockStyle.Top,
            Multiline = multiline,
            Height = multiline ? 100 : 26,
        };

        Button ok = Theme.MakeButton("OK", 100, primary: true);
        Button cancel = Theme.MakeButton("Cancel", 100);
        ok.DialogResult = DialogResult.OK;
        cancel.DialogResult = DialogResult.Cancel;

        Panel buttons = Theme.MakeButtonBar(null, new[] { cancel, ok }, height: 44);

        form.Controls.Add(buttons);
        form.Controls.Add(box);
        form.Controls.Add(label);
        form.AcceptButton = multiline ? null : ok;
        form.CancelButton = cancel;

        Theme.ApplyToForm(form);

        box.SelectionStart = box.TextLength;
        return form.ShowDialog(owner) == DialogResult.OK ? box.Text.Trim() : null;
    }
}
