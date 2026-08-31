using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows.Forms;

namespace VPin.Inspector.UI;

/// <summary>
/// Centralized dark color palette and helpers for applying a dark theme to a
/// WinForms control tree. Kept in one place so every front-end surface uses the
/// same colors and severity accents stay readable on a dark background.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class DarkTheme
{
    /// <summary>Window / root background.</summary>
    public static readonly Color Background = Color.FromArgb(32, 32, 32);

    /// <summary>Input and content surfaces (text boxes, trees, log panes).</summary>
    public static readonly Color Surface = Color.FromArgb(45, 45, 48);

    /// <summary>Slightly raised surfaces such as buttons.</summary>
    public static readonly Color Control = Color.FromArgb(60, 60, 64);

    /// <summary>Primary text color.</summary>
    public static readonly Color Foreground = Color.FromArgb(220, 220, 220);

    /// <summary>Borders and separators.</summary>
    public static readonly Color Border = Color.FromArgb(80, 80, 84);

    /// <summary>Link / accent color.</summary>
    public static readonly Color Accent = Color.FromArgb(86, 156, 214);

    // Severity accents tuned to remain legible on a dark background.
    public static readonly Color Error = Color.FromArgb(240, 105, 105);
    public static readonly Color Warning = Color.FromArgb(226, 184, 92);

    private const int DwmwaUseImmersiveDarkMode = 20;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(
        IntPtr hWnd, string? pszSubAppName, string? pszSubIdList);

    /// <summary>
    /// Applies the dark palette to <paramref name="form"/> and, recursively, to
    /// every child control. Also switches the title bar to dark mode.
    /// </summary>
    public static void Apply(Form form)
    {
        form.BackColor = Background;
        form.ForeColor = Foreground;

        ApplyToChildren(form.Controls);
        UseDarkTitleBar(form);
    }

    private static void ApplyToChildren(Control.ControlCollection controls)
    {
        foreach (Control control in controls)
        {
            ApplyToControl(control);
            ApplyToChildren(control.Controls);
        }
    }

    private static void ApplyToControl(Control control)
    {
        switch (control)
        {
            case TextBox textBox:
                textBox.BackColor = Surface;
                textBox.ForeColor = Foreground;
                textBox.BorderStyle = BorderStyle.FixedSingle;
                if (textBox.Multiline)
                {
                    UseDarkScrollBars(textBox);
                }
                break;

            case NumericUpDown numeric:
                numeric.BackColor = Surface;
                numeric.ForeColor = Foreground;
                break;

            case RichTextBox richTextBox:
                richTextBox.BackColor = Surface;
                richTextBox.ForeColor = Foreground;
                richTextBox.BorderStyle = BorderStyle.None;
                UseDarkScrollBars(richTextBox);
                break;

            case TreeView tree:
                tree.BackColor = Surface;
                tree.ForeColor = Foreground;
                tree.BorderStyle = BorderStyle.FixedSingle;
                tree.LineColor = Border;
                UseDarkScrollBars(tree);
                break;

            case Button button:
                button.BackColor = Control;
                button.ForeColor = Foreground;
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderColor = Border;
                break;

            case LinkLabel link:
                link.BackColor = Color.Transparent;
                link.LinkColor = Accent;
                link.ActiveLinkColor = Accent;
                link.VisitedLinkColor = Accent;
                break;

            case MenuStrip menu:
                menu.BackColor = Surface;
                menu.ForeColor = Foreground;
                menu.Renderer = new ToolStripProfessionalRenderer(new DarkColorTable());
                ApplyToMenuItems(menu.Items);
                break;

            case ProgressBar:
                // ProgressBar renders via the OS theme; leave as-is.
                break;

            case Label label:
                label.BackColor = Color.Transparent;
                // Preserve intentionally colored labels (e.g. severity legend).
                if (label.ForeColor == SystemColors.ControlText)
                {
                    label.ForeColor = Foreground;
                }
                break;

            default:
                control.BackColor = Background;
                control.ForeColor = Foreground;
                break;
        }
    }

    private static void ApplyToMenuItems(ToolStripItemCollection items)
    {
        foreach (ToolStripItem item in items)
        {
            item.BackColor = Surface;
            item.ForeColor = Foreground;
            if (item is ToolStripMenuItem menuItem)
            {
                ApplyToMenuItems(menuItem.DropDownItems);
            }
        }
    }

    private static void UseDarkTitleBar(Form form)
    {
        try
        {
            int useDark = 1;
            DwmSetWindowAttribute(
                form.Handle, DwmwaUseImmersiveDarkMode, ref useDark, sizeof(int));
        }
        catch
        {
            // Title bar theming is best-effort; ignore on unsupported systems.
        }
    }

    /// <summary>
    /// Opts a control's window into the OS dark mode so its native (Win32)
    /// scroll bars render dark instead of the default light theme. Managed
    /// BackColor/ForeColor do not affect these non-client scroll bars, so this
    /// SetWindowTheme call is required. Re-applied on handle recreation.
    /// </summary>
    private static void UseDarkScrollBars(Control control)
    {
        void Apply()
        {
            try
            {
                SetWindowTheme(control.Handle, "DarkMode_Explorer", null);
            }
            catch
            {
                // Scroll bar theming is best-effort; ignore on unsupported systems.
            }
        }

        if (control.IsHandleCreated)
        {
            Apply();
        }

        // The theme is bound to the window handle, so re-apply whenever the
        // handle is (re)created.
        control.HandleCreated += (_, _) => Apply();
    }

    /// <summary>Dark color table for menu strip rendering.</summary>
    private sealed class DarkColorTable : ProfessionalColorTable
    {
        public override Color MenuItemSelected => Control;
        public override Color MenuItemSelectedGradientBegin => Control;
        public override Color MenuItemSelectedGradientEnd => Control;
        public override Color MenuItemBorder => Border;
        public override Color MenuBorder => Border;
        public override Color MenuItemPressedGradientBegin => Surface;
        public override Color MenuItemPressedGradientEnd => Surface;
        public override Color ToolStripDropDownBackground => Surface;
        public override Color ImageMarginGradientBegin => Surface;
        public override Color ImageMarginGradientMiddle => Surface;
        public override Color ImageMarginGradientEnd => Surface;
        public override Color MenuStripGradientBegin => Surface;
        public override Color MenuStripGradientEnd => Surface;
        public override Color SeparatorDark => Border;
        public override Color SeparatorLight => Border;
    }
}
