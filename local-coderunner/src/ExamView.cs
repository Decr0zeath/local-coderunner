using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace LocalCodeRunner
{
    class ExamView : Panel
    {
        public readonly Session Session;
        readonly MainForm form;
        readonly HeaderBar header;
        readonly QuestionTabs tabs;
        readonly SplitContainer split = new SplitContainer(), rightSplit = new SplitContainer();
        readonly WebBrowser statement = Browser(), results = Browser();
        readonly CodeEditor editor = new CodeEditor();
        readonly FlatButton check = new FlatButton { Text = "Check" };
        readonly Label checkNote = Theme.Text("", Theme.Ui(10f), Theme.Muted);
        readonly Label best = Theme.Text("", Theme.Mono(11f), Theme.Plum);
        readonly Timer tick = new Timer { Interval = 250 }, saveSoon = new Timer { Interval = 1500 };
        readonly Dictionary<int, string> notCounted = new Dictionary<int, string>();
        int current = -1, ticks;
        bool loading, submitting, laidOut;

        public bool Busy { get; private set; }
        Question Q => Session.Exam.Questions[current];

        public ExamView(MainForm form, Session session)
        {
            this.form = form;
            Session = session;
            BackColor = Theme.Paper;
            header = new HeaderBar(session) { Dock = DockStyle.Top, Height = Theme.Px(66) };
            tabs = new QuestionTabs(session) { Dock = DockStyle.Top, Height = Theme.Px(62) };

            split.Dock = DockStyle.Fill;
            split.BackColor = Theme.Paper;
            split.SplitterWidth = Theme.Px(9);
            split.Panel1.BackColor = split.Panel2.BackColor = Theme.Paper;
            split.Panel1.Controls.Add(statement);
            split.Paint += (s, e) => PaintSplitter(e.Graphics, split.SplitterRectangle, true);

            var right = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Paper, Padding = new Padding(Theme.Px(14), Theme.Px(14), Theme.Px(22), 0) };
            var answerBar = new Panel { Dock = DockStyle.Top, Height = Theme.Px(34), BackColor = Theme.Paper };
            var answerTitle = Theme.Text("Your answer", Theme.UiSemibold(11.5f), Theme.Ink);
            var answerHint = Theme.Text("Paste your function here. A main() of your own is set aside.", Theme.Ui(9.5f), Theme.Muted);
            answerBar.Controls.Add(answerTitle);
            answerBar.Controls.Add(answerHint);
            answerBar.Layout += (s, e) =>
            {
                answerTitle.Location = new Point(0, Theme.Px(2));
                answerHint.Location = new Point(answerTitle.Right + Theme.Px(12), answerTitle.Bottom - answerHint.Height - Theme.Px(1));
            };

            rightSplit.Dock = DockStyle.Fill;
            rightSplit.Orientation = Orientation.Horizontal;
            rightSplit.BackColor = Theme.Paper;
            rightSplit.SplitterWidth = Theme.Px(9);
            rightSplit.Panel1.BackColor = rightSplit.Panel2.BackColor = Theme.Paper;
            rightSplit.Paint += (s, e) => PaintSplitter(e.Graphics, rightSplit.SplitterRectangle, false);
            editor.Dock = DockStyle.Fill;
            rightSplit.Panel1.Controls.Add(editor);

            var checkBar = new Panel { Dock = DockStyle.Top, Height = Theme.Px(56), BackColor = Theme.Paper };
            check.Size = new Size(Theme.Px(132), Theme.Px(38));
            checkBar.Controls.Add(check);
            checkBar.Controls.Add(checkNote);
            checkBar.Controls.Add(best);
            checkBar.Layout += (s, e) =>
            {
                check.Location = new Point(0, Theme.Px(6));
                checkNote.Location = new Point(check.Right + Theme.Px(14), check.Top + (check.Height - checkNote.Height) / 2);
                best.Location = new Point(checkBar.Width - best.Width, check.Top + (check.Height - best.Height) / 2);
            };
            checkBar.Paint += (s, e) =>
            {
                using (var p = new Pen(Theme.Rule)) e.Graphics.DrawLine(p, 0, checkBar.Height - 1, checkBar.Width, checkBar.Height - 1);
            };
            rightSplit.Panel2.Controls.Add(results);
            rightSplit.Panel2.Controls.Add(checkBar);

            right.Controls.Add(rightSplit);
            right.Controls.Add(answerBar);
            split.Panel2.Controls.Add(right);

            Controls.Add(split);
            Controls.Add(tabs);
            Controls.Add(header);

            tabs.Picked += i => { if (!Busy) ShowQuestion(i); };
            header.SubmitClicked += () => DoSubmit(false);
            header.ProctorClicked += OpenProctor;
            check.Click += (s, e) => DoCheck();
            editor.Box.TextChanged += (s, e) =>
            {
                if (loading) return;
                Session.SetAnswer(Q.Number, editor.Answer);
                notCounted.Remove(Q.Number);
                saveSoon.Stop();
                saveSoon.Start();
                UpdateCheckBar();
            };
            results.Navigating += (s, e) =>
            {
                if (e.Url.Scheme == "lcr" && e.Url.Host == "line" && int.TryParse(e.Url.AbsolutePath.Trim('/'), out int line))
                    editor.GoToLine(line);
            };
            saveSoon.Tick += (s, e) => { saveSoon.Stop(); SaveNow(); };
            tick.Tick += (s, e) => OnTick();

            ShowQuestion(0);
            OnTick();
            tick.Start();
        }

        // The splitters keep their proportions as the window changes size; a
        // student who drags one sets new proportions.
        double across = 0.45, down = 0.5;
        bool placing, dragged;

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (!laidOut && rightSplit.Height > Theme.Px(240))
            {
                laidOut = true;
                split.SplitterMoved += (s, a) => { if (!placing && split.Width > 0) across = (double)split.SplitterDistance / split.Width; };
                rightSplit.SplitterMoved += (s, a) =>
                {
                    if (placing || rightSplit.Height <= 0) return;
                    down = (double)rightSplit.SplitterDistance / rightSplit.Height;
                    dragged = true;
                };
                split.SizeChanged += (s, a) => PlaceSplitters();
                rightSplit.SizeChanged += (s, a) => PlaceSplitters();
            }
            PlaceSplitters();
        }

        void PlaceSplitters()
        {
            placing = true;
            try
            {
                if (split.Width > Theme.Px(400)) split.SplitterDistance = (int)(split.Width * across);
                // Until the student drags it, a short screen gives the results
                // the larger share once there is a result to show.
                bool result = current >= 0 && (Session.ChecksFor(Q.Number).Any() || notCounted.ContainsKey(Q.Number));
                double d = dragged ? down : result && rightSplit.Height < Theme.Px(640) ? 0.42 : 0.5;
                if (rightSplit.Height > Theme.Px(240)) rightSplit.SplitterDistance = (int)(rightSplit.Height * d);
            }
            catch (InvalidOperationException) { }
            finally { placing = false; }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { tick.Dispose(); saveSoon.Dispose(); }
            base.Dispose(disposing);
        }

        // A rule, with a short thick grip at its middle to say it can be dragged.
        static void PaintSplitter(Graphics g, Rectangle r, bool vertical)
        {
            int cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2, len = Theme.Px(36), w = Theme.Px(4);
            using (var p = new Pen(Theme.Rule))
                if (vertical) g.DrawLine(p, cx, r.Y, cx, r.Bottom);
                else g.DrawLine(p, r.X, cy, r.Right, cy);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var grip = vertical ? new Rectangle(cx - w / 2, cy - len / 2, w, len) : new Rectangle(cx - len / 2, cy - w / 2, len, w);
            using (var b = new SolidBrush(Theme.Rule)) using (var path = FlatButton.Round(grip, w / 2)) g.FillPath(b, path);
        }

        static WebBrowser Browser()
        {
            var b = new WebBrowser
            {
                Dock = DockStyle.Fill, ScriptErrorsSuppressed = true, IsWebBrowserContextMenuEnabled = false,
                AllowWebBrowserDrop = false, WebBrowserShortcutsEnabled = false,
            };
            b.Navigating += (s, e) => { if (e.Url.ToString() != "about:blank") e.Cancel = true; };
            return b;
        }

        public void SaveNow()
        {
            if (!loading && current >= 0 && !Session.Submitted) Session.SetAnswer(Q.Number, editor.Answer);
            Session.Save();
        }

        // ---- showing a question -----------------------------------------

        void ShowQuestion(int i)
        {
            if (current >= 0 && !Session.Submitted) Session.SetAnswer(Q.Number, editor.Answer);
            current = i;
            tabs.Selected = i;
            var v = Session.VariantFor(Q);
            statement.DocumentText = Html.Statement(Q, v);
            loading = true;
            editor.Answer = Session.State.Answers[Q.Number];
            loading = false;
            editor.Box.Select(0, 0);
            ShowLastResult();
            UpdateCheckBar();
        }

        void ShowLastResult()
        {
            var last = Session.ChecksFor(Q.Number).LastOrDefault();
            if (notCounted.TryGetValue(Q.Number, out var why)) results.DocumentText = Html.Note("Not counted", why);
            else results.DocumentText = last == null ? Html.NoCheckYet(Session, Q) : Html.Result(Session, Q, last);
            if (laidOut) PlaceSplitters();
        }

        void UpdateCheckBar()
        {
            var q = Q;
            int n = Session.ChecksFor(q.Number).Count() + 1;
            double pen = Session.NextPenalty(q.Number);
            bool can = !Busy && Session.CanCheck(q);
            check.Enabled = can;
            check.Text = Busy ? "Checking..." : "Check";
            if (Session.Submitted) checkNote.Text = "Submitted.";
            else if (Busy) checkNote.Text = "Compiling and running every test.";
            else if (Session.Unanswered(q)) checkNote.Text = "Write or paste your answer first.";
            else if (!Session.ChangedSinceCheck(q)) checkNote.Text = "No change since Check " + (n - 1) + ".";
            else checkNote.Text = (pen == 0 ? "Check " + n + " is free." : "Check " + n + " costs " + Theme.Pct(pen) + " of " + Theme.Num(q.Points) + " points.")
                                  + "   Ctrl+Enter";
            checkNote.ForeColor = can && pen > 0 ? Theme.Sienna : Theme.Muted;
            best.Text = "Best " + Theme.Num(Session.Best(q.Number)) + " / " + Theme.Num(q.Points);
            check.Parent?.PerformLayout();
            header.SetBusy(Busy || submitting);
            tabs.Enabled = !Busy;
            tabs.Invalidate();
        }

        // ---- Check and Submit ---------------------------------------------

        void DoCheck()
        {
            var q = Q;
            if (!Session.CanCheck(q) || Busy) return;
            Session.SetAnswer(q.Number, editor.Answer);
            Busy = true;
            UpdateCheckBar();
            results.DocumentText = Html.Working(Session.ChecksFor(q.Number).Count() + 1);
            form.Background(() => Session.Check(q), r =>
            {
                Busy = false;
                var (result, rec) = r;
                if (rec == null) notCounted[q.Number] = result.Message;
                if (Q == q) ShowLastResult();
                UpdateCheckBar();
                if (Session.LeftOnThisPc() <= 0) OnTick();
            }, () => { Busy = false; UpdateCheckBar(); });
        }

        void DoSubmit(bool timeUp)
        {
            if (Session.Submitted || submitting) return;
            if (!timeUp)
            {
                var sb = new StringBuilder("You still have " + Theme.Clock(Session.LeftOnThisPc()) + " on the clock.\n\n");
                foreach (var q in Session.Exam.Questions)
                {
                    sb.Append("Question ").Append(q.Number).Append(": ");
                    if (Session.CanCheck(q))
                        sb.Append("changed since its last Check, so it is graded once more, as Check ")
                          .Append(Session.ChecksFor(q.Number).Count() + 1).Append(" (").Append(Theme.Pct(Session.NextPenalty(q.Number))).Append(" penalty).");
                    else if (!Session.ChecksFor(q.Number).Any()) sb.Append("no answer, so it scores 0.");
                    else sb.Append("your best Check stands, ").Append(Theme.Num(Session.Best(q.Number))).Append(" / ").Append(Theme.Num(q.Points)).Append('.');
                    sb.Append("\n\n");
                }
                sb.Append("After this you cannot change any answer. Submit now?");
                if (MessageBox.Show(form, sb.ToString(), "Submit all and finish?", MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            }
            Session.SetAnswer(Q.Number, editor.Answer);
            submitting = Busy = true;
            editor.ReadOnly = true;
            UpdateCheckBar();
            results.DocumentText = Html.Note(timeUp ? "Time is up" : "Submitting",
                "Grading every answer changed since its last Check, then writing your submission file. Stay at this PC.");
            form.Background(() => { Session.Submit(timeUp); return true; },
                _ => form.Show(new ReceiptView(form, Session)),
                () => { submitting = Busy = false; editor.ReadOnly = false; UpdateCheckBar(); });
        }

        void OnTick()
        {
            double left = Session.Tick();
            header.SetTime(left);
            if (left <= 0 && !Session.Submitted && !Busy && !submitting) { DoSubmit(true); return; }
            if (++ticks % 40 == 0 && !Busy) SaveNow();
        }

        void OpenProctor()
        {
            using (var dlg = new ProctorDialog(form, Session, "Add time to this student's clock.", gateOnly: false))
                dlg.ShowDialog(form);
            OnTick();
            UpdateCheckBar();
        }

        public bool HandleKey(Keys key)
        {
            if (Busy || submitting) return false;
            if (key == (Keys.Control | Keys.Enter)) { if (check.Enabled) DoCheck(); return true; }
            for (int i = 0; i < Session.Exam.Questions.Count && i < 9; i++)
                if (key == (Keys.Control | (Keys.D1 + i))) { ShowQuestion(i); return true; }
            return false;
        }
    }

    class HeaderBar : Panel
    {
        readonly Label name, sub, clock, clockLabel;
        readonly LinkLabel proctor;
        readonly FlatButton submit;
        public event Action SubmitClicked, ProctorClicked;

        public HeaderBar(Session s)
        {
            BackColor = Theme.Paper;
            SetStyle(ControlStyles.ResizeRedraw, true);
            name = Theme.Text(s.Student.Name, Theme.UiSemibold(12.5f), Theme.Ink);
            sub = Theme.Text(s.Student.Id + "   " + s.Bundle.Title, Theme.Ui(9.5f), Theme.Muted);
            clockLabel = Theme.Text("Time left", Theme.Ui(9f), Theme.Muted);
            clock = Theme.Text("00:00", Theme.Mono(22f, FontStyle.Bold), Theme.Ink);
            proctor = new LinkLabel
            {
                Text = "Proctor", AutoSize = true, Font = Theme.Ui(9.5f), LinkColor = Theme.Muted, ActiveLinkColor = Theme.Wine,
                LinkBehavior = LinkBehavior.HoverUnderline, BackColor = Color.Transparent,
            };
            submit = new FlatButton { Text = "Submit", Outline = true, Fill = Theme.Wine, Size = new Size(Theme.Px(116), Theme.Px(38)) };
            Controls.AddRange(new Control[] { name, sub, clockLabel, clock, proctor, submit });
            submit.Click += (o, e) => SubmitClicked?.Invoke();
            proctor.LinkClicked += (o, e) => ProctorClicked?.Invoke();
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int pad = Theme.Px(22);
            name.Location = new Point(pad, Theme.Px(10));
            sub.Location = new Point(pad + Theme.Px(1), name.Bottom);
            submit.Location = new Point(Width - pad - submit.Width, (Height - submit.Height) / 2);
            proctor.Location = new Point(submit.Left - Theme.Px(20) - proctor.Width, (Height - proctor.Height) / 2);
            int cx = Width / 2;
            clock.Location = new Point(cx - clock.Width / 2, Theme.Px(19));
            clockLabel.Location = new Point(cx - clockLabel.Width / 2, Theme.Px(4));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var p = new Pen(Theme.Rule)) e.Graphics.DrawLine(p, 0, Height - 1, Width, Height - 1);
        }

        public void SetTime(double left)
        {
            var text = Theme.Clock(left);
            if (clock.Text != text)
            {
                clock.Text = text;
                clock.ForeColor = left <= 120 ? Theme.Wine : left <= 600 ? Theme.Sienna : Theme.Ink;
                clockLabel.Text = left <= 120 ? "Submits by itself at 00:00" : "Time left";
                PerformLayout();
            }
        }

        public void SetBusy(bool busy) => submit.Enabled = !busy;
    }

    // The three questions as tabs: number, title, and the best mark so far.
    class QuestionTabs : Control
    {
        readonly Session session;
        int selected, hover = -1;
        public event Action<int> Picked;

        public QuestionTabs(Session s)
        {
            session = s;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Parchment;
        }

        public int Selected { get => selected; set { selected = value; Invalidate(); } }
        int Count => session.Exam.Questions.Count;

        Rectangle TabRect(int i)
        {
            int pad = Theme.Px(22), gap = Theme.Px(2);
            int w = Math.Min(Theme.Px(420), (Width - 2 * pad - gap * (Count - 1)) / Count);
            return new Rectangle(pad + i * (w + gap), Theme.Px(8), w, Height - Theme.Px(8));
        }

        int At(Point p)
        {
            for (int i = 0; i < Count; i++) if (TabRect(i).Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = At(e.Location);
            Cursor = h >= 0 && h != selected && Enabled ? Cursors.Hand : Cursors.Default;
            if (h != hover) { hover = h; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            int i = At(e.Location);
            if (i >= 0 && i != selected && Enabled) Picked?.Invoke(i);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Parchment);
            using (var rule = new Pen(Theme.Rule)) g.DrawLine(rule, 0, Height - 1, Width, Height - 1);
            using (var qFont = Theme.Mono(11f, FontStyle.Bold))
            using (var tFont = Theme.UiSemibold(10.5f))
            using (var sFont = Theme.Ui(9f))
            {
                for (int i = 0; i < Count; i++)
                {
                    var q = session.Exam.Questions[i];
                    var r = TabRect(i);
                    bool sel = i == selected;
                    var fill = sel ? Theme.Paper : i == hover && Enabled ? Color.FromArgb(0xf3, 0xee, 0xe0) : Theme.Parchment;
                    using (var b = new SolidBrush(fill)) g.FillRectangle(b, r);
                    if (sel)
                    {
                        using (var p = new Pen(Theme.Rule))
                        {
                            g.DrawLine(p, r.Left, r.Top, r.Right - 1, r.Top);
                            g.DrawLine(p, r.Left, r.Top, r.Left, r.Bottom);
                            g.DrawLine(p, r.Right - 1, r.Top, r.Right - 1, r.Bottom);
                        }
                        using (var b = new SolidBrush(Theme.Paper)) g.FillRectangle(b, r.Left + 1, r.Bottom - 1, r.Width - 2, 2);
                        using (var b = new SolidBrush(Theme.Wine)) g.FillRectangle(b, r.Left, r.Top, r.Width, Theme.Px(3));
                    }
                    int x = r.Left + Theme.Px(14), y = r.Top + Theme.Px(8);
                    var qText = "Q" + q.Number;
                    var qSize = TextRenderer.MeasureText(g, qText, qFont, Size.Empty, TextFormatFlags.NoPadding);
                    TextRenderer.DrawText(g, qText, qFont, new Point(x, y + Theme.Px(1)), sel ? Theme.Wine : Theme.Plum, TextFormatFlags.NoPadding);
                    TextRenderer.DrawText(g, q.Title, tFont, new Rectangle(x + qSize.Width + Theme.Px(8), y, r.Right - x - qSize.Width - Theme.Px(20), Theme.Px(22)),
                        sel ? Theme.Ink : Theme.Plum, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                    int checks = session.ChecksFor(q.Number).Count();
                    const string dot = "  ·  ";
                    string status = Theme.Num(q.Points).Replace(".00", "") + " points" + dot + (checks == 0
                        ? "no Check yet"
                        : "best " + Theme.Num(session.Best(q.Number)) + dot + checks + (checks == 1 ? " Check" : " Checks"));
                    if (!session.Submitted && session.CanCheck(q) && checks > 0) status += dot + "edited since";
                    // Muted only on the paper tab: on parchment it falls under 4.5:1.
                    TextRenderer.DrawText(g, status, sFont, new Rectangle(x, y + Theme.Px(24), r.Width - Theme.Px(28), Theme.Px(18)),
                        sel ? Theme.Muted : Theme.Plum, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                }
            }
        }
    }
}
