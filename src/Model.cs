using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace LocalCodeRunner
{
    // The exam as the pack carries it: the questions without model answers,
    // the roster with each student's draw, and the keys for seals and the proctor.
    class Bundle
    {
        public string PackId, Title;
        public byte[] SealKey, ProctorSalt, ProctorHash;
        public int ProctorIterations;
        public Exam Exam;
        public List<Student> Roster;

        public static Bundle Parse(Dictionary<string, object> d)
        {
            var proctor = Json.Obj(d["proctor"]);
            return new Bundle
            {
                PackId = (string)d["pack"],
                Title = (string)d["title"],
                SealKey = Convert.FromBase64String((string)d["seal_key"]),
                ProctorSalt = Convert.FromBase64String((string)proctor["salt"]),
                ProctorHash = Convert.FromBase64String((string)proctor["hash"]),
                ProctorIterations = Convert.ToInt32(proctor["iterations"]),
                Exam = Exam.Parse(Json.Obj(d["exam"])),
                Roster = Json.List(d["roster"]).Select(s => Student.Parse(Json.Obj(s))).ToList(),
            };
        }

        public Student FindStudent(string id)
        {
            string key = Student.NormalizeId(id);
            return key.Length == 0 ? null : Roster.FirstOrDefault(s => Student.NormalizeId(s.Id) == key);
        }

        public bool IsProctor(string code) =>
            Crypto.SameBytes(Crypto.Derive(code, ProctorSalt, ProctorIterations, 32), ProctorHash);
    }

    class Student
    {
        public string Id, Name;
        public Dictionary<int, string> Draw;

        public static string NormalizeId(string id) =>
            new string((id ?? "").Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();

        public static Student Parse(Dictionary<string, object> d) => new Student
        {
            Id = (string)d["id"],
            Name = (string)d["name"],
            Draw = Json.Obj(d["draw"]).ToDictionary(kv => int.Parse(kv.Key), kv => (string)kv.Value),
        };
    }

    class Exam
    {
        public string Code, Title, Penalty, Header;
        public int Minutes;
        public double TimeLimit;
        public List<Question> Questions;

        public static Exam Parse(Dictionary<string, object> d) => new Exam
        {
            Code = (string)d["code"],
            Title = (string)d["title"],
            Penalty = (string)d["penalty"],
            Header = (string)d["header"],
            Minutes = Convert.ToInt32(d["minutes"]),
            TimeLimit = Convert.ToDouble(d["time_limit"]),
            Questions = Json.List(d["questions"]).Select(q => Question.Parse(Json.Obj(q))).ToList(),
        };

        public double TotalPoints => Questions.Sum(q => q.Points);
    }

    class Question
    {
        public int Number;
        public string Title;
        public double Points;
        public List<Variant> Variants;

        public static Question Parse(Dictionary<string, object> d) => new Question
        {
            Number = Convert.ToInt32(d["number"]),
            Title = (string)d["title"],
            Points = Convert.ToDouble(d["points"]),
            Variants = Json.List(d["variants"]).Select(v => Variant.Parse(Json.Obj(v))).ToList(),
        };

        public Variant Pick(string v) => Variants.First(x => x.V == v);
    }

    class Variant
    {
        public string Id, V, Fn, Signature, Preload;
        public List<string> Statement;
        public List<Rule> Rules;
        public List<TestCase> Tests;

        public static Variant Parse(Dictionary<string, object> d) => new Variant
        {
            Id = (string)d["id"],
            V = (string)d["v"],
            Fn = (string)d["fn"],
            Signature = (string)d["signature"],
            Preload = (string)d["preload"],
            Statement = Json.List(d["statement"]).Cast<string>().ToList(),
            Rules = Json.List(d["rules"]).Select(r => Json.Obj(r)).Select(r => new Rule
            {
                Kind = (string)r["kind"], Pattern = (string)r["pattern"], Message = (string)r["message"],
            }).ToList(),
            Tests = Json.List(d["tests"]).Select(t => Json.Obj(t)).Select(t => new TestCase
            {
                Label = (string)t["label"], Code = (string)t["code"], Expect = (string)t["expect"],
                Mark = Convert.ToDouble(t["mark"]), Shown = (bool)t["shown"],
            }).ToList(),
        };
    }

    class Rule { public string Kind, Pattern, Message; }

    class TestCase
    {
        public string Label, Code, Expect;
        public double Mark;
        public bool Shown;
    }

    static class Json
    {
        static JavaScriptSerializer Make() => new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 64 };

        public static Dictionary<string, object> Parse(string text) =>
            (Dictionary<string, object>)Make().DeserializeObject(text);

        public static Dictionary<string, object> Parse(byte[] utf8) => Parse(new UTF8Encoding(false).GetString(utf8));

        public static string Write(object value) => Make().Serialize(value);

        public static byte[] WriteBytes(object value) => new UTF8Encoding(false).GetBytes(Write(value));

        public static Dictionary<string, object> Obj(object o) => (Dictionary<string, object>)o;

        public static IEnumerable<object> List(object o) => o == null ? Enumerable.Empty<object>() : ((IEnumerable)o).Cast<object>();

        public static string Str(Dictionary<string, object> d, string key) =>
            d.TryGetValue(key, out var v) && v != null ? v.ToString() : null;

        public static double Num(Dictionary<string, object> d, string key) =>
            d.TryGetValue(key, out var v) && v != null ? Convert.ToDouble(v) : 0.0;
    }

    // The pack pack.py appends to the exe:
    // header JSON | ciphertext | 32-byte MAC | u32 header length | u64 body length | "LCRPACK1"
    class Pack
    {
        public string Title;
        byte[] header, body, tag;
        Dictionary<string, object> meta;

        public static Pack ReadFrom(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                if (fs.Length < 20 + 32) return null;
                var trailer = new byte[20];
                fs.Seek(-20, SeekOrigin.End);
                fs.Read(trailer, 0, 20);
                if (Encoding.ASCII.GetString(trailer, 12, 8) != "LCRPACK1") return null;
                int headerLen = BitConverter.ToInt32(trailer, 0);
                long bodyLen = BitConverter.ToInt64(trailer, 4);
                long end = fs.Length - 20;
                var p = new Pack
                {
                    header = ReadAt(fs, end - 32 - bodyLen - headerLen, headerLen),
                    body = ReadAt(fs, end - 32 - bodyLen, (int)bodyLen),
                    tag = ReadAt(fs, end - 32, 32),
                };
                p.meta = Json.Parse(p.header);
                p.Title = (string)p.meta["title"];
                return p;
            }
        }

        static byte[] ReadAt(Stream s, long offset, int length)
        {
            var buf = new byte[length];
            s.Seek(offset, SeekOrigin.Begin);
            int got = 0;
            while (got < length)
            {
                int n = s.Read(buf, got, length - got);
                if (n <= 0) throw new EndOfStreamException();
                got += n;
            }
            return buf;
        }

        // Null when the start password is wrong.
        public byte[] Open(string password)
        {
            var salt = Convert.FromBase64String((string)meta["salt"]);
            var nonce = Convert.FromBase64String((string)meta["nonce"]);
            var key = Crypto.Derive(password, salt, Convert.ToInt32(meta["iterations"]), 64);
            var encKey = key.Take(32).ToArray();
            var macKey = key.Skip(32).ToArray();
            if (!Crypto.SameBytes(Crypto.Hmac(macKey, header, body), tag)) return null;
            return Crypto.Gunzip(Crypto.KeystreamXor(encKey, nonce, body));
        }
    }
}
