using System;
using System.Drawing;
using System.Windows.Forms;

namespace LocalCodeRunner
{
    // The proctor's password, then either a yes for the step that asked for
    // it, or the tools: add minutes, reopen a submitted exam.
    class ProctorDialog : Form
    {
        readonly MainForm form;
        readonly Session session;
        readonly bool gateOnly;
        readonly Stack stack = new Stack();
        readonly Field password = new Field(Theme.Mono(14f));
        readonly Label error = Theme.Text("", Theme.Ui(10f), Theme.Sienna);
        readonly FlatButton confirm = new FlatButton { Text = "Confirm" };
        Label done;

        public ProctorDialog(MainForm form, Session session, string reason, bool gateOnly)
        {
            this.form = form;
            this.session = session;
            this.gateOnly = gateOnly;
            Text = "Proctor";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Paper;
            Font = Theme.Ui(10f);
            ClientSize = new Size(Theme.Px(480), Theme.Px(400));

            stack.ColumnWidth = 400;
            stack.Add(Theme.Text("Proctor", Theme.UiSemibold(16f), Theme.Ink));
            stack.Add(Theme.Text(session.Student.Name + "    " + session.Student.Id, Theme.Ui(10f), Theme.Muted), 2);
            stack.Add(Theme.Text(reason, Theme.Ui(10.5f), Theme.Plum), 16);
            stack.Add(Theme.Text("Proctor password", Theme.UiSemibold(10.5f), Theme.Plum), 18);
            stack.Add(password, 6).Height = Theme.Px(42);
            password.Box.CharacterCasing = CharacterCasing.Upper;
            password.Box.UseSystemPasswordChar = true;
            stack.Add(error, 6).Visible = false;
            confirm.Size = new Size(Theme.Px(130), Theme.Px(38));
            stack.Add(confirm, 14, auto: true);
            Controls.Add(stack);

            AcceptButton = confirm;
            confirm.Click += (s, e) => Verify();
            password.Box.TextChanged += (s, e) => { error.Visible = false; stack.PerformLayout(); };
            Shown += (s, e) => password.Box.Focus();
            FitHeight();
        }

        void FitHeight()
        {
            stack.PerformLayout();
            ClientSize = new Size(Theme.Px(480), stack.ContentHeight + Theme.Px(84));
        }

        void Verify()
        {
            var code = password.Box.Text;
            confirm.Enabled = false;
            confirm.Text = "Checking...";
            form.Background(() => form.Bundle.IsProctor(code), ok =>
            {
                confirm.Enabled = true;
                confirm.Text = "Confirm";
                if (!ok)
                {
                    error.Text = "That is not the proctor password.";
                    error.Visible = true;
                    stack.PerformLayout();
                    password.Box.SelectAll();
                    password.Box.Focus();
                    return;
                }
                if (gateOnly) { DialogResult = DialogResult.OK; Close(); return; }
                ShowTools();
            }, () => { confirm.Enabled = true; confirm.Text = "Confirm"; });
        }

        void ShowTools()
        {
            stack.SuspendLayout();
            for (int i = stack.Controls.Count - 1; i >= 3; i--) stack.Controls[i].Dispose();
            AcceptButton = null;

            stack.Add(Theme.Text("Add time", Theme.UiSemibold(10.5f), Theme.Plum), 18);
            var row = stack.Add(new Panel { Height = Theme.Px(38), BackColor = Theme.Paper }, 6);
            // A plain field in the course's style rather than NumericUpDown's grey spinner.
            var minutes = new Field(Theme.Mono(13f)) { Size = new Size(Theme.Px(72), Theme.Px(38)), Location = Point.Empty };
            minutes.Padding = new Padding(Theme.Px(10), Theme.Px(7), Theme.Px(6), Theme.Px(4));
            minutes.Box.Text = "10";
            minutes.Box.MaxLength = 3;
            minutes.Box.KeyPress += (s, e) => { if (!char.IsDigit(e.KeyChar) && !char.IsControl(e.KeyChar)) e.Handled = true; };
            var add = new FlatButton { Text = "Add minutes", Size = new Size(Theme.Px(140), Theme.Px(38)) };
            add.Location = new Point(Theme.Px(86), 0);
            row.Controls.Add(minutes);
            row.Controls.Add(add);

            FlatButton reopen = null;
            if (session.Submitted)
            {
                stack.Add(Theme.Text("Reopen", Theme.UiSemibold(10.5f), Theme.Plum), 20);
                stack.Add(Theme.Text("Lets the student change answers and Check again. They submit again for a new receipt.",
                    Theme.Ui(9.5f), Theme.Muted), 4);
                reopen = stack.Add(new FlatButton { Text = "Reopen the exam", Outline = true, Fill = Theme.Wine, Size = new Size(Theme.Px(170), Theme.Px(36)) }, 8, auto: true);
            }
            done = stack.Add(Theme.Text("", Theme.Ui(10f), Theme.Teal), 14);
            var close = stack.Add(new FlatButton { Text = "Done", Outline = true, Fill = Theme.Plum, Size = new Size(Theme.Px(110), Theme.Px(36)) }, 18, auto: true);
            stack.ResumeLayout(true);
            FitHeight();

            add.Click += (s, e) =>
            {
                if (!int.TryParse(minutes.Box.Text, out int m) || m < 1 || m > 120)
                {
                    done.ForeColor = Theme.Sienna;
                    done.Text = "Type a number of minutes from 1 to 120.";
                }
                else
                {
                    session.AddMinutes(m);
                    done.ForeColor = Theme.Teal;
                    done.Text = "Added " + m + " minutes. " + Theme.Clock(session.LeftOnThisPc()) + " left.";
                }
                stack.PerformLayout();
                FitHeight();
            };
            if (reopen != null)
                reopen.Click += (s, e) =>
                {
                    if (session.LeftOnThisPc() <= 0)
                    {
                        done.ForeColor = Theme.Sienna;
                        done.Text = "Add time first: the clock has run out.";
                        stack.PerformLayout();
                        return;
                    }
                    session.Reopen();
                    DialogResult = DialogResult.OK;
                    Close();
                };
            close.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };
            CancelButton = close;
        }
    }
}
