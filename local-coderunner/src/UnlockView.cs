using System;
using System.Windows.Forms;

namespace LocalCodeRunner
{
    // Start password, then student number, then the student confirms it is them.
    class UnlockView : Stack
    {
        readonly MainForm form;
        readonly Label title, subtitle, step, hint, error, name, nameNote, gccLine;
        readonly Field input;
        readonly FlatButton go, start;
        Session session;
        Found found;
        bool unlocked;

        public UnlockView(MainForm form)
        {
            this.form = form;
            ColumnWidth = 440;
            title = Add(Theme.Text(form.Pack.Title, Theme.UiSemibold(20f), Theme.Ink));
            subtitle = Add(Theme.Text("Data Structures and Algorithms", Theme.Ui(11f), Theme.Muted), 2);
            step = Add(Theme.Text("Start password", Theme.UiSemibold(11f), Theme.Plum), 34);
            input = Add(new Field(Theme.Mono(16f)) { Height = Theme.Px(46) }, 6);
            input.Box.CharacterCasing = CharacterCasing.Upper;
            input.Box.MaxLength = 32;
            hint = Add(Theme.Text("Your proctor writes it on the board when the exam starts.", Theme.Ui(10f), Theme.Muted), 6);
            error = Add(Theme.Text("", Theme.Ui(10f), Theme.Sienna), 8);
            error.Visible = false;
            go = Add(new FlatButton { Text = "Unlock", Width = Theme.Px(150), Height = Theme.Px(40) }, 16, auto: true);

            name = Add(Theme.Text("", Theme.UiSemibold(17f), Theme.Ink), 28);
            nameNote = Add(Theme.Text("", Theme.Ui(10f), Theme.Muted), 4);
            start = Add(new FlatButton { Width = Theme.Px(260), Height = Theme.Px(42) }, 16, auto: true);
            name.Visible = nameNote.Visible = start.Visible = false;

            var version = Engine.GccVersion();
            gccLine = Add(Theme.Text(version == null
                    ? "gcc was not found on this PC. Raise your hand before you start."
                    : "gcc " + version + " is ready on this PC.",
                Theme.Ui(9f), version == null ? Theme.Sienna : Theme.Muted), 40);

            go.Click += (s, e) => Submit();
            input.Box.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Submit(); }
            };
            input.Box.TextChanged += (s, e) =>
            {
                error.Visible = false;
                if (unlocked && session != null)
                {
                    session = null;
                    name.Visible = nameNote.Visible = start.Visible = false;
                    go.Outline = false;
                    go.Invalidate();
                    PerformLayout();
                }
            };
            start.Click += (s, e) => Start();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            BeginInvoke((Action)(() => input.Box.Focus()));
        }

        void Fail(string message)
        {
            error.Text = message;
            error.Visible = true;
            PerformLayout();
            input.Box.Focus();
            input.Box.SelectAll();
        }

        void Submit()
        {
            if (!unlocked) Unlock();
            else FindStudent();
        }

        void Unlock()
        {
            var code = input.Box.Text;
            if (Crypto.NormalizeCode(code).Length < 6) { Fail("Type the whole password from the board."); return; }
            go.Enabled = false;
            go.Text = "Unlocking...";
            form.Background(() => form.Pack.Open(code), plain =>
            {
                go.Enabled = true;
                go.Text = "Unlock";
                if (plain == null) { Fail("That is not the password. Check each character against the board."); return; }
                form.Bundle = Bundle.Parse(Json.Parse(plain));
                unlocked = true;
                step.Text = "Student number";
                hint.Text = "As it appears on your school ID, and on the front page you signed.";
                go.Text = "Find me";
                input.Box.CharacterCasing = CharacterCasing.Normal;
                input.Box.Font = Theme.Mono(14f);
                input.Box.Text = "";
                error.Visible = false;
                PerformLayout();
                input.Box.Focus();
            }, () => { go.Enabled = true; go.Text = "Unlock"; });
        }

        void FindStudent()
        {
            var student = form.Bundle.FindStudent(input.Box.Text);
            if (student == null) { Fail("That student number is not on this exam's class list. Check it, or raise your hand."); return; }
            session = Session.Open(form.Bundle, student, out found);
            name.Text = student.Name;
            var exam = form.Bundle.Exam;
            switch (found)
            {
                case Found.New:
                    nameNote.Text = "If this is not you, fix the student number above. Your " + exam.Minutes +
                                    " minutes start when you press the button, and keep running even if this window closes.";
                    start.Text = "Start my " + exam.Minutes + " minutes";
                    break;
                case Found.Resume:
                    if (session.Submitted)
                    {
                        nameNote.Text = "You have already submitted. Your receipt is on the next screen.";
                        start.Text = "Show my receipt";
                    }
                    else
                    {
                        double left = session.LeftOnThisPc();
                        nameNote.Text = "Your exam is saved on this PC, with " + Theme.Clock(left) + " left on your clock.";
                        start.Text = left > 0 ? "Continue my exam" : "Finish my exam";
                    }
                    break;
                case Found.Moved:
                    nameNote.Text = "Your exam was started on " + session.State.Machine + ". A proctor confirms the move to this PC.";
                    start.Text = "Continue here";
                    break;
                case Found.Missing:
                case Found.Damaged:
                    nameNote.Text = "This PC remembers your exam, but its save file is " + (found == Found.Missing ? "missing" : "damaged") +
                                    ". A proctor confirms starting again; your clock carries on from your first start.";
                    start.Text = "Start again";
                    break;
            }
            name.Visible = nameNote.Visible = start.Visible = true;
            error.Visible = false;
            PerformLayout();
            // One primary at a time: Find me steps back until the number changes.
            go.Outline = true;
            go.Invalidate();
            start.Focus();
        }

        void Start()
        {
            if (session == null) return;
            if (found == Found.Moved || found == Found.Missing || found == Found.Damaged)
            {
                using (var dlg = new ProctorDialog(form, session, nameNote.Text, gateOnly: true))
                    if (dlg.ShowDialog(form) != DialogResult.OK) return;
            }
            if (found == Found.Resume && session.Submitted) { }
            else if (found == Found.Resume) session.Resume(moved: false);
            else if (found == Found.Moved) session.Resume(moved: true);
            else session.Begin();
            form.StartExam(session);
        }
    }
}
