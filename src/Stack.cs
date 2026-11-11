using System.Drawing;
using System.Windows.Forms;

namespace LocalCodeRunner
{
    // A centred column: children stack top to bottom in the order added, each
    // the column's width unless it is a wrapping label or tagged "auto".
    class Stack : Panel
    {
        public int ColumnWidth = 460;
        public int ContentHeight { get; private set; }

        public Stack()
        {
            BackColor = Theme.Paper;
            Dock = DockStyle.Fill;
            AutoScroll = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        }

        // Our own order: WinForms reorders Controls when a hidden child is shown.
        readonly System.Collections.Generic.List<Control> order = new System.Collections.Generic.List<Control>();

        public T Add<T>(T c, int gapAbove = 0, bool auto = false) where T : Control
        {
            c.Margin = new Padding(0, Theme.Px(gapAbove), 0, 0);
            if (auto) c.Tag = "auto";
            order.Add(c);
            Controls.Add(c);
            return c;
        }

        protected override void OnControlRemoved(ControlEventArgs e)
        {
            order.Remove(e.Control);
            base.OnControlRemoved(e);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            int width = Theme.Px(ColumnWidth);
            int y = 0;
            foreach (Control c in order)
            {
                if (!c.Visible) continue;
                y += c.Margin.Top;
                if (c is Label l && l.AutoSize) l.MaximumSize = new Size(width, 0);
                else if (!"auto".Equals(c.Tag)) c.Width = width;
                y += c.Height;
            }
            ContentHeight = y;
            int left = System.Math.Max(Theme.Px(24), (ClientSize.Width - width) / 2);
            int top = System.Math.Max(Theme.Px(32), (ClientSize.Height - ContentHeight) / 2 - Theme.Px(30));
            y = top;
            foreach (Control c in order)
            {
                if (!c.Visible) continue;
                y += c.Margin.Top;
                c.Location = new Point(left - Lead(c) + AutoScrollPosition.X, y + AutoScrollPosition.Y);
                y += c.Height;
            }
            AutoScrollMinSize = new Size(width + Theme.Px(48), y + Theme.Px(32));
        }

        // GDI draws a label's text a sixth of an em in from its edge; pulling
        // the label left by that much puts its ink on the same edge as the
        // fields and buttons, whatever the font size.
        static int Lead(Control c)
        {
            if (!(c is Label l)) return 0;
            return (int)System.Math.Round(l.Font.SizeInPoints * Theme.Scale * 96f / 72f / 6f);
        }
    }
}
