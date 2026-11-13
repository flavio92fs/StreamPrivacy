using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace StreamPrivacy
{
    internal static class Theme
    {
        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

        internal static Color Background(bool dark) { return dark ? Color.FromArgb(17, 24, 39) : Color.FromArgb(246, 248, 251); }
        internal static Color Surface(bool dark) { return dark ? Color.FromArgb(28, 40, 59) : Color.White; }
        internal static Color Raised(bool dark) { return dark ? Color.FromArgb(37, 52, 76) : Color.FromArgb(235, 240, 247); }
        internal static Color Foreground(bool dark) { return dark ? Color.FromArgb(237, 243, 251) : Color.FromArgb(28, 39, 55); }
        internal static Color Muted(bool dark) { return dark ? Color.FromArgb(167, 183, 205) : Color.FromArgb(85, 95, 110); }
        internal static Color Border(bool dark) { return dark ? Color.FromArgb(53, 72, 98) : Color.FromArgb(190, 200, 215); }
        internal static Color Primary { get { return Color.FromArgb(42, 103, 211); } }

        internal static void Apply(Control root, bool dark)
        {
            ApplyControl(root, dark);
            Form form = root as Form;
            if (form != null) ApplyTitleBar(form, dark);
            root.Invalidate(true);
        }

        private static void ApplyControl(Control control, bool dark)
        {
            control.BackColor = Background(dark);
            control.ForeColor = Foreground(dark);
            Button button = control as Button;
            if (button != null)
            {
                ThemedButton themed = button as ThemedButton;
                if (themed != null) themed.DarkTheme = dark;
                bool primary = String.Equals(button.Tag as string, "primary", StringComparison.Ordinal);
                button.BackColor = primary ? Primary : Surface(dark);
                button.ForeColor = primary ? Color.White : Foreground(dark);
                button.FlatAppearance.BorderColor = primary ? Primary : Border(dark);
                button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(56, 124, 235) : Raised(dark);
                button.FlatAppearance.MouseDownBackColor = primary ? Color.FromArgb(31, 83, 177) : Border(dark);
            }
            ListView list = control as ListView;
            if (list != null)
            {
                list.BackColor = Surface(dark);
                BufferedListView buffered = list as BufferedListView;
                if (buffered != null) { buffered.DarkTheme = dark; buffered.Invalidate(); }
            }
            ThemedTabControl tabs = control as ThemedTabControl;
            if (tabs != null) tabs.DarkTheme = dark;
            Label label = control as Label;
            if (label != null && label.Font.Size < 10) label.ForeColor = Muted(dark);
            foreach (Control child in control.Controls) ApplyControl(child, dark);
        }

        internal static void ApplyTitleBar(Form form, bool dark)
        {
            if (!form.IsHandleCreated) return;
            try
            {
                int enabled = dark ? 1 : 0;
                // Documented DWMWA_USE_IMMERSIVE_DARK_MODE; unsupported systems keep their normal frame.
                DwmSetWindowAttribute(form.Handle, 20, ref enabled, sizeof(int));
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        internal static void SetState(Label label, bool dark, bool error)
        {
            label.BackColor = dark
                ? (error ? Color.FromArgb(76, 43, 40) : Color.FromArgb(27, 52, 77))
                : (error ? Color.FromArgb(255, 233, 219) : Color.FromArgb(228, 236, 247));
            label.ForeColor = dark
                ? (error ? Color.FromArgb(255, 220, 204) : Color.FromArgb(219, 235, 255))
                : Foreground(false);
        }

        internal static void ApplyMenu(ContextMenuStrip menu, bool dark)
        {
            if (menu == null) return;
            menu.BackColor = Surface(dark); menu.ForeColor = Foreground(dark);
            menu.Renderer = new MenuRenderer(dark);
            foreach (ToolStripItem item in menu.Items) item.ForeColor = Foreground(dark);
        }

        private sealed class MenuRenderer : ToolStripProfessionalRenderer
        {
            private readonly bool dark;
            internal MenuRenderer(bool dark) : base(new MenuColors(dark)) { this.dark = dark; }
            protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs args)
            {
                Rectangle box = args.ImageRectangle;
                using (Pen check = new Pen(Foreground(dark), 2))
                    args.Graphics.DrawLines(check, new Point[] {
                        new Point(box.Left + 3, box.Top + box.Height / 2),
                        new Point(box.Left + box.Width / 2 - 1, box.Bottom - 4),
                        new Point(box.Right - 3, box.Top + 4)
                    });
            }
        }

        private sealed class MenuColors : ProfessionalColorTable
        {
            private readonly bool dark;
            internal MenuColors(bool dark) { this.dark = dark; UseSystemColors = false; }
            public override Color ToolStripDropDownBackground { get { return Surface(dark); } }
            public override Color ImageMarginGradientBegin { get { return Surface(dark); } }
            public override Color ImageMarginGradientMiddle { get { return Surface(dark); } }
            public override Color ImageMarginGradientEnd { get { return Surface(dark); } }
            public override Color MenuItemSelected { get { return Raised(dark); } }
            public override Color MenuItemBorder { get { return Border(dark); } }
            public override Color MenuBorder { get { return Border(dark); } }
            public override Color SeparatorDark { get { return Border(dark); } }
            public override Color SeparatorLight { get { return Surface(dark); } }
            public override Color CheckBackground { get { return Raised(dark); } }
            public override Color CheckSelectedBackground { get { return Raised(dark); } }
        }
    }

    internal sealed class ThemedButton : Button
    {
        internal bool DarkTheme;
        internal ThemedButton()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        }
        protected override void OnPaint(PaintEventArgs args)
        {
            if (Enabled) { base.OnPaint(args); return; }
            // WinForms otherwise forces dark system text for disabled flat buttons,
            // ignoring ForeColor and making labels unreadable on a dark surface.
            Color background = Theme.Raised(DarkTheme);
            using (Brush fill = new SolidBrush(background)) args.Graphics.FillRectangle(fill, ClientRectangle);
            Rectangle border = ClientRectangle; border.Width--; border.Height--;
            using (Pen outline = new Pen(Theme.Border(DarkTheme))) args.Graphics.DrawRectangle(outline, border);
            Rectangle text = ClientRectangle; text.Inflate(-6, -2);
            TextRenderer.DrawText(args.Graphics, Text, Font, text, Theme.Muted(DarkTheme),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    internal sealed class ThemedTabControl : TabControl
    {
        internal bool DarkTheme;
        internal ThemedTabControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Padding = new Point(14, 6);
            SelectedIndexChanged += delegate { Invalidate(); };
        }
        protected override void OnPaint(PaintEventArgs args)
        {
            args.Graphics.Clear(Theme.Background(DarkTheme));
            Rectangle frame = DisplayRectangle; frame.Inflate(1, 1);
            using (Pen border = new Pen(Theme.Border(DarkTheme))) args.Graphics.DrawRectangle(border, frame);
            for (int index = 0; index < TabCount; index++)
            {
                Rectangle tab = GetTabRect(index);
                using (Brush fill = new SolidBrush(index == SelectedIndex ? Theme.Surface(DarkTheme) : Theme.Background(DarkTheme)))
                    args.Graphics.FillRectangle(fill, tab);
                if (index == SelectedIndex)
                    using (Pen accent = new Pen(Color.FromArgb(51, 205, 185), 3))
                        args.Graphics.DrawLine(accent, tab.Left + 8, tab.Bottom - 2, tab.Right - 8, tab.Bottom - 2);
                TextRenderer.DrawText(args.Graphics, TabPages[index].Text, Font, tab,
                    index == SelectedIndex ? Theme.Foreground(DarkTheme) : Theme.Muted(DarkTheme),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
        }
    }
}
