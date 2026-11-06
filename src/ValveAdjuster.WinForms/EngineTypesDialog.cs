using System.Drawing;
using ValveAdjuster.Core;

namespace ValveAdjuster.WinForms;

/// <summary>Lists saved engine types. Choose one to load it, or delete ones that are no longer needed.</summary>
internal sealed class EngineTypesDialog : Form
{
    private readonly EngineLibrary _library;
    private readonly ListBox _list = new() { Dock = DockStyle.Fill, IntegralHeight = false, BorderStyle = BorderStyle.FixedSingle };
    private readonly Button _use = new() { Text = "Use", Width = 90, Height = 30 };
    private readonly Button _delete = new() { Text = "Delete", Width = 90, Height = 30 };
    private readonly Button _close = new() { Text = "Close", Width = 90, Height = 30, DialogResult = DialogResult.Cancel };
    private List<EngineProfile> _profiles = new();

    public EngineTypesDialog(EngineLibrary library)
    {
        _library = library;

        Text = "Engine types";
        Font = new Font("Segoe UI", 9f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(560, 380);

        var hint = new Label
        {
            Text = "Saved engine types keep the engine name, valve layout and clearance and shim specs. Measurements are not saved here.",
            Dock = DockStyle.Top,
            Height = 48,
            AutoSize = false,
            Padding = new Padding(12, 10, 12, 0),
            Tag = ThemeManager.MutedTag
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 52,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8, 10, 12, 8)
        };
        buttons.Controls.Add(_close);
        buttons.Controls.Add(_use);
        buttons.Controls.Add(_delete);

        Controls.Add(_list);
        Controls.Add(hint);
        Controls.Add(buttons);

        AcceptButton = _use;
        CancelButton = _close;

        _use.Click += (_, _) => Choose();
        _delete.Click += (_, _) => DeleteSelected();
        _list.DoubleClick += (_, _) => Choose();
        _list.SelectedIndexChanged += (_, _) => UpdateButtons();

        Shown += (_, _) => ThemeManager.SetDarkTitleBar(Handle, ThemeManager.Current.IsDark);

        ReloadList();
        ThemeManager.Apply(this);
    }

    /// <summary>The engine type the user chose to load, or null if they closed the dialog.</summary>
    public string? SelectedName { get; private set; }

    private void ReloadList()
    {
        _profiles = _library.Profiles.ToList();
        _list.Items.Clear();
        foreach (var profile in _profiles)
        {
            var c = profile.Config;
            _list.Items.Add(
                $"{profile.Name}    {c.Cylinders} cyl  |  IN {c.IntakeValvesPerCylinder}/cyl  EX {c.ExhaustValvesPerCylinder}/cyl  |  Units: {c.Units}");
        }
        if (_list.Items.Count > 0) _list.SelectedIndex = 0;
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        bool hasSelection = _list.SelectedIndex >= 0;
        _use.Enabled = hasSelection;
        _delete.Enabled = hasSelection;
    }

    private void Choose()
    {
        if (_list.SelectedIndex < 0) return;
        SelectedName = _profiles[_list.SelectedIndex].Name;
        DialogResult = DialogResult.OK;
    }

    private void DeleteSelected()
    {
        if (_list.SelectedIndex < 0) return;

        var name = _profiles[_list.SelectedIndex].Name;
        if (MessageBox.Show(this, $"Delete the engine type \"{name}\"?", "Delete engine type",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        _library.Remove(name);
        try
        {
            _library.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"The change could not be saved.\n\n{ex.Message}", "Save failed",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        ReloadList();
    }
}
