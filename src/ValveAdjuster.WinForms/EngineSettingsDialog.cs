using System.Drawing;
using ValveAdjuster.Core;

namespace ValveAdjuster.WinForms;

/// <summary>
/// Edits the engine identity, valve layout, and the clearance and shim specs for each valve type.
/// Every section has a heading so it is clear which fields belong to intake and which to exhaust.
/// </summary>
internal sealed class EngineSettingsDialog : Form
{
    private readonly TextBox _name = new() { Width = 260 };
    private readonly TextBox _units = new() { Width = 90 };
    private readonly NumericUpDown _cylinders = Numeric(1, EngineConfig.MaxCylinders, 0);
    private readonly NumericUpDown _intakePer = Numeric(0, EngineConfig.MaxValvesPerCylinder, 0);
    private readonly NumericUpDown _exhaustPer = Numeric(0, EngineConfig.MaxValvesPerCylinder, 0);
    private readonly SpecFields _intake = new("Intake valves",
        "Clearance window (feeler gauge) and the shim sizes available for intake valves.");
    private readonly SpecFields _exhaust = new("Exhaust valves",
        "Clearance window (feeler gauge) and the shim sizes available for exhaust valves.");

    public EngineConfig Result { get; private set; } = new();

    public EngineSettingsDialog(EngineConfig current)
    {
        Text = "Engine settings";
        Font = new Font("Segoe UI", 9f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        _name.Text = current.Name;
        _units.Text = current.Units;
        _cylinders.Value = Math.Clamp(current.Cylinders, 1, EngineConfig.MaxCylinders);
        _intakePer.Value = Math.Clamp(current.IntakeValvesPerCylinder, 0, EngineConfig.MaxValvesPerCylinder);
        _exhaustPer.Value = Math.Clamp(current.ExhaustValvesPerCylinder, 0, EngineConfig.MaxValvesPerCylinder);
        _intake.Load(current.Intake);
        _exhaust.Load(current.Exhaust);

        var table = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Padding = new Padding(12) };

        AddHeading(table, "Engine", "Identify the engine and its valve layout.");
        AddRow(table, "Engine name", _name);
        AddRow(table, "Units", _units);
        AddRow(table, "Cylinders", _cylinders);
        AddRow(table, "Intake valves per cylinder", _intakePer);
        AddRow(table, "Exhaust valves per cylinder", _exhaustPer);

        _intake.AddTo(table);
        _exhaust.AddTo(table);

        var ok = new Button { Text = "OK", Width = 90, DialogResult = DialogResult.None };
        var cancel = new Button { Text = "Cancel", Width = 90, DialogResult = DialogResult.Cancel };
        ok.Click += OnOk;

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 14, 0, 0)
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        AddFullWidth(table, buttons);

        Controls.Add(table);
        Shown += (_, _) => ThemeManager.SetDarkTitleBar(Handle, ThemeManager.Current.IsDark);
        ThemeManager.Apply(this);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    private void OnOk(object? sender, EventArgs e)
    {
        var candidate = new EngineConfig
        {
            Name = _name.Text.Trim(),
            Units = _units.Text.Trim(),
            Cylinders = (int)_cylinders.Value,
            IntakeValvesPerCylinder = (int)_intakePer.Value,
            ExhaustValvesPerCylinder = (int)_exhaustPer.Value,
            Intake = _intake.Read(),
            Exhaust = _exhaust.Read()
        };

        var errors = Validation.Config(candidate).Where(i => i.IsError).Select(i => i.Message).ToList();
        if (errors.Count > 0)
        {
            MessageBox.Show(this, string.Join(Environment.NewLine, errors), "Check engine settings",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Result = candidate;
        DialogResult = DialogResult.OK;
    }

    private static NumericUpDown Numeric(decimal min, decimal max, int places) => new()
    {
        Minimum = min,
        Maximum = max,
        DecimalPlaces = places,
        Increment = places == 0 ? 1m : 1m / (decimal)Math.Pow(10, places),
        Width = 120
    };

    /// <summary>Adds a bold section heading with an optional grey hint line spanning both columns.</summary>
    private static void AddHeading(TableLayoutPanel table, string text, string? hint)
    {
        var heading = new Label
        {
            Text = text,
            AutoSize = true,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            Margin = new Padding(0, table.RowCount == 0 ? 0 : 16, 0, 0)
        };
        AddFullWidth(table, heading);

        if (!string.IsNullOrEmpty(hint))
        {
            AddFullWidth(table, new Label
            {
                Text = hint,
                AutoSize = true,
                Tag = ThemeManager.MutedTag,
                Margin = new Padding(0, 0, 0, 4)
            });
        }
    }

    private static void AddFullWidth(TableLayoutPanel table, Control control)
    {
        int row = table.RowCount;
        table.RowCount = row + 1;
        table.Controls.Add(control, 0, row);
        table.SetColumnSpan(control, 2);
    }

    private static void AddRow(TableLayoutPanel table, string label, Control control)
    {
        int row = table.RowCount;
        table.RowCount = row + 1;
        table.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 6, 12, 6)
        }, 0, row);
        control.Margin = new Padding(0, 3, 0, 3);
        table.Controls.Add(control, 1, row);
    }

    /// <summary>The five numeric fields for one valve type. Added to the shared table so headings align.</summary>
    private sealed class SpecFields
    {
        private readonly string _title;
        private readonly string _hint;
        private readonly NumericUpDown _clearMin = Numeric(0, 1000, 3);
        private readonly NumericUpDown _clearMax = Numeric(0, 1000, 3);
        private readonly NumericUpDown _shimMin = Numeric(0, 1000, 3);
        private readonly NumericUpDown _shimMax = Numeric(0, 1000, 3);
        private readonly NumericUpDown _increment = Numeric(0, 1000, 3);

        public SpecFields(string title, string hint)
        {
            _title = title;
            _hint = hint;
        }

        public void AddTo(TableLayoutPanel table)
        {
            AddHeading(table, _title, _hint);
            AddRow(table, "Minimum clearance", _clearMin);
            AddRow(table, "Maximum clearance", _clearMax);
            AddRow(table, "Smallest available shim", _shimMin);
            AddRow(table, "Largest available shim", _shimMax);
            AddRow(table, "Shim increment", _increment);
        }

        public void Load(ValveTypeSpec spec)
        {
            _clearMin.Value = Clamp(spec.ClearanceMin);
            _clearMax.Value = Clamp(spec.ClearanceMax);
            _shimMin.Value = Clamp(spec.ShimMin);
            _shimMax.Value = Clamp(spec.ShimMax);
            _increment.Value = Clamp(spec.ShimIncrement);
        }

        public ValveTypeSpec Read() => new()
        {
            ClearanceMin = _clearMin.Value,
            ClearanceMax = _clearMax.Value,
            ShimMin = _shimMin.Value,
            ShimMax = _shimMax.Value,
            ShimIncrement = _increment.Value
        };

        private static decimal Clamp(decimal value) => Math.Clamp(value, 0m, 1000m);
    }
}
