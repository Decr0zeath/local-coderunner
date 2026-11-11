using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows.Forms;

namespace LocalCodeRunner
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            // No "has stopped working" dialog for a crashing answer: child
            // processes inherit this error mode.
            SetErrorMode(0x0001 | 0x0002);

            if (args.Length == 3 && args[0] == "--selftest") return SelfTest(args[1], args[2]);
            if (args.Length == 3 && args[0] == "--verify") return Verify(args[1], args[2]);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Pack pack = null;
            try { pack = Pack.ReadFrom(Application.ExecutablePath); } catch (Exception) { }
            if (pack == null)
            {
                MessageBox.Show("This copy of Local CodeRunner holds no exam. Download the exam's own copy again.",
                    "Local CodeRunner", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
            Application.Run(new MainForm(pack));
            return 0;
        }

        [DllImport("kernel32.dll")]
        static extern uint SetErrorMode(uint mode);

        // pack.py's check that this exe and lcr.py agree: the same answers
        // checked both ways, and the same crypto on the same input.
        static int SelfTest(string input, string output)
        {
            var d = Json.Parse(File.ReadAllBytes(input));
            var exam = Exam.Parse(Json.Obj(d["exam"]));
            var results = new List<object>();
            foreach (var c in Json.List(d["cases"]).Select(Json.Obj))
            {
                var q = exam.Questions.First(x => x.Number == Convert.ToInt32(c["q"]));
                var r = Engine.Check(exam, q.Pick((string)c["v"]), (string)c["answer"]);
                results.Add(new Dictionary<string, object>
                {
                    ["outcome"] = r.Outcome, ["fraction"] = Math.Round(r.Fraction, 9),
                    ["passed"] = r.Tests.Select(t => t.Passed).ToList(),
                });
            }
            var k = Json.Obj(d["crypto"]);
            var salt = Convert.FromBase64String((string)k["salt"]);
            var data = Convert.FromBase64String((string)k["data"]);
            var key = Crypto.Derive((string)k["code"], salt, Convert.ToInt32(k["iterations"]), 64);
            var crypto = new Dictionary<string, object>
            {
                ["normalized"] = Crypto.NormalizeCode((string)k["code"]),
                ["key"] = Crypto.Hex(key),
                ["stream"] = Crypto.Hex(Crypto.KeystreamXor(key.Take(32).ToArray(), Convert.FromBase64String((string)k["nonce"]), data)),
                ["hmac"] = Crypto.Hex(Crypto.Hmac(key.Skip(32).ToArray(), data, salt)),
                ["receipt"] = Crypto.Receipt(data),
                ["penalties"] = Enumerable.Range(0, 40).Select(i => Session.PenaltyFor((string)k["regime"], i)).ToList(),
                ["marks"] = Json.List(k["marks"]).Select(Json.List).Select(m => m.Select(Convert.ToDouble).ToArray())
                    .Select(m => Math.Round(Session.MarkFor(m[0], m[1], m[2]), 9)).ToList(),
            };
            File.WriteAllBytes(output, Json.WriteBytes(new Dictionary<string, object> { ["checks"] = results, ["crypto"] = crypto }));
            return 0;
        }

        // pack.py's check that the packed exe opens with the start password.
        static int Verify(string password, string output)
        {
            var pack = Pack.ReadFrom(Application.ExecutablePath);
            var plain = pack?.Open(password);
            string digest = null, gcc = Engine.GccVersion();
            if (plain != null)
            {
                using (var sha = SHA256.Create()) digest = Crypto.Hex(sha.ComputeHash(plain));
                Bundle.Parse(Json.Parse(plain));
            }
            File.WriteAllBytes(output, Json.WriteBytes(new Dictionary<string, object>
            {
                ["title"] = pack?.Title, ["sha256"] = digest, ["gcc"] = gcc,
            }));
            return plain == null ? 1 : 0;
        }
    }
}
