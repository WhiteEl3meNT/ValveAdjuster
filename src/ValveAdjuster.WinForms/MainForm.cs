using System.Drawing;
using System.Globalization;
using System.Text.Json;
using Microsoft.Win32;
using ValveAdjuster.Core;
using System.Reflection;

namespace ValveAdjuster.WinForms;

public sealed class MainForm : Form
{
    // Column order must match the order columns are added in BuildGrid.
    private enum Col
    {
        Valve, Target, Gap, ExistingShim, Calculated, Recommended,
        Difference, InstalledShim, Expected, FinalGap, Status, Notes
    }

    private const string SessionFilter =
        "Valve adjustment session (*.valveadj.json)|*.valveadj.json|All files (*.*)|*.*";

    private static readonly string[] CardTitles =
    {
        "Total valves", "Within spec", "Needs adjustment", "Completed",
        "Pending verification", "Failed verification", "Not measured"
    };

    private EngineConfig _config = new();
    private List<ValveRecord> _valves = new();
    private List<ValveResult> _results = new();
    private string? _path;
    private bool _dirty;
    private readonly EngineLibrary _library;

    private readonly MenuStrip _menu = new();
    private readonly ToolStripMenuItem _useTypeMenu = new() { Text = "&Use engine type" };
    private readonly Label _title = new()
    {
        Dock = DockStyle.Top,
        Height = 44,
        AutoSize = false,
        Font = new Font("Segoe UI", 13f, FontStyle.Bold),
        Padding = new Padding(12, 10, 0, 0)
    };
    private readonly FlowLayoutPanel _cards = new()
    {
        Dock = DockStyle.Top,
        Height = 96,
        Padding = new Padding(12, 8, 12, 8)
    };
    private readonly List<Panel> _cardPanels = new();
    private readonly Dictionary<string, Label> _cardValues = new();
    private readonly DataGridView _grid = new();
    private readonly Label _detail = new()
    {
        Dock = DockStyle.Bottom,
        Height = 56,
        AutoSize = false,
        Padding = new Padding(12, 8, 12, 4)
    };

    public MainForm()
    {
        Text = "ValveAdjuster";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1100, 620);
        Size = new Size(1280, 740);
        Font = new Font("Segoe UI", 9f);
        Icon = LoadWindowIcon();
        _library = LoadLibrary();

        BuildMenu();
        BuildCards();
        BuildGrid();

        // Docking: the most recently added control docks first (outermost), so the Fill control goes first.
        Controls.Add(_grid);
        Controls.Add(_detail);
        Controls.Add(_cards);
        Controls.Add(_title);
        Controls.Add(_menu);
        MainMenuStrip = _menu;

        FormClosing += (_, e) => { if (!ConfirmDiscard()) e.Cancel = true; };
        Shown += (_, _) => OnFirstShow();
        Activated += (_, _) => RefreshTheme();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        _valves = _config.BuildValves();
        UpdateUnitHeaders();
        RefreshTheme();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        base.Dispose(disposing);
    }

    // ----- Startup and engine types -------------------------------------------------

    private EngineLibrary LoadLibrary()
    {
        var path = EngineLibrary.DefaultPath;
        try
        {
            return EngineLibrary.Load(path);
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
        {
            // Keep a copy of the unreadable file so saved types are not lost, then start empty.
            try { File.Copy(path, path + ".bak", overwrite: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            MessageBox.Show($"Saved engine types could not be read and were moved to a backup file.\n\n{ex.Message}",
                "Engine types", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return new EngineLibrary(path);
        }
    }

    private void OnFirstShow()
    {
        ThemeManager.SetDarkTitleBar(Handle, ThemeManager.Current.IsDark);

        if (!Validation.Config(_config).Any(i => i.IsError)) return;

        if (!_library.IsEmpty)
        {
            var name = ChooseEngineType();
            if (name is not null)
            {
                UseEngineType(name);
                return;
            }
        }

        EditSettings();
    }

    private string? ChooseEngineType()
    {
        using var dialog = new EngineTypesDialog(_library);
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.SelectedName : null;
    }

    private void ManageEngineTypes()
    {
        var name = ChooseEngineType();
        if (name is not null) UseEngineType(name);
    }

    /// <summary>Loads a saved engine type as a fresh session. Measurements are not carried over.</summary>
    private void UseEngineType(string name)
    {
        var profile = _library.Find(name);
        if (profile is null || !ConfirmDiscard()) return;

        _config = EngineLibrary.Copy(profile.Config);
        _valves = _config.BuildValves();
        _path = null;
        _dirty = false;
        UpdateUnitHeaders();
        RefreshAll();
    }

    private void SaveEngineType()
    {
        if (!NamePromptDialog.TryGetName(this, "Save engine type", "Name for this engine type:", _config.Name, out var name))
            return;

        if (_library.Find(name) is not null &&
            MessageBox.Show(this, $"Replace the saved engine type \"{name}\"?", "Replace engine type",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        _library.Upsert(name, _config);
        if (!SaveLibrary()) return;

        if (_config.Name != name)
        {
            _config.Name = name;
            _dirty = true;
            RefreshAll();
        }

        _detail.Text = $"Saved engine type \"{name}\". It will be available under Engine > Use engine type.";
    }

    private void RebuildUseTypeMenu()
    {
        _useTypeMenu.DropDownItems.Clear();

        if (_library.IsEmpty)
        {
            _useTypeMenu.DropDownItems.Add(new ToolStripMenuItem("(none saved yet)") { Enabled = false });
        }
        else
        {
            foreach (var profile in _library.Profiles)
            {
                var name = profile.Name;
                _useTypeMenu.DropDownItems.Add(new ToolStripMenuItem(name, null, (_, _) => UseEngineType(name)));
            }
        }

        ThemeManager.StyleMenuItems(_useTypeMenu.DropDownItems);
    }

    private bool SaveLibrary()
    {
        try
        {
            _library.Save();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"The engine types could not be saved.\n\n{ex.Message}", "Save failed",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    // ----- Menus, cards, grid ---------------------------------------------------------

    private void BuildMenu()
    {
        var file = new ToolStripMenuItem("&File");
        file.DropDownItems.Add("&New session", null, (_, _) => NewSession());
        file.DropDownItems.Add("&Open...", null, (_, _) => OpenSession());
        file.DropDownItems.Add("&Save", null, (_, _) => SaveSession(saveAs: false));
        file.DropDownItems.Add("Save &as...", null, (_, _) => SaveSession(saveAs: true));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add("&Export results (CSV)...", null, (_, _) => ExportCsv());
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add("E&xit", null, (_, _) => Close());

        var engine = new ToolStripMenuItem("&Engine");
        _useTypeMenu.DropDownOpening += (_, _) => RebuildUseTypeMenu();
        engine.DropDownItems.Add(_useTypeMenu);
        engine.DropDownItems.Add("&Manage engine types...", null, (_, _) => ManageEngineTypes());
        engine.DropDownItems.Add("&Save as engine type...", null, (_, _) => SaveEngineType());
        engine.DropDownItems.Add(new ToolStripSeparator());
        engine.DropDownItems.Add("Engine &settings...", null, (_, _) => EditSettings());

        var help = new ToolStripMenuItem("&Help");
        help.DropDownItems.Add("&About", null, (_, _) => MessageBox.Show(this,
            "ValveAdjuster\n" +
            "Disclaimer: This application is intended as a calculation and documentation aid. All shim recommendations " +
            "and final valve clearances must be verified against the applicable engine manufacturer's specifications " +
            "and approved service procedures.\n\nLicensed under the MIT License.",
            "About", MessageBoxButtons.OK, MessageBoxIcon.Information));

        _menu.Items.AddRange(new ToolStripItem[] { file, engine, help });
        RebuildUseTypeMenu();
    }
/// <summary>Loads the embedded logo.ico for the window title bar and taskbar. Returns null if it is missing.</summary>
    private static Icon? LoadWindowIcon()
    {
    using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("logo.ico");
    return stream is null ? null : new Icon(stream);
    }
    private void BuildCards()
    {
        foreach (var title in CardTitles)
        {
            var card = new Panel { Width = 156, Height = 76, Margin = new Padding(0, 0, 10, 0) };
            var value = new Label
            {
                Text = "0",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(10, 0, 0, 0),
                Font = new Font("Segoe UI", 20f, FontStyle.Bold)
            };
            var caption = new Label
            {
                Text = title,
                Dock = DockStyle.Top,
                Height = 24,
                AutoSize = false,
                Padding = new Padding(10, 6, 0, 0),
                Tag = ThemeManager.MutedTag
            };
            card.Controls.Add(value);   // Fill first, then the Top caption docks above it.
            card.Controls.Add(caption);
            _cards.Controls.Add(card);
            _cardPanels.Add(card);
            _cardValues[title] = value;
        }
    }

    private void BuildGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.EditMode = DataGridViewEditMode.EditOnEnter;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        _grid.BorderStyle = BorderStyle.None;
        _grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
        _grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _grid.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        _grid.RowTemplate.Height = 28;

        AddColumn(Col.Valve, "Valve", editable: false, width: 100, unitTag: false);
        AddColumn(Col.Target, "Target clearance", editable: false, width: 85, unitTag: true);
        AddColumn(Col.Gap, "Measured clearance", editable: true, width: 110, unitTag: true);
        AddColumn(Col.ExistingShim, "Existing shim", editable: true, width: 100, unitTag: true);
        AddColumn(Col.Calculated, "Calculated shim", editable: false, width: 100, unitTag: true);
        AddColumn(Col.Recommended, "Recommended shim", editable: false, width: 110, unitTag: true);
        AddColumn(Col.Difference, "Difference", editable: false, width: 95, unitTag: true);
        AddColumn(Col.InstalledShim, "Installed shim", editable: true, width: 100, unitTag: true);
        AddColumn(Col.Expected, "Expected clearance", editable: false, width: 110, unitTag: true);
        AddColumn(Col.FinalGap, "Final clearance", editable: true, width: 105, unitTag: true);
        AddColumn(Col.Status, "Status", editable: false, width: 135, unitTag: false);
        AddColumn(Col.Notes, "Notes", editable: true, width: 200, unitTag: false);

        _grid.CellEndEdit += OnCellEndEdit;
        _grid.SelectionChanged += (_, _) => UpdateDetail();
    }

    private void AddColumn(Col col, string header, bool editable, int width, bool unitTag)
    {
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = col.ToString(),
            HeaderText = header,
            ReadOnly = !editable,
            Width = width,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            Tag = unitTag ? header : null
        });
    }

    // ----- Theme ----------------------------------------------------------------------

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color)) return;

        if (InvokeRequired) BeginInvoke(new Action(RefreshTheme));
        else RefreshTheme();
    }

    /// <summary>Re-reads the Windows light/dark setting and repaints everything if it changed.</summary>
    private void RefreshTheme()
    {
        ThemeManager.Refresh();
        ApplyTheme();
        RefreshAll();
    }

    private void ApplyTheme()
    {
        var p = ThemeManager.Current;
        ThemeManager.Apply(this);

        _cards.BackColor = p.Band;
        foreach (var card in _cardPanels) card.BackColor = p.Surface;

        _title.BackColor = p.Back;
        _title.ForeColor = p.Text;
        _detail.BackColor = p.Band;
        _detail.ForeColor = p.Text;
    }

    // ----- Refresh --------------------------------------------------------------------

    private void RefreshAll()
    {
        _results = AdjustmentCalculator.EvaluateAll(_config, _valves);

        _grid.SuspendLayout();
        if (_grid.Rows.Count != _results.Count)
        {
            _grid.Rows.Clear();
            for (int i = 0; i < _results.Count; i++) _grid.Rows.Add();
        }
        for (int i = 0; i < _results.Count; i++) WriteRow(i, _results[i]);
        _grid.ResumeLayout();

        UpdateCards();
        UpdateTitle();
        UpdateDetail();
    }

    private void WriteRow(int index, ValveResult r)
    {
        var v = r.Valve;
        var row = _grid.Rows[index];

        SetCell(row, Col.Valve, v.Label);
        SetCell(row, Col.Target, Num(r.TargetClearance));
        SetCell(row, Col.Gap, Num(v.InitialGap));
        SetCell(row, Col.ExistingShim, Num(v.ExistingShim));
        SetCell(row, Col.Calculated, Num(r.CalculatedShim));
        SetCell(row, Col.Recommended, Num(r.RecommendedShim));
        SetCell(row, Col.Difference, Num(r.ShimDifference, signed: true));
        SetCell(row, Col.InstalledShim, Num(v.InstalledShim));
        SetCell(row, Col.Expected, Num(r.ExpectedClearance));
        SetCell(row, Col.FinalGap, Num(v.FinalGap));
        SetCell(row, Col.Status, r.Status.Display());
        SetCell(row, Col.Notes, v.Notes);

        var status = row.Cells[(int)Col.Status].Style;
        status.BackColor = ThemeManager.StatusColor(r.Status);
        status.ForeColor = ThemeManager.StatusText();
    }

    private static void SetCell(DataGridViewRow row, Col col, string text)
    {
        var cell = row.Cells[(int)col];
        if (!Equals(cell.Value, text)) cell.Value = text;
    }

    private void UpdateCards()
    {
        var s = AdjustmentSummary.From(_results);
        _cardValues["Total valves"].Text = s.Total.ToString(CultureInfo.CurrentCulture);
        _cardValues["Within spec"].Text = s.WithinSpec.ToString(CultureInfo.CurrentCulture);
        _cardValues["Needs adjustment"].Text = s.NeedsAdjustment.ToString(CultureInfo.CurrentCulture);
        _cardValues["Completed"].Text = s.Completed.ToString(CultureInfo.CurrentCulture);
        _cardValues["Pending verification"].Text = s.PendingVerification.ToString(CultureInfo.CurrentCulture);
        _cardValues["Failed verification"].Text = s.FailedVerification.ToString(CultureInfo.CurrentCulture);
        _cardValues["Not measured"].Text = s.NotMeasured.ToString(CultureInfo.CurrentCulture);
    }

    private void UpdateTitle()
    {
        var name = string.IsNullOrWhiteSpace(_config.Name) ? "Untitled engine" : _config.Name;
        _title.Text = $"{name}   |   {_config.Cylinders} cyl   |   IN {_config.IntakeValvesPerCylinder}/cyl   EX {_config.ExhaustValvesPerCylinder}/cyl   |   Units: {_config.Units}";
        Text = $"{(_dirty ? "* " : "")}{name} - ValveAdjuster";
    }

    private void UpdateUnitHeaders()
    {
        var u = string.IsNullOrWhiteSpace(_config.Units) ? "" : $" ({_config.Units})";
        foreach (DataGridViewColumn column in _grid.Columns)
        {
            if (column.Tag is string baseHeader) column.HeaderText = baseHeader + u;
        }
    }

    private void UpdateDetail()
    {
        var row = _grid.CurrentRow;
        if (row is null || row.Index < 0 || row.Index >= _results.Count)
        {
            _detail.Text = "Select a valve to see how its shim was calculated.";
            return;
        }

        var r = _results[row.Index];
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(r.Explanation)) parts.Add(r.Explanation);
        parts.AddRange(r.Issues.Select(i => (i.IsError ? "Error: " : "Warning: ") + i.Message));

        _detail.Text = parts.Count == 0
            ? "Enter the measured clearance and the existing shim to calculate the replacement."
            : string.Join("   |   ", parts);
    }

    // ----- Editing --------------------------------------------------------------------

    private void OnCellEndEdit(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _valves.Count) return;

        var valve = _valves[e.RowIndex];
        var cell = _grid.Rows[e.RowIndex].Cells[e.ColumnIndex];
        var col = (Col)e.ColumnIndex;
        var text = (cell.Value as string ?? "").Trim();
        cell.ErrorText = string.Empty;

        if (col == Col.Notes)
        {
            valve.Notes = text;
        }
        else
        {
            decimal? value = null;
            if (text.Length > 0)
            {
                if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out var parsed) || parsed <= 0)
                {
                    cell.ErrorText = "Enter a number greater than 0.";
                    return; // keep the typed text so it can be corrected
                }
                value = parsed;
            }

            switch (col)
            {
                case Col.Gap: valve.InitialGap = value; break;
                case Col.ExistingShim: valve.ExistingShim = value; break;
                case Col.InstalledShim: valve.InstalledShim = value; break;
                case Col.FinalGap: valve.FinalGap = value; break;
                default: return;
            }
        }

        _dirty = true;
        RefreshAll();
    }

    // ----- Session commands -----------------------------------------------------------

    private void NewSession()
    {
        if (!ConfirmDiscard()) return;

        _path = null;
        _dirty = false;
        _config = new EngineConfig();
        _valves = _config.BuildValves();
        UpdateUnitHeaders();
        RefreshAll();
        EditSettings();
    }

    private void EditSettings()
    {
        using var dialog = new EngineSettingsDialog(_config);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        _config = dialog.Result;
        _valves = _config.BuildValves(_valves);
        _dirty = true;
        UpdateUnitHeaders();
        RefreshAll();
    }

    private void OpenSession()
    {
        if (!ConfirmDiscard()) return;

        using var dialog = new OpenFileDialog { Filter = SessionFilter, Title = "Open adjustment session" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var file = SessionStore.Load(dialog.FileName);
            _config = file.Engine;
            _valves = _config.BuildValves(file.Valves);
            _path = dialog.FileName;
            _dirty = false;
            UpdateUnitHeaders();
            RefreshAll();
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"The session could not be opened.\n\n{ex.Message}", "Open failed",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private bool SaveSession(bool saveAs)
    {
        string? path = _path;
        if (saveAs || path is null)
        {
            using var dialog = new SaveFileDialog
            {
                Filter = SessionFilter,
                DefaultExt = "json",
                FileName = SuggestedFileName(),
                Title = "Save adjustment session"
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return false;
            path = dialog.FileName;
        }

        try
        {
            SessionStore.Save(path, _config, _valves);
            _path = path;
            _dirty = false;
            UpdateTitle();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"The session could not be saved.\n\n{ex.Message}", "Save failed",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private void ExportCsv()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "CSV file (*.csv)|*.csv",
            DefaultExt = "csv",
            FileName = SuggestedFileName().Replace(".valveadj.json", ".csv"),
            Title = "Export adjustment results"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            CsvExporter.Write(dialog.FileName, _config, _results);
            MessageBox.Show(this, "Results exported.", "Export complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"The export failed.\n\n{ex.Message}", "Export failed",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>Returns true if it is safe to discard or replace the current session.</summary>
    private bool ConfirmDiscard()
    {
        if (!_dirty) return true;

        var answer = MessageBox.Show(this, "Save the changes to this session first?", "Unsaved changes",
            MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

        return answer switch
        {
            DialogResult.Yes => SaveSession(saveAs: false),
            DialogResult.No => true,
            _ => false
        };
    }

    private string SuggestedFileName()
    {
        var name = string.IsNullOrWhiteSpace(_config.Name) ? "valve-adjustment" : _config.Name;
        var safe = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
        return safe + ".valveadj.json";
    }

    private static string Num(decimal? value, bool signed = false) =>
        value is decimal d
            ? d.ToString(signed ? "+0.000;-0.000;0.000" : "0.000", CultureInfo.CurrentCulture)
            : "";
}
