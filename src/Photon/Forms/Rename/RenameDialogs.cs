namespace Photon.App.Forms;

/// <summary>Minimal modal text prompt (used for naming presets).</summary>
internal sealed class TextPromptDialog : Form
{
    private readonly TextBox _text;

    public string Value => _text.Text.Trim();

    public TextPromptDialog(string title, string label, string initial = "")
    {
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(380, 108);

        var lbl = new Label { Text = label, AutoSize = true, Location = new Point(12, 12) };
        _text = new TextBox { Location = new Point(12, 34), Width = 356, Text = initial };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(212, 70), Width = 75 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(293, 70), Width = 75 };
        AcceptButton = ok;
        CancelButton = cancel;
        Controls.AddRange([lbl, _text, ok, cancel]);
    }
}

/// <summary>Scrollable summary + error list shown after a batch rename finishes.</summary>
internal sealed class RenameSummaryDialog : Form
{
    public RenameSummaryDialog(string summary, IReadOnlyList<(string File, string Error)> errors)
    {
        Text = "Batch rename finished";
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(640, errors.Count > 0 ? 420 : 150);
        MinimumSize = new Size(420, 160);

        var lbl = new Label
        {
            Text = summary,
            Dock = DockStyle.Top,
            Height = 56,
            Padding = new Padding(12, 12, 12, 0),
        };

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 88 };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 44,
            Padding = new Padding(8),
        };
        buttons.Controls.Add(ok);
        AcceptButton = ok;
        CancelButton = ok;

        Controls.Add(buttons);
        if (errors.Count > 0)
        {
            var box = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Dock = DockStyle.Fill,
                Font = new Font(FontFamily.GenericMonospace, 8.5f),
                Text = string.Join(Environment.NewLine, errors.Select(e => $"{e.File}  —  {e.Error}")),
            };
            Controls.Add(box);
            box.BringToFront();
        }
        Controls.Add(lbl);
    }
}
