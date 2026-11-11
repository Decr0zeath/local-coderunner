using System;
using System.Drawing;
using System.Windows.Forms;

namespace LocalCodeRunner
{
    // The answer box: a plain Edit control with line numbers, so gcc's "line 5"
    // can be found, and pasting from any IDE keeps its line breaks.
    class CodeEditor : Panel
    {
        public readonly CodeBox Box = new CodeBox();
        readonly Gutter gutter;
        readonly Timer poll = new Timer { Interval = 60 };
        int lastFirst = -1, lastCount = -1;

        public CodeEditor()
        {
            BackColor = Theme.Panel;
            Padding = new Padding(1);
            gutter = new Gutter(this) { Dock = DockStyle.Left };
            Box.Dock = DockStyle.Fill;
            Controls.Add(Box);
            Controls.Add(gutter);
            Box.TextChanged += (s, e) => gutter.Invalidate();
            poll.Tick += (s, e) =>
            {
                if (!Box.IsHandleCreated) return;
                int first = FirstVisibleLine, count = LineCount;
                if (first != lastFirst || count != lastCount)
                {
                    lastFirst = first; lastCount = count;
                    gutter.Width = gutter.WidthFor(count);
                    gutter.Invalidate();
                }
            };
            poll.Start();
            SetStyle(ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var p = new Pen(Box.Focused ? Theme.Sienna : Theme.Rule)) e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) poll.Dispose();
            base.Dispose(disposing);
        }

        public string Answer
        {
            get => Box.Text.Replace("\r\n", "\n");
            set => Box.Text = Engine.CleanAnswer(value).Replace("\n", "\r\n");
        }

        public bool ReadOnly
        {
            get => Box.ReadOnly;
            set { Box.ReadOnly = value; Box.BackColor = Theme.Panel; }
        }

        int FirstVisibleLine => (int)Native.SendMessage(Box.Handle, 0xCE, IntPtr.Zero, IntPtr.Zero);
        int LineCount => Box.GetLineFromCharIndex(Box.TextLength) + 1;

        public void GoToLine(int line)
        {
            int start = Box.GetFirstCharIndexFromLine(Math.Max(0, line - 1));
            if (start < 0) return;
            int end = Box.Text.IndexOf("\r\n", start, StringComparison.Ordinal);
            Box.Focus();
            Box.Select(start, (end < 0 ? Box.TextLength : end) - start);
            Box.ScrollToCaret();
        }

        class Gutter : Control
        {
            readonly CodeEditor editor;

            public Gutter(CodeEditor editor)
            {
                this.editor = editor;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
                BackColor = Theme.Paper;
                Font = editor.Box.Font;
                Width = WidthFor(1);
            }

            public int WidthFor(int lines)
            {
                int digits = Math.Max(2, lines.ToString().Length);
                return TextRenderer.MeasureText(new string('0', digits), Font).Width + 14;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var box = editor.Box;
                e.Graphics.Clear(BackColor);
                using (var p = new Pen(Theme.Rule)) e.Graphics.DrawLine(p, Width - 1, 0, Width - 1, Height);
                if (!box.IsHandleCreated) return;
                int first = editor.FirstVisibleLine, count = editor.LineCount;
                int lineHeight = box.Font.Height;
                int firstChar = box.GetFirstCharIndexFromLine(first);
                int top = firstChar >= 0 && firstChar < box.TextLength ? box.GetPositionFromCharIndex(firstChar).Y : 1;
                int caretLine = box.GetLineFromCharIndex(box.SelectionStart);
                for (int line = first; line < count; line++)
                {
                    int y = top + (line - first) * lineHeight;
                    if (y > Height) break;
                    var color = line == caretLine && box.Focused ? Theme.Wine : Theme.Muted;
                    TextRenderer.DrawText(e.Graphics, (line + 1).ToString(), Font,
                        new Rectangle(0, y, Width - 8, lineHeight), color, TextFormatFlags.Right | TextFormatFlags.NoPadding);
                }
            }
        }
    }

    class CodeBox : TextBox
    {
        public CodeBox()
        {
            Multiline = true;
            AcceptsTab = true;
            AcceptsReturn = true;
            WordWrap = false;
            ScrollBars = ScrollBars.Both;
            BorderStyle = BorderStyle.None;
            MaxLength = 200000;
            Font = Theme.Mono(11f);
            BackColor = Theme.Panel;
            ForeColor = Theme.Ink;
            HideSelection = false;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // Tab stops every four characters (16 dialog units).
            Native.SendMessage(Handle, 0xCB, (IntPtr)1, new[] { 16 });
            // A little air between the line numbers and the code.
            Native.SendMessage(Handle, 0xD3, (IntPtr)3, (IntPtr)(8 | (8 << 16)));
        }

        protected override void WndProc(ref Message m)
        {
            // An editor's clipboard may hold bare \n line ends, which an Edit
            // control shows as one long line.
            if (m.Msg == 0x302 && !ReadOnly && Clipboard.ContainsText())
            {
                SelectedText = Engine.CleanAnswer(Clipboard.GetText()).Replace("\n", "\r\n");
                return;
            }
            base.WndProc(ref m);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Enter && !ReadOnly)
            {
                // Keep the current line's indentation.
                var text = Text;
                int line = GetLineFromCharIndex(SelectionStart);
                int start = GetFirstCharIndexFromLine(line);
                int i = start;
                while (i < text.Length && (text[i] == ' ' || text[i] == '\t') && i < SelectionStart) i++;
                SelectedText = "\r\n" + text.Substring(start, i - start);
                return true;
            }
            if (keyData == (Keys.Control | Keys.A)) { SelectAll(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
