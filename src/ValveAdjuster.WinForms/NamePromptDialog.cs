using System.Drawing;

namespace ValveAdjuster.WinForms;

/// <summary>A small single-field prompt, used to name an engine type.</summary>
internal sealed class NamePromptDialog : Form
{
    private readonly TextBox _box = new() { Width = 280 };

    private NamePromptDialog(string title, string prompt, string initial)
    {
        Text = title;
        Font = new Font("Segoe UI", 9f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        _box.Text = initial;

        var label = new Label { Text = prompt, AutoSize = true, Margin = new Padding(0, 0, 0, 6) };

        var ok = new Button { Text = "OK", Width = 90, DialogResult = DialogResult.None };
        var cancel = new Button { Text = "Cancel", Width = 90, DialogResult = DialogResult.Cancel };
        ok.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_box.Text))
            {
                MessageBox.Show(this, "Enter a name.", title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DialogResult = DialogResult.OK;
        };

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Margin = new Padding(0, 10, 0, 0)
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);

        var root = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            Padding = new Padding(12)
        };
        root.Controls.Add(label);
        root.Controls.Add(_box);
        root.Controls.Add(buttons);

        Controls.Add(root);
        AcceptButton = ok;
        CancelButton = cancel;

        Shown += (_, _) => ThemeManager.SetDarkTitleBar(Handle, ThemeManager.Current.IsDark);
        ThemeManager.Apply(this);
    }

    public static bool TryGetName(IWin32Window owner, string title, string prompt, string initial, out string name)
    {
        using var dialog = new NamePromptDialog(title, prompt, initial);
        bool accepted = dialog.ShowDialog(owner) == DialogResult.OK;
        name = accepted ? dialog._box.Text.Trim() : "";
        return accepted;
    }
}
