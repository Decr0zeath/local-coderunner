using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Microsoft.Win32;

namespace LocalCodeRunner
{
    class CheckRecord
    {
        public int N, Q;
        public string Variant, At, Code, Outcome, Message;
        public double Elapsed, Fraction, Penalty, Mark;
        public List<bool> Passed = new List<bool>();
        public bool AtSubmit;
        // What the shown tests printed, so a reopened exe can show the last result.
        public List<Dictionary<string, object>> Shown = new List<Dictionary<string, object>>();

        public Dictionary<string, object> ToJson() => new Dictionary<string, object>
        {
            ["n"] = N, ["q"] = Q, ["variant"] = Variant, ["at"] = At, ["elapsed"] = Math.Round(Elapsed, 1),
            ["code"] = Code, ["outcome"] = Outcome, ["message"] = Message, ["passed"] = Passed,
            ["fraction"] = Fraction, ["penalty"] = Penalty, ["mark"] = Mark, ["at_submit"] = AtSubmit,
            ["shown"] = Shown,
        };

        public static CheckRecord FromJson(Dictionary<string, object> d) => new CheckRecord
        {
            N = Convert.ToInt32(d["n"]), Q = Convert.ToInt32(d["q"]), Variant = (string)d["variant"],
            At = (string)d["at"], Elapsed = Json.Num(d, "elapsed"), Code = (string)d["code"],
            Outcome = (string)d["outcome"], Message = Json.Str(d, "message") ?? "",
            Passed = Json.List(d["passed"]).Select(Convert.ToBoolean).ToList(),
            Fraction = Json.Num(d, "fraction"), Penalty = Json.Num(d, "penalty"), Mark = Json.Num(d, "mark"),
            AtSubmit = d.TryGetValue("at_submit", out var s) && Convert.ToBoolean(s),
            Shown = d.TryGetValue("shown", out var sh) ? Json.List(sh).Select(Json.Obj).ToList() : new List<Dictionary<string, object>>(),
        };
    }

    class State
    {
        public string Pack, Exam, Id, Name, Started, Deadline, Machine, Status = "open", Submitted, Receipt;
        public double Remaining, Allowed;
        public int Seq;
        public Dictionary<int, string> Draw = new Dictionary<int, string>();
        public Dictionary<int, string> Answers = new Dictionary<int, string>();
        public List<CheckRecord> Checks = new List<CheckRecord>();
        public List<Dictionary<string, object>> Events = new List<Dictionary<string, object>>();

        // The receipt is a fingerprint of this, so it is left out here and kept apart.
        public Dictionary<string, object> ToJson() => new Dictionary<string, object>
        {
            ["format"] = 1, ["pack"] = Pack, ["exam"] = Exam, ["id"] = Id, ["name"] = Name,
            ["draw"] = Draw.ToDictionary(kv => kv.Key.ToString(), kv => (object)kv.Value),
            ["started"] = Started, ["deadline"] = Deadline, ["remaining"] = Math.Round(Remaining, 1),
            ["allowed"] = Allowed, ["machine"] = Machine, ["status"] = Status, ["submitted"] = Submitted,
            ["seq"] = Seq,
            ["answers"] = Answers.ToDictionary(kv => kv.Key.ToString(), kv => (object)kv.Value),
            ["checks"] = Checks.Select(c => c.ToJson()).ToList(),
            ["events"] = Events,
        };

        public static State FromJson(Dictionary<string, object> d)
        {
            var s = new State
            {
                Pack = (string)d["pack"], Exam = (string)d["exam"], Id = (string)d["id"], Name = (string)d["name"],
                Started = (string)d["started"], Deadline = (string)d["deadline"],
                Remaining = Json.Num(d, "remaining"), Allowed = Json.Num(d, "allowed"),
                Machine = (string)d["machine"], Status = (string)d["status"], Submitted = Json.Str(d, "submitted"),
                Seq = Convert.ToInt32(d["seq"]), Receipt = Json.Str(d, "receipt"),
            };
            foreach (var kv in Json.Obj(d["draw"])) s.Draw[int.Parse(kv.Key)] = (string)kv.Value;
            foreach (var kv in Json.Obj(d["answers"])) s.Answers[int.Parse(kv.Key)] = (string)kv.Value;
            s.Checks = Json.List(d["checks"]).Select(c => CheckRecord.FromJson(Json.Obj(c))).ToList();
            s.Events = Json.List(d["events"]).Select(Json.Obj).ToList();
            return s;
        }
    }

    enum Found { New, Resume, Moved, Missing, Damaged }

    class Session
    {
        public readonly Bundle Bundle;
        public readonly Student Student;
        public State State;
        readonly Stopwatch clock = Stopwatch.StartNew();
        double lastTick;

        Session(Bundle b, Student s) { Bundle = b; Student = s; }

        public Exam Exam => Bundle.Exam;
        public bool Submitted => State.Status == "submitted";
        public Variant VariantFor(Question q) => q.Pick(State.Draw[q.Number]);

        // ---- where things are kept ------------------------------------

        public string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Bundle.Exam.Code);
        public string SavePath => Path.Combine(Folder, Bundle.Exam.Code + "-" + Safe(Student.Id) + ".save");
        public string ZipPath => Path.Combine(Folder, Bundle.Exam.Code + "-" + Safe(Student.Id) + ".zip");
        string BackupPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LocalCodeRunner", Bundle.PackId, Safe(Student.Id) + ".save");
        string MarkerKey => @"Software\LocalCodeRunner\" + Bundle.PackId + @"\" + Safe(Student.Id);

        static string Safe(string id) => new string(id.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());

        static string Now() => DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);
        static string Utc(DateTime t) => t.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
        static DateTime ParseUtc(string s) => DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();

        // ---- opening an exam ------------------------------------------

        public static Session Open(Bundle b, Student s, out Found found)
        {
            var session = new Session(b, s);
            State best = null;
            bool damaged = false;
            foreach (var path in new[] { session.SavePath, session.BackupPath })
            {
                if (!File.Exists(path)) continue;
                var st = session.ReadSave(path);
                if (st == null) { damaged = true; continue; }
                if (best == null || st.Seq > best.Seq) best = st;
            }
            if (best != null)
            {
                session.State = best;
                found = best.Machine == Environment.MachineName ? Found.Resume : Found.Moved;
            }
            else if (damaged) found = Found.Damaged;
            else if (session.ReadMarker() != null) found = Found.Missing;
            else found = Found.New;
            return session;
        }

        public void Begin()
        {
            var now = DateTime.UtcNow;
            var marker = ReadMarker();
            State = new State
            {
                Pack = Bundle.PackId, Exam = Exam.Code, Id = Student.Id, Name = Student.Name,
                Draw = new Dictionary<int, string>(Student.Draw), Machine = Environment.MachineName,
                Allowed = Exam.Minutes * 60.0,
            };
            if (marker != null)
            {
                // A lost save: the clock carries on from the first start.
                State.Started = marker[0];
                State.Deadline = marker[1];
                State.Remaining = Math.Max(0, (ParseUtc(marker[1]) - now).TotalSeconds);
            }
            else
            {
                State.Started = Utc(now);
                State.Deadline = Utc(now.AddSeconds(State.Allowed));
                State.Remaining = State.Allowed;
                WriteMarker();
            }
            foreach (var q in Exam.Questions) State.Answers[q.Number] = VariantFor(q).Preload;
            Log(marker == null ? "started" : "started again after a lost save, with the proctor's password");
            lastTick = clock.Elapsed.TotalSeconds;
            Save();
        }

        public void Resume(bool moved)
        {
            var now = DateTime.UtcNow;
            if (moved)
            {
                Log("moved here from " + State.Machine + ", with the proctor's password");
                State.Machine = Environment.MachineName;
                State.Deadline = Utc(now.AddSeconds(State.Remaining));
                WriteMarker();
            }
            else
            {
                State.Remaining = Math.Min(State.Remaining, (ParseUtc(State.Deadline) - now).TotalSeconds);
                Log("reopened");
            }
            lastTick = clock.Elapsed.TotalSeconds;
            Save();
        }

        public void Log(string what, string detail = null)
        {
            var e = new Dictionary<string, object> { ["at"] = Now(), ["what"] = what, ["machine"] = Environment.MachineName };
            if (detail != null) e["detail"] = detail;
            State.Events.Add(e);
        }

        // ---- the clock --------------------------------------------------

        // The stopwatch cannot be wound back, the deadline follows the PC's
        // clock; whichever leaves less time wins.
        public double Tick()
        {
            double t = clock.Elapsed.TotalSeconds;
            if (!Submitted)
                State.Remaining = Math.Min(State.Remaining - (t - lastTick), (ParseUtc(State.Deadline) - DateTime.UtcNow).TotalSeconds);
            lastTick = t;
            return Math.Max(0, State.Remaining);
        }

        public double LeftOnThisPc() =>
            Math.Max(0, Math.Min(State.Remaining, (ParseUtc(State.Deadline) - DateTime.UtcNow).TotalSeconds));

        public void AddMinutes(int minutes)
        {
            State.Remaining = LeftOnThisPc() + minutes * 60;
            lastTick = clock.Elapsed.TotalSeconds;
            State.Allowed += minutes * 60;
            State.Deadline = Utc(DateTime.UtcNow.AddSeconds(State.Remaining));
            Log("proctor added " + minutes + " minutes");
            WriteMarker();
            Save();
        }

        public void Reopen()
        {
            State.Status = "open";
            State.Receipt = null;
            State.Remaining = LeftOnThisPc();
            lastTick = clock.Elapsed.TotalSeconds;
            Log("proctor reopened the submitted exam");
            Save();
        }

        // ---- answers, Checks and marks ----------------------------------

        public void SetAnswer(int q, string text) => State.Answers[q] = Engine.CleanAnswer(text);

        public IEnumerable<CheckRecord> ChecksFor(int q) => State.Checks.Where(c => c.Q == q);

        public double NextPenalty(int q) => PenaltyFor(Exam.Penalty, ChecksFor(q).Count());

        public double Best(int q) => ChecksFor(q).Select(c => c.Mark).DefaultIfEmpty(0).Max();

        public double Total => Exam.Questions.Sum(q => Best(q.Number));

        public bool Unanswered(Question q)
        {
            var a = State.Answers[q.Number];
            return string.IsNullOrWhiteSpace(a) || Squash(a) == Squash(VariantFor(q).Preload);
        }

        static string Squash(string s) => new string(s.Where(c => !char.IsWhiteSpace(c)).ToArray());

        public bool ChangedSinceCheck(Question q)
        {
            var last = ChecksFor(q.Number).LastOrDefault();
            return last == null || last.Code != State.Answers[q.Number];
        }

        public bool CanCheck(Question q) => !Submitted && !Unanswered(q) && ChangedSinceCheck(q);

        public static double PenaltyFor(string regime, int previous)
        {
            var parts = regime.Split(',').Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
            bool more = parts.Count > 0 && parts[parts.Count - 1] == "...";
            var values = parts.Where(p => p != "...").Select(p => double.Parse(p, CultureInfo.InvariantCulture)).ToList();
            if (previous < values.Count) return Math.Min(values[previous], 100.0);
            double step = more && values.Count > 1 ? values[values.Count - 1] - values[values.Count - 2] : 0.0;
            return Math.Min(values[values.Count - 1] + step * (previous - values.Count + 1), 100.0);
        }

        public static double MarkFor(double points, double fraction, double penalty) =>
            points * Math.Max(0.0, fraction - penalty / 100.0);

        // Runs on a worker thread; the caller keeps the window responsive.
        public (CheckResult, CheckRecord) Check(Question q, bool atSubmit = false)
        {
            var v = VariantFor(q);
            var code = State.Answers[q.Number];
            var result = Engine.Check(Exam, v, code);
            if (result.Outcome == "nogcc") return (result, null);
            double penalty = NextPenalty(q.Number);
            var rec = new CheckRecord
            {
                N = State.Checks.Count + 1, Q = q.Number, Variant = v.Id, At = Now(),
                Elapsed = State.Allowed - Math.Max(0, State.Remaining), Code = code, Outcome = result.Outcome,
                Message = Trim(result.Message, 8000), Passed = result.Tests.Select(t => t.Passed).ToList(),
                Fraction = result.Fraction, Penalty = penalty, AtSubmit = atSubmit,
                Mark = MarkFor(q.Points, result.Fraction, penalty),
            };
            for (int i = 0; i < v.Tests.Count && result.Outcome == "ran"; i++)
                if (v.Tests[i].Shown)
                    rec.Shown.Add(new Dictionary<string, object>
                    {
                        ["i"] = i, ["got"] = Trim(result.Tests[i].Got, 2000), ["trouble"] = result.Tests[i].Trouble,
                    });
            lock (State) State.Checks.Add(rec);
            Save();
            return (result, rec);
        }

        static string Trim(string s, int max) => s == null ? "" : s.Length <= max ? s : s.Substring(0, max) + "\n...";

        // ---- submitting ---------------------------------------------------

        // Answers changed since their last Check are graded as one more Check,
        // as Moodle's "Submit all and finish" does.
        public void Submit(bool timeUp)
        {
            foreach (var q in Exam.Questions)
                if (CanCheck(q)) Check(q, atSubmit: true);
            State.Status = "submitted";
            State.Submitted = Now();
            Log(timeUp ? "submitted when time ran out" : "submitted");
            WriteZip();
            Save();
        }

        void WriteZip()
        {
            var history = Json.WriteBytes(State.ToJson());
            State.Receipt = Crypto.Receipt(history);
            var seal = Crypto.Hex(Crypto.Hmac(Bundle.SealKey, history));
            Directory.CreateDirectory(Folder);
            var tmp = ZipPath + ".tmp";
            using (var fs = new FileStream(tmp, FileMode.Create))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                Add(zip, "history.json", history);
                Add(zip, "seal.txt", Encoding.ASCII.GetBytes(seal + "\n"));
                Add(zip, "receipt.txt", new UTF8Encoding(false).GetBytes(
                    "Receipt " + State.Receipt + "\r\n" + State.Name + "\r\n" + State.Id + "\r\n" + State.Submitted + "\r\n"));
                foreach (var q in Exam.Questions)
                {
                    // The Check that set the mark; on a tie, the later answer.
                    var best = ChecksFor(q.Number).OrderByDescending(c => c.Mark).ThenByDescending(c => c.N).FirstOrDefault();
                    if (best != null) Add(zip, best.Variant + ".c", new UTF8Encoding(false).GetBytes(best.Code.Replace("\n", "\r\n") + "\r\n"));
                }
            }
            Replace(tmp, ZipPath);
        }

        static void Add(ZipArchive zip, string name, byte[] data)
        {
            using (var s = zip.CreateEntry(name, CompressionLevel.Optimal).Open()) s.Write(data, 0, data.Length);
        }

        // ---- saving ---------------------------------------------------------

        readonly object saveLock = new object();

        // One save at a time: the clock's save and a Check's save must not
        // interleave, or the older one could land last and lose the Check.
        public void Save()
        {
            lock (saveLock)
            {
                byte[] body;
                lock (State)
                {
                    State.Seq++;
                    var json = State.ToJson();
                    if (State.Receipt != null) json["receipt"] = State.Receipt;
                    body = Json.WriteBytes(json);
                }
                var head = Encoding.ASCII.GetBytes("LCRSAVE1 " + Crypto.Hex(Crypto.Hmac(Bundle.SealKey, body)) + "\n");
                var data = head.Concat(body).ToArray();
                foreach (var path in new[] { SavePath, BackupPath })
                {
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(path));
                        File.WriteAllBytes(path + ".tmp", data);
                        Replace(path + ".tmp", path);
                    }
                    catch (Exception) { }
                }
            }
        }

        static void Replace(string tmp, string path)
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    if (File.Exists(path)) File.Replace(tmp, path, null);
                    else File.Move(tmp, path);
                    return;
                }
                catch (IOException) when (attempt < 10) { System.Threading.Thread.Sleep(100); }
                catch (UnauthorizedAccessException) when (attempt < 10) { System.Threading.Thread.Sleep(100); }
            }
        }

        State ReadSave(string path)
        {
            try
            {
                var data = File.ReadAllBytes(path);
                int nl = Array.IndexOf(data, (byte)'\n');
                if (nl < 0) return null;
                var head = Encoding.ASCII.GetString(data, 0, nl).Split(' ');
                var body = data.Skip(nl + 1).ToArray();
                if (head.Length != 2 || head[0] != "LCRSAVE1") return null;
                if (head[1] != Crypto.Hex(Crypto.Hmac(Bundle.SealKey, body))) return null;
                var st = State.FromJson(Json.Parse(body));
                if (st.Pack != Bundle.PackId || Student.NormalizeId(st.Id) != Student.NormalizeId(Student.Id)) return null;
                return st;
            }
            catch (Exception) { return null; }
        }

        string[] ReadMarker()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(MarkerKey))
                {
                    var started = k?.GetValue("started") as string;
                    var deadline = k?.GetValue("deadline") as string;
                    return started != null && deadline != null ? new[] { started, deadline } : null;
                }
            }
            catch (Exception) { return null; }
        }

        void WriteMarker()
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(MarkerKey))
                {
                    k.SetValue("started", State.Started);
                    k.SetValue("deadline", State.Deadline);
                }
            }
            catch (Exception) { }
        }
    }
}
