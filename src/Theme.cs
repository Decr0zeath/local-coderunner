using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LocalCodeRunner
{
    // The course's question card (build-pracex.py's CARD and PANEL) with the
    // Lost Century palette from css/shell.css. Html.cs repeats these values in CSS.
    static class Theme
    {
        public static readonly Color Paper = Hex("#faf7f1");
        public static readonly Color Panel = Hex("#fffdf8");
        public static readonly Color Rule = Hex("#c9bfa8");
        public static readonly Color Ink = Hex("#1a1816");
        public static readonly Color Plum = Hex("#4b3d44");
        public static readonly Color Muted = Hex("#6b665e");
        public static readonly Color Wine = Hex("#79444a");
        public static readonly Color Sienna = Hex("#ae5d40");
        public static readonly Color Teal = Hex("#4b726e");
        public static readonly Color TealDeep = Hex("#3c5c58");
        public static readonly Color Sand = Hex("#d1b187");
        public static readonly Color Parchment = Hex("#e9e2cc");

        public static Font Ui(float size, FontStyle style = FontStyle.Regular) => new Font("Segoe UI", size, style);
        public static Font UiSemibold(float size) => new Font("Segoe UI Semibold", size);
        public static Font Mono(float size, FontStyle style = FontStyle.Regular) => new Font("Consolas", size, style);

        static Color Hex(string s) => ColorTranslator.FromHtml(s);

        static float scale;
        // Layout sizes below are written for 96 dpi and scaled here; fonts in
        // points scale on their own. The HTML panes do not scale, so Html.cs
        // zooms them by the same factor.
        public static float Scale
        {
            get
            {
                if (scale == 0) using (var g = Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96f;
                return scale;
            }
        }

        public static int Px(int v) => (int)Math.Round(v * Scale);

        public static Label Text(string text, Font font, Color color) =>
            new Label { Text = text, Font = font, ForeColor = color, AutoSize = true, BackColor = Color.Transparent, UseMnemonic = false };

        public static string Clock(double seconds)
        {
            var t = TimeSpan.FromSeconds(Math.Max(0, Math.Ceiling(seconds)));
            return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes:00}:{t.Seconds:00}";
        }

        public static string Num(double x) => x.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        public static string Pct(double x) => x.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "%";
    }

    // A flat button drawn from the palette, so its disabled and focused states
    // are the course's colours rather than Windows grey.
    class FlatButton : Button
    {
        public Color Fill = Theme.Teal, FillHover = Theme.TealDeep, TextColor = Theme.Paper;
        public bool Outline;
        bool hover, down;

        public FlatButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Font = Theme.UiSemibold(10.5f);
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent?.BackColor ?? Theme.Paper);
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fill, text, border;
            if (!Enabled) { fill = Outline ? Theme.Paper : Theme.Parchment; text = Theme.Muted; border = Theme.Rule; }
            else if (Outline) { fill = hover || down ? Theme.Parchment : Theme.Paper; text = Fill; border = Fill; }
            else { fill = down ? Theme.Ink : hover ? FillHover : Fill; text = TextColor; border = fill; }
            using (var path = Round(r, 4))
            {
                using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                using (var p = new Pen(border)) g.DrawPath(p, path);
            }
            if (Focused && ShowFocusCues && Enabled)
                using (var p = new Pen(Outline ? Fill : Theme.Paper, 1)) using (var path = Round(new Rectangle(3, 3, Width - 7, Height - 7), 3)) g.DrawPath(p, path);
            TextRenderer.DrawText(g, Text, Font, r, text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }

        public static GraphicsPath Round(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    // A text field on a panel with a one-pixel rule, focus shown in sienna.
    class Field : Panel
    {
        public readonly TextBox Box = new TextBox();

        public Field(Font font, bool mono = false)
        {
            BackColor = Theme.Panel;
            Padding = new Padding(10, 8, 10, 6);
            Box.BorderStyle = BorderStyle.None;
            Box.BackColor = Theme.Panel;
            Box.ForeColor = Theme.Ink;
            Box.Font = font;
            Box.Dock = DockStyle.Fill;
            Controls.Add(Box);
            Box.GotFocus += (s, e) => Invalidate();
            Box.LostFocus += (s, e) => Invalidate();
            SetStyle(ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            bool focus = Box.Focused;
            using (var p = new Pen(focus ? Theme.Sienna : Theme.Rule, focus ? 2 : 1))
                e.Graphics.DrawRectangle(p, focus ? 1 : 0, focus ? 1 : 0, Width - (focus ? 2 : 1), Height - (focus ? 2 : 1));
        }
    }

    static class Native
    {
        // The IE engine behind WebBrowser clicks on every navigation, and a
        // results pane navigates on every Check.
        public static void QuietBrowser()
        {
            try { CoInternetSetFeatureEnabled(21, 2, true); } catch (Exception) { }
            try
            {
                var exe = System.IO.Path.GetFileName(Application.ExecutablePath);
                using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
                    @"Software\Microsoft\Internet Explorer\Main\FeatureControl\FEATURE_BROWSER_EMULATION"))
                    k.SetValue(exe, 11001, Microsoft.Win32.RegistryValueKind.DWord);
            }
            catch (Exception) { }
        }

        [DllImport("urlmon.dll")]
        [return: MarshalAs(UnmanagedType.Error)]
        static extern int CoInternetSetFeatureEnabled(int feature, int flags, bool enable);

        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, int[] lParam);
    }
}
