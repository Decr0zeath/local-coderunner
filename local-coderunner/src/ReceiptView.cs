using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace LocalCodeRunner
{
    class ReceiptView : Stack
    {
        public ReceiptView(MainForm form, Session s)
        {
            ColumnWidth = 640;
            var when = s.State.Submitted != null && s.State.Submitted.Length >= 16 ? s.State.Submitted.Substring(11, 5) : "";
            Add(Theme.Text("Submitted", Theme.UiSemibold(20f), Theme.Ink));
            Add(Theme.Text(s.Student.Name + "    " + s.Student.Id + "    " + when, Theme.Ui(11f), Theme.Muted), 2);

            Add(Theme.Text("Your receipt", Theme.UiSemibold(11f), Theme.Plum), 30);
            var code = Add(Theme.Text(s.State.Receipt ?? "", Theme.Mono(34f, FontStyle.Bold), Theme.Wine), 2);
            Add(Theme.Text("Copy this code onto your front page, in the Receipt box, before you leave your seat. " +
                           "The proctor checks it against this screen.", Theme.Ui(11f), Theme.Ink), 8);

            Add(Theme.Text("Your submission file", Theme.UiSemibold(11f), Theme.Plum), 28);
            var path = Add(new Field(Theme.Mono(10.5f)) { Height = Theme.Px(38) }, 6);
            path.Box.ReadOnly = true;
            path.Box.Text = s.ZipPath;
            Add(Theme.Text("Upload this file to the LMS assignment once the internet is back. Do not open or change it: " +
                           "the receipt would stop matching.", Theme.Ui(10f), Theme.Muted), 6);

            Add(Theme.Text("Your marks", Theme.UiSemibold(11f), Theme.Plum), 28);
            Add(new MarksList(s), 6);
            Add(Theme.Text("These are the marks this PC worked out. Your teacher reruns every Check when grading.",
                Theme.Ui(9.5f), Theme.Muted), 8);

            var proctor = Add(new LinkLabel
            {
                Text = "Proctor", AutoSize = true, Font = Theme.Ui(9.5f), LinkColor = Theme.Muted, ActiveLinkColor = Theme.Wine,
                LinkBehavior = LinkBehavior.HoverUnderline,
            }, 34, auto: true);
            proctor.LinkClicked += (o, e) =>
            {
                using (var dlg = new ProctorDialog(form, s, "Reopen this exam, or add time first.", gateOnly: false))
                    dlg.ShowDialog(form);
                if (!s.Submitted) form.StartExam(s);
            };
        }

    }

    // One row per question and a total under a rule, drawn rather than laid
    // out so the column's width decides everything.
    class MarksList : Control
    {
        readonly Session s;
        int Row => Theme.Px(30);

        public MarksList(Session s)
        {
            this.s = s;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Paper;
            Height = Row * (s.Exam.Questions.Count + 1) + Theme.Px(10);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            using (var ui = Theme.Ui(11f))
            using (var bold = Theme.UiSemibold(11f))
            using (var mono = Theme.Mono(11.5f))
            using (var monoBold = Theme.Mono(11.5f, FontStyle.Bold))
            {
                int y = 0, valueWidth = TextRenderer.MeasureText("00.00 / 00.00", monoBold).Width;
                var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
                foreach (var q in s.Exam.Questions)
                {
                    TextRenderer.DrawText(g, "Question " + q.Number + "   " + q.Title, ui,
                        new Rectangle(0, y, Width - valueWidth - Theme.Px(16), Row), Theme.Plum, flags | TextFormatFlags.EndEllipsis);
                    TextRenderer.DrawText(g, Theme.Num(s.Best(q.Number)) + " / " + Theme.Num(q.Points), mono,
                        new Rectangle(0, y, Width, Row), Theme.Ink, flags | TextFormatFlags.Right);
                    y += Row;
                }
                y += Theme.Px(6);
                using (var p = new Pen(Theme.Rule)) g.DrawLine(p, 0, y - Theme.Px(2), Width, y - Theme.Px(2));
                TextRenderer.DrawText(g, "Total", bold, new Rectangle(0, y, Width, Row), Theme.Ink, flags);
                TextRenderer.DrawText(g, Theme.Num(s.Total) + " / " + Theme.Num(s.Exam.TotalPoints), monoBold,
                    new Rectangle(0, y, Width, Row), Theme.Ink, flags | TextFormatFlags.Right);
            }
        }
    }
}
