using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LocalCodeRunner
{
    class MainForm : Form
    {
        public readonly Pack Pack;
        public Bundle Bundle;
        Control view;
        ExamView exam;

        public MainForm(Pack pack)
        {
            Pack = pack;
            Text = pack.Title + " - Local CodeRunner";
            BackColor = Theme.Paper;
            ForeColor = Theme.Ink;
            Font = Theme.Ui(10f);
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(Theme.Px(1280), Theme.Px(800));
            MinimumSize = new Size(Theme.Px(1000), Theme.Px(660));
            WindowState = FormWindowState.Maximized;
            KeyPreview = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }
            Native.QuietBrowser();
            Show(new UnlockView(this));
        }

        public void Show(Control next)
        {
            SuspendLayout();
            var old = view;
            view = next;
            next.Dock = DockStyle.Fill;
            Controls.Add(next);
            if (old != null) { Controls.Remove(old); old.Dispose(); }
            exam = next as ExamView;
            ResumeLayout(true);
            next.Focus();
        }

        public void StartExam(Session s)
        {
            if (s.Submitted) Show(new ReceiptView(this, s));
            else Show(new ExamView(this, s));
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (exam != null && !exam.Session.Submitted && e.CloseReason == CloseReason.UserClosing)
            {
                if (exam.Busy)
                {
                    MessageBox.Show(this, "Wait for the Check to finish, then close.", "Local CodeRunner",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    e.Cancel = true;
                    return;
                }
                var answer = MessageBox.Show(this,
                    "Your answers are saved, but your clock keeps running while this window is closed.\n\nClose Local CodeRunner?",
                    "Close the exam window?", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                if (answer != DialogResult.Yes) { e.Cancel = true; return; }
            }
            if (exam != null)
            {
                exam.Session.Log("window closed");
                exam.SaveNow();
            }
            base.OnFormClosing(e);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (exam != null && exam.HandleKey(keyData)) return true;
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // Runs slow work (key derivation, a Check) off the window's thread.
        public void Background<T>(Func<T> work, Action<T> done, Action failed = null)
        {
            Task.Run(work).ContinueWith(t =>
            {
                if (IsDisposed) return;
                BeginInvoke((Action)(() =>
                {
                    if (t.IsFaulted)
                    {
                        MessageBox.Show(this, "Something went wrong: " + t.Exception.GetBaseException().Message +
                            "\n\nRaise your hand for the proctor.", "Local CodeRunner", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        failed?.Invoke();
                    }
                    else done(t.Result);
                }));
            });
        }
    }
}
