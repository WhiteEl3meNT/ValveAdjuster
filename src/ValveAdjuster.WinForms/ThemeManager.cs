using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;
using ValveAdjuster.Core;

namespace ValveAdjuster.WinForms;

internal sealed record Palette(
    bool IsDark,
    Color Back,
    Color Surface,
    Color Band,
    Color Input,
    Color Text,
    Color Muted,
    Color Border,
    Color Button,
    Color Header,
    Color HeaderText,
    Color Selection,
    Color Grid);

/// <summary>
/// Follows the Windows app light/dark setting (Settings > Personalization > Colors) and paints
/// WinForms controls to match. .NET 8 WinForms has no built-in dark mode, so colours are applied here.
/// </summary>
internal static class ThemeManager
{
    /// <summary>Tag value that marks a Label as secondary text.</summary>
    public const string MutedTag = "muted";

    public static readonly Palette Light = new(
        false,
        SystemColors.Control,
        Color.White,
        Color.FromArgb(243, 245, 248),
        Color.White,
        Color.Black,
        Color.DimGray,
        Color.FromArgb(200, 205, 212),
        SystemColors.Control,
        Color.FromArgb(38, 46, 62),
        Color.White,
        Color.FromArgb(205, 225, 250),
        Color.FromArgb(214, 218, 224));

    public static readonly Palette Dark = new(
        true,
        Color.FromArgb(32, 32, 34),
        Color.FromArgb(40, 40, 45),
        Color.FromArgb(46, 46, 52),
        Color.FromArgb(50, 50, 56),
        Color.FromArgb(230, 230, 230),
        Color.FromArgb(150, 150, 156),
        Color.FromArgb(70, 70, 78),
        Color.FromArgb(56, 56, 63),
        Color.FromArgb(58, 60, 70),
        Color.FromArgb(235, 235, 235),
        Color.FromArgb(55, 80, 120),
        Color.FromArgb(62, 62, 70));

    public static Palette Current { get; private set; } = Light;

    /// <summary>Re-reads the Windows setting. Returns true if the palette changed.</summary>
    public static bool Refresh()
    {
        var next = IsSystemDark() ? Dark : Light;
        if (next == Current) return false;
        Current = next;
        return true;
    }

    /// <summary>
    /// AppsUseLightTheme is the app-mode value (0 = dark). This is the setting that
    /// decides whether apps such as this one should use a dark look.
    /// </summary>
    public static bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    public static void Apply(Control root)
    {
        var p = Current;
        if (root is Form form)
        {
            form.BackColor = p.Back;
            form.ForeColor = p.Text;
            if (form.IsHandleCreated) SetDarkTitleBar(form.Handle, p.IsDark);
        }
        Walk(root, p);
    }

    public static void StyleMenuItems(ToolStripItemCollection items)
    {
        var renderer = CreateRenderer(Current);
        StyleItems(items, Current, renderer);
    }

    public static Color StatusColor(ValveStatus status) => Current.IsDark
        ? status switch
        {
            ValveStatus.InSpec => Color.FromArgb(28, 70, 44),
            ValveStatus.NeedsAdjustment => Color.FromArgb(88, 34, 34),
            ValveStatus.AwaitingVerification => Color.FromArgb(92, 72, 20),
            ValveStatus.PassVerified => Color.FromArgb(36, 100, 56),
            ValveStatus.FailVerified => Color.FromArgb(126, 38, 38),
            _ => Color.FromArgb(58, 58, 62)
        }
        : status switch
        {
            ValveStatus.InSpec => Color.FromArgb(220, 240, 224),
            ValveStatus.NeedsAdjustment => Color.FromArgb(252, 220, 220),
            ValveStatus.AwaitingVerification => Color.FromArgb(255, 240, 200),
            ValveStatus.PassVerified => Color.FromArgb(170, 225, 180),
            ValveStatus.FailVerified => Color.FromArgb(242, 160, 160),
            _ => Color.FromArgb(236, 236, 236)
        };

    public static Color StatusText() => Current.IsDark ? Color.White : Color.Black;

    /// <summary>Asks Windows to draw the title bar dark. Ignored on systems that don't support it.</summary>
    public static void SetDarkTitleBar(IntPtr handle, bool dark)
    {
        try
        {
            int value = dark ? 1 : 0;
            DwmSetWindowAttribute(handle, 20, ref value, sizeof(int)); // DWMWA_USE_IMMERSIVE_DARK_MODE
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private static void Walk(Control parent, Palette p)
    {
        foreach (Control c in parent.Controls)
        {
            switch (c)
            {
                case DataGridView grid:
                    StyleGrid(grid, p);
                    break;
                case MenuStrip menu:
                    StyleMenu(menu, p);
                    break;
                case Button button:
                    button.FlatStyle = FlatStyle.Flat;
                    button.BackColor = p.Button;
                    button.ForeColor = p.Text;
                    button.FlatAppearance.BorderColor = p.Border;
                    break;
                case TextBox or NumericUpDown or ListBox:
                    c.BackColor = p.Input;
                    c.ForeColor = p.Text;
                    break;
                case Label label:
                    label.ForeColor = Equals(label.Tag, MutedTag) ? p.Muted : p.Text;
                    break;
                case Panel:
                    c.BackColor = p.Back;
                    break;
            }

            if (c.HasChildren && c is not DataGridView && c is not MenuStrip)
            {
                Walk(c, p);
            }
        }
    }

    private static void StyleGrid(DataGridView g, Palette p)
    {
        g.BackgroundColor = p.Back;
        g.GridColor = p.Grid;
        g.EnableHeadersVisualStyles = false;
        g.DefaultCellStyle.BackColor = p.Surface;
        g.DefaultCellStyle.ForeColor = p.Text;
        g.DefaultCellStyle.SelectionBackColor = p.Selection;
        g.DefaultCellStyle.SelectionForeColor = p.Text;
        g.ColumnHeadersDefaultCellStyle.BackColor = p.Header;
        g.ColumnHeadersDefaultCellStyle.ForeColor = p.HeaderText;
        g.ColumnHeadersDefaultCellStyle.SelectionBackColor = p.Header;
        g.ColumnHeadersDefaultCellStyle.SelectionForeColor = p.HeaderText;
    }

    private static void StyleMenu(MenuStrip menu, Palette p)
    {
        var renderer = CreateRenderer(p);
        menu.BackColor = p.Back;
        menu.ForeColor = p.Text;
        menu.Renderer = renderer;
        StyleItems(menu.Items, p, renderer);
    }

    private static void StyleItems(ToolStripItemCollection items, Palette p, ToolStripRenderer renderer)
    {
        foreach (ToolStripItem item in items)
        {
            item.BackColor = p.Surface;
            item.ForeColor = p.Text;

            if (item is ToolStripDropDownItem dropDownItem)
            {
                dropDownItem.DropDown.Renderer = renderer;
                dropDownItem.DropDown.BackColor = p.Surface;
                StyleItems(dropDownItem.DropDownItems, p, renderer);
            }
        }
    }

    private static ToolStripRenderer CreateRenderer(Palette p) =>
        new ToolStripProfessionalRenderer(new ThemedColorTable(p)) { RoundedEdges = false };

    private sealed class ThemedColorTable : ProfessionalColorTable
    {
        private readonly Palette _p;

        public ThemedColorTable(Palette p)
        {
            _p = p;
            UseSystemColors = false; // a settable property on the base class, not an overridable member
        }

        public override Color MenuItemSelected => _p.Selection;
        public override Color MenuItemSelectedGradientBegin => _p.Selection;
        public override Color MenuItemSelectedGradientEnd => _p.Selection;
        public override Color MenuItemBorder => _p.Border;
        public override Color MenuBorder => _p.Border;
        public override Color MenuItemPressedGradientBegin => _p.Surface;
        public override Color MenuItemPressedGradientMiddle => _p.Surface;
        public override Color MenuItemPressedGradientEnd => _p.Surface;
        public override Color ToolStripDropDownBackground => _p.Surface;
        public override Color ImageMarginGradientBegin => _p.Surface;
        public override Color ImageMarginGradientMiddle => _p.Surface;
        public override Color ImageMarginGradientEnd => _p.Surface;
        public override Color MenuStripGradientBegin => _p.Back;
        public override Color MenuStripGradientEnd => _p.Back;
        public override Color ToolStripBorder => _p.Border;
        public override Color SeparatorDark => _p.Border;
        public override Color SeparatorLight => _p.Surface;
    }
}
