using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace LocalCodeRunner
{
    class TestRun
    {
        public bool Passed;
        public string Got = "", Trouble;
    }

    class CheckResult
    {
        // "ran", "compile" or "rule" count as a Check; "nogcc" does not.
        public string Outcome, Message = "";
        public List<TestRun> Tests = new List<TestRun>();
        public double Fraction;
    }

    // Builds and runs an answer exactly as lcr.py does.
    static class Engine
    {
        static readonly string[] Flags =
            { "-Wall", "-Wextra", "-std=c17", "-g", "-ftrack-macro-expansion=0" };
        const int OutputCap = 64 * 1024;
        const int Parallel = 4;

        static readonly Dictionary<uint, string> Crashes = new Dictionary<uint, string>
        {
            [0xC0000005] = "crashed: a bad pointer, or an index outside the array",
            [0xC00000FD] = "crashed: stack overflow, a recursion that never stops",
            [0xC0000094] = "crashed: division by zero",
        };

        static string gcc;
        public static string Gcc => gcc ?? (gcc = FindOnPath("gcc.exe"));

        static string FindOnPath(string exe)
        {
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
            {
                try
                {
                    var p = Path.Combine(dir.Trim().Trim('"'), exe);
                    if (dir.Trim().Length > 0 && File.Exists(p)) return p;
                }
                catch (ArgumentException) { }
            }
            return null;
        }

        public static string GccVersion()
        {
            if (Gcc == null) return null;
            try
            {
                var psi = new ProcessStartInfo(Gcc, "-dumpfullversion")
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
                };
                using (var p = Process.Start(psi))
                {
                    var v = p.StandardOutput.ReadToEnd().Trim();
                    p.WaitForExit(10000);
                    return v;
                }
            }
            catch (Exception) { return null; }
        }

        public static string CleanAnswer(string text) => (text ?? "").Replace("\r\n", "\n").Replace("\r", "\n");

        public static string Harness(string header, string answer, List<TestCase> tests)
        {
            var sb = new StringBuilder();
            sb.Append(header).Append("#define main lcr_student_main\n");
            sb.Append("#line 1 \"answer\"\n").Append(answer).Append("\n#undef main\n#line 1 \"tests\"\n");
            sb.Append("int main(int argc, char **argv)\n{\n");
            sb.Append("    switch (argc > 1 ? atoi(argv[1]) : -1) {\n");
            for (int i = 0; i < tests.Count; i++)
            {
                sb.Append("    case ").Append(i).Append(": {\n");
                foreach (var line in tests[i].Code.Split('\n')) sb.Append("        ").Append(line).Append('\n');
                sb.Append("        break;\n    }\n");
            }
            sb.Append("    }\n    return 0;\n}\n");
            return sb.ToString();
        }

        public static string Tidy(string text) =>
            string.Join("\n", text.Split('\n').Select(l => l.TrimEnd())).TrimEnd('\n');

        public static string BrokenRule(Variant v, string answer)
        {
            foreach (var r in v.Rules)
            {
                bool hit = Regex.IsMatch(answer, r.Pattern);
                if (hit != (r.Kind == "require")) return r.Message;
            }
            return null;
        }

        public static CheckResult Check(Exam exam, Variant v, string answer)
        {
            answer = CleanAnswer(answer);
            var result = new CheckResult { Outcome = "ran" };
            foreach (var _ in v.Tests) result.Tests.Add(new TestRun());

            var why = BrokenRule(v, answer);
            if (why != null)
            {
                result.Outcome = "rule";
                result.Message = why;
                return result;
            }
            if (Gcc == null)
            {
                result.Outcome = "nogcc";
                result.Message = "gcc was not found on PATH. Raise your hand: this Check was not counted.";
                return result;
            }

            var work = Path.Combine(Path.GetTempPath(), "lcr-" + Guid.NewGuid().ToString("N").Substring(0, 12));
            Directory.CreateDirectory(work);
            try
            {
                var exe = Path.Combine(work, "lcr-check.exe");
                var args = string.Join(" ", Flags) + " -pipe -x c - -x none -o \"" + exe + "\" -lm";
                var source = new UTF8Encoding(false).GetBytes(Harness(exam.Header, answer, v.Tests));
                var compile = Run(Gcc, args, work, source, 120.0, false);
                if (compile.TimedOut)
                {
                    result.Outcome = "nogcc";
                    result.Message = "gcc did not finish in two minutes. Raise your hand: this Check was not counted.";
                    return result;
                }
                if (compile.ExitCode != 0 || !File.Exists(exe))
                {
                    result.Outcome = "compile";
                    result.Message = compile.Text;
                    return result;
                }
                result.Message = compile.Text;

                var runs = new RunResult[v.Tests.Count];
                System.Threading.Tasks.Parallel.For(0, v.Tests.Count,
                    new ParallelOptions { MaxDegreeOfParallelism = Parallel },
                    i => runs[i] = Run(exe, i.ToString(), work, new byte[0], exam.TimeLimit, true));

                double total = v.Tests.Sum(t => t.Mark), earned = 0;
                for (int i = 0; i < v.Tests.Count; i++)
                {
                    var r = runs[i];
                    var t = result.Tests[i];
                    t.Got = r.Text;
                    if (r.TimedOut) t.Trouble = "still running after " + exam.TimeLimit.ToString("0.###") + " seconds";
                    else if (r.Overflow) { t.Trouble = "printed more than 64 KB"; t.Got = ""; }
                    else if (r.ExitCode != 0)
                    {
                        uint code = unchecked((uint)r.ExitCode);
                        t.Trouble = Crashes.TryGetValue(code, out var m) ? m : "ended with exit code " + code;
                    }
                    t.Passed = t.Trouble == null && Tidy(t.Got) == Tidy(v.Tests[i].Expect);
                    if (t.Passed) earned += v.Tests[i].Mark;
                }
                result.Fraction = earned / total;
                return result;
            }
            finally
            {
                TryDelete(work);
            }
        }

        static void TryDelete(string dir)
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try { Directory.Delete(dir, true); return; }
                catch (Exception) { Thread.Sleep(200); }
            }
        }

        class RunResult
        {
            public int ExitCode;
            public bool TimedOut, Overflow;
            public string Text = "";
        }

        // Runs a program in a job object, so a hung or runaway answer, and
        // anything it starts, dies with the job.
        static RunResult Run(string file, string args, string dir, byte[] stdin, double seconds, bool capped)
        {
            var psi = new ProcessStartInfo(file, args)
            {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = dir,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            psi.EnvironmentVariables["LC_ALL"] = "C";
            psi.EnvironmentVariables["TMP"] = dir;
            psi.EnvironmentVariables["TEMP"] = dir;

            var result = new RunResult();
            using (var job = new Job(512L * 1024 * 1024))
            using (var p = new Process { StartInfo = psi })
            {
                p.Start();
                job.Add(p);
                var outBuf = new CappedBuffer(capped ? OutputCap : int.MaxValue);
                var errBuf = new CappedBuffer(capped ? OutputCap : int.MaxValue);
                var readOut = Task.Factory.StartNew(() => outBuf.Drain(p.StandardOutput.BaseStream), TaskCreationOptions.LongRunning);
                var readErr = Task.Factory.StartNew(() => errBuf.Drain(p.StandardError.BaseStream), TaskCreationOptions.LongRunning);
                try
                {
                    p.StandardInput.BaseStream.Write(stdin, 0, stdin.Length);
                    p.StandardInput.Close();
                }
                catch (IOException) { }

                var deadline = DateTime.UtcNow.AddSeconds(seconds);
                while (!p.WaitForExit(50))
                {
                    if (outBuf.Full || errBuf.Full) { result.Overflow = true; break; }
                    if (DateTime.UtcNow > deadline) { result.TimedOut = true; break; }
                }
                if (result.Overflow || result.TimedOut)
                {
                    job.Kill();
                    p.WaitForExit(5000);
                }
                Task.WaitAll(new Task[] { readOut, readErr }, 5000);
                if (!result.TimedOut && !result.Overflow) result.ExitCode = p.ExitCode;

                var bytes = outBuf.ToArray();
                var err = errBuf.ToArray();
                if (err.Length > 0) bytes = bytes.Concat(new[] { (byte)'\n' }).Concat(err).ToArray();
                if (capped && bytes.Length > OutputCap) result.Overflow = true;
                result.Text = new UTF8Encoding(false, false).GetString(bytes).Replace("\r\n", "\n");
            }
            return result;
        }

        class CappedBuffer
        {
            readonly MemoryStream data = new MemoryStream();
            readonly int cap;
            public volatile bool Full;
            public CappedBuffer(int cap) { this.cap = cap; }

            public void Drain(Stream s)
            {
                var buf = new byte[8192];
                try
                {
                    int n;
                    while ((n = s.Read(buf, 0, buf.Length)) > 0)
                    {
                        lock (data)
                        {
                            if (data.Length + n > cap) { Full = true; return; }
                            data.Write(buf, 0, n);
                        }
                    }
                }
                catch (Exception) { }
            }

            public byte[] ToArray() { lock (data) return data.ToArray(); }
        }
    }

    sealed class Job : IDisposable
    {
        IntPtr handle;

        public Job(long memoryLimit)
        {
            handle = CreateJobObject(IntPtr.Zero, null);
            var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
            // Kill on close, a per-process memory cap, and no crash dialog.
            info.BasicLimitInformation.LimitFlags = 0x2000 | 0x100 | 0x400;
            info.ProcessMemoryLimit = (UIntPtr)(ulong)memoryLimit;
            int size = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
            SetInformationJobObject(handle, 9, ref info, (uint)size);
        }

        public void Add(Process p)
        {
            try { AssignProcessToJobObject(handle, p.Handle); } catch (Exception) { }
        }

        public void Kill() => TerminateJobObject(handle, 1);

        public void Dispose()
        {
            if (handle != IntPtr.Zero) { CloseHandle(handle); handle = IntPtr.Zero; }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass, SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct IO_COUNTERS
        {
            public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
            public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr CreateJobObject(IntPtr attributes, string name);

        [DllImport("kernel32.dll")]
        static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION info, uint length);

        [DllImport("kernel32.dll")]
        static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [DllImport("kernel32.dll")]
        static extern bool TerminateJobObject(IntPtr job, uint exitCode);

        [DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr handle);
    }
}
