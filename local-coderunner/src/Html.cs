using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace LocalCodeRunner
{
    // The statement and result panes. WebBrowser is Internet Explorer 11, so
    // the CSS stays within what IE11 renders: no grid, no custom properties.
    static class Html
    {
        const string Css = @"
html { scrollbar-face-color: #e9e2cc; scrollbar-track-color: #faf7f1; scrollbar-arrow-color: #6b665e;
       scrollbar-shadow-color: #c9bfa8; scrollbar-highlight-color: #e9e2cc; scrollbar-3dlight-color: #faf7f1;
       scrollbar-darkshadow-color: #faf7f1; }
html, body { margin: 0; padding: 0; background: #faf7f1; color: #1a1816; }
body { font-family: 'Segoe UI', sans-serif; font-size: 15px; line-height: 1.6; padding: 18px 26px 28px; cursor: default; }
::selection { background: #d1b187; color: #1a1816; }
h1 { font-family: 'Segoe UI Semibold', 'Segoe UI', sans-serif; font-weight: normal; font-size: 21px; line-height: 1.3; color: #1a1816; margin: 4px 0 4px; }
h1 .qn { color: #79444a; }
h2 { font-family: 'Segoe UI Semibold', 'Segoe UI', sans-serif; font-weight: normal; font-size: 17px; line-height: 1.35; margin: 2px 0 10px; }
h3 { font-family: 'Segoe UI Semibold', 'Segoe UI', sans-serif; font-weight: normal; font-size: 14px; color: #4b3d44; margin: 22px 0 8px; }
p { margin: 0 0 12px; max-width: 42em; }
.points { color: #6b665e; margin: 0 0 18px; font-size: 14px; }
.num { font-family: Consolas, monospace; }
code { font-family: Consolas, monospace; font-size: 0.94em; color: #79444a; }
pre { font-family: Consolas, monospace; margin: 0; white-space: pre; }
pre.sig { font-size: 15px; line-height: 1.55; color: #4b3d44; background: #fffdf8; border: 1px solid #c9bfa8;
          padding: 12px 18px; margin: 0 0 14px; white-space: pre-wrap; word-wrap: break-word; }
table.ex pre { white-space: pre-wrap; word-wrap: break-word; }
.ln { display: block; padding-left: 2em; text-indent: -2em; }
pre.sig .kw { color: #ae5d40; }
table { border-collapse: collapse; }
table.ex { width: 100%; margin: 0 0 6px; }
table.ex th { text-align: left; font-family: 'Segoe UI Semibold', 'Segoe UI', sans-serif; font-weight: normal; font-size: 13px;
              color: #6b665e; padding: 6px 12px 6px 0; border-bottom: 1px solid #c9bfa8; }
table.ex td { vertical-align: top; padding: 8px 12px 8px 0; border-bottom: 1px solid #e3dcc8; }
table.ex pre { font-size: 13px; line-height: 1.5; color: #4b3d44; }
table.ex td.verdict { font-family: 'Segoe UI Semibold', 'Segoe UI', sans-serif; font-size: 13px; white-space: nowrap; padding-right: 0; }
.pass { color: #4b726e; }
.fail { color: #ae5d40; }
.trouble { color: #ae5d40; font-family: 'Segoe UI', sans-serif; font-size: 13px; }
table.marks { margin: 0 0 4px; }
table.marks th { text-align: left; font-family: 'Segoe UI', sans-serif; font-weight: normal; color: #6b665e; font-size: 13px;
                 padding: 0 28px 2px 0; white-space: nowrap; }
table.marks td { font-family: Consolas, monospace; font-size: 16px; color: #1a1816; padding: 0 28px 0 0; white-space: nowrap; }
table.marks td .note { font-family: 'Segoe UI', sans-serif; font-size: 12px; color: #6b665e; }
.status { padding-bottom: 8px; margin-bottom: 12px; border-bottom: 1px solid #c9bfa8; }
.status h2 { margin: 0; display: inline; }
.status .when { color: #6b665e; font-size: 13px; margin-left: 12px; }
pre.log { font-size: 13px; line-height: 1.5; color: #4b3d44; background: #fffdf8; border: 1px solid #c9bfa8;
          padding: 10px 14px; overflow-x: auto; }
pre.log a { color: #79444a; }
.lead { font-size: 15px; color: #4b3d44; }
.muted { color: #6b665e; }
.id { font-family: Consolas, monospace; font-size: 12px; color: #6b665e; letter-spacing: 0.04em; margin-top: 26px;
      padding-top: 8px; border-top: 1px solid #c9bfa8; }
";

        static string E(string s) => WebUtility.HtmlEncode(s ?? "");

        static string scaled;

        // IE's zoom overflows the pane, so every size but a 1px rule is scaled
        // by the display's factor instead.
        static string ScaledCss() => scaled ?? (scaled = Regex.Replace(Css, @"(\d+(?:\.\d+)?)px", m =>
        {
            double v = double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            return v <= 1 ? m.Value : (v * Theme.Scale).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "px";
        }));

        static string Page(string body) =>
            "<!DOCTYPE html><html><head><meta http-equiv=\"X-UA-Compatible\" content=\"IE=edge\">" +
            "<meta charset=\"utf-8\"><style>" + ScaledCss() + "</style></head><body>" + body + "</body></html>";

        public static string Statement(Question q, Variant v)
        {
            var sb = new StringBuilder();
            sb.Append("<h1><span class=\"qn\">Question ").Append(q.Number).Append("</span>&nbsp; ").Append(E(q.Title)).Append("</h1>");
            sb.Append("<p class=\"points\"><span class=\"num\">").Append(Theme.Num(q.Points).Replace(".00", "")).Append("</span> points</p>");
            var signature = "<span class=\"ln\">" +
                Regex.Replace(E(v.Signature), @"\b(const|unsigned|int)\b", "<span class=\"kw\">$1</span>") + "</span>";
            for (int i = 0; i < v.Statement.Count; i++)
            {
                sb.Append("<p>").Append(v.Statement[i]).Append("</p>");
                if (i == 0) sb.Append("<pre class=\"sig\">").Append(signature).Append("</pre>");
            }
            var shown = v.Tests.Where(t => t.Shown).ToList();
            if (shown.Count > 0)
            {
                sb.Append("<h3>For example</h3><table class=\"ex\"><tr><th>Test</th><th>Result</th></tr>");
                foreach (var t in shown)
                    sb.Append("<tr><td><pre>").Append(Code(t.Code)).Append("</pre></td><td><pre>")
                      .Append(Code(t.Expect.TrimEnd('\n'))).Append("</pre></td></tr>");
                sb.Append("</table>");
            }
            sb.Append("<div class=\"id\">").Append(E(v.Id)).Append("</div>");
            return Page(sb.ToString());
        }

        // One block per source line, so a line too long for the pane wraps
        // under itself with a hanging indent instead of reading as a new line.
        static string Code(string text) =>
            string.Concat(text.Split('\n').Select(l => "<span class=\"ln\">" + (l.Length == 0 ? "&nbsp;" : E(l)) + "</span>"));

        public static string Note(string title, string text) =>
            Page("<h2>" + E(title) + "</h2><p class=\"lead\">" + E(text) + "</p>");

        public static string Working(int n) =>
            Page("<div class=\"status\"><h2>Check " + n + " running</h2></div><p class=\"lead\">Compiling your answer and running every test. " +
                 "This takes a few seconds; an answer that never stops is cut off after a few seconds a test.</p>");

        public static string NoCheckYet(Session s, Question q)
        {
            var costs = string.Join(", ", Enumerable.Range(0, 4).Select(i =>
            {
                double p = Session.PenaltyFor(s.Exam.Penalty, i);
                return "Check " + (i + 1) + (p == 0 ? " is free" : " costs " + Theme.Pct(p));
            }));
            return Page("<h2>No Check yet</h2><p class=\"lead\">Check compiles your answer and runs every test, the hidden ones too. " +
                        "Each Check costs a little more of this question's <span class=\"num\">" + Theme.Num(q.Points) +
                        "</span> points: " + E(costs) + ", and so on. Your best Check is the one that counts.</p>" +
                        "<p class=\"muted\">A Check that does not compile still counts, so read your answer over first.</p>");
        }

        public static string Result(Session s, Question q, CheckRecord rec)
        {
            var v = q.Pick(s.State.Draw[q.Number]);
            var sb = new StringBuilder();
            int passed = rec.Passed.Count(p => p), total = v.Tests.Count;
            string headline = rec.Outcome == "compile" ? "did not compile"
                : rec.Outcome == "rule" ? "not accepted"
                : "<span class=\"num\">" + passed + "</span> of <span class=\"num\">" + total + "</span> tests passed";
            var hidden = Enumerable.Range(0, total).Where(i => !v.Tests[i].Shown).ToList();
            int number = s.ChecksFor(q.Number).TakeWhile(c => c != rec).Count() + 1;
            sb.Append("<div class=\"status\"><h2 class=\"").Append(rec.Outcome == "ran" && passed == total ? "pass" : rec.Outcome == "ran" ? "" : "fail")
              .Append("\">Check ").Append(number).Append(" &middot; ").Append(headline).Append("</h2>")
              .Append("<span class=\"when\">at ").Append(E(rec.At.Length >= 16 ? rec.At.Substring(11, 5) : rec.At))
              .Append(rec.AtSubmit ? ", graded at Submit" : "").Append("</span></div>");

            bool ran = rec.Outcome == "ran" && hidden.Count > 0;
            var examples = Enumerable.Range(0, total).Where(i => v.Tests[i].Shown).ToList();
            bool tally = rec.Outcome == "ran" && examples.Count > 0;
            sb.Append("<table class=\"marks\"><tr>").Append(tally ? "<th>Examples</th>" : "").Append(ran ? "<th>Hidden tests</th>" : "").Append("<th>This Check, ")
              .Append(rec.Penalty > 0 ? Theme.Pct(rec.Penalty) + " penalty" : "no penalty")
              .Append("</th><th>Best so far</th>").Append(s.Submitted ? "" : "<th>Next Check</th>").Append("</tr><tr>");
            if (tally) sb.Append("<td>").Append(examples.Count(i => rec.Passed[i])).Append(" of ").Append(examples.Count).Append("</td>");
            if (ran) sb.Append("<td>").Append(hidden.Count(i => rec.Passed[i])).Append(" of ").Append(hidden.Count).Append("</td>");
            sb.Append("<td>").Append(Theme.Num(rec.Mark)).Append(" / ").Append(Theme.Num(q.Points)).Append("</td><td>")
              .Append(Theme.Num(s.Best(q.Number))).Append(" / ").Append(Theme.Num(q.Points)).Append("</td>");
            if (!s.Submitted) sb.Append("<td>costs ").Append(Theme.Pct(s.NextPenalty(q.Number))).Append("</td>");
            sb.Append("</tr></table>");

            if (rec.Outcome == "rule")
            {
                sb.Append("<h3>Why</h3><p class=\"lead\">").Append(E(rec.Message)).Append("</p>");
            }
            else if (rec.Outcome == "compile")
            {
                sb.Append("<h3>What gcc said</h3><pre class=\"log\">").Append(GccLog(rec.Message)).Append("</pre>");
            }
            else
            {
                var shown = rec.Shown.ToDictionary(d => Convert.ToInt32(d["i"]), d => d);
                if (shown.Count > 0)
                {
                    sb.Append("<h3>Examples</h3><table class=\"ex\"><tr><th>Test</th><th>Expected</th><th>Got</th><th></th></tr>");
                    foreach (var kv in shown)
                    {
                        var t = v.Tests[kv.Key];
                        bool ok = rec.Passed[kv.Key];
                        var trouble = Json.Str(kv.Value, "trouble");
                        sb.Append("<tr><td><pre>").Append(Code(t.Code)).Append("</pre></td><td><pre>").Append(Code(t.Expect.TrimEnd('\n')))
                          .Append("</pre></td><td><pre>").Append(Code((Json.Str(kv.Value, "got") ?? "").TrimEnd('\n'))).Append("</pre>");
                        if (trouble != null) sb.Append("<div class=\"trouble\">").Append(E(trouble)).Append("</div>");
                        sb.Append("</td><td class=\"verdict ").Append(ok ? "pass\">pass" : "fail\">fail").Append("</td></tr>");
                    }
                    sb.Append("</table>");
                }
                var warnings = AnswerWarnings(rec.Message);
                if (warnings.Length > 0)
                    sb.Append("<h3>gcc warnings in your answer</h3><pre class=\"log\">").Append(warnings).Append("</pre>");
            }
            return Page(sb.ToString());
        }

        // gcc's messages, with "answer:12:5:" turned into a link to line 12 and
        // lines about the hidden test code kept vague.
        static string GccLog(string log)
        {
            var lines = (log ?? "").Split('\n').Where(l => l.Trim().Length > 0).Take(60);
            var sb = new StringBuilder();
            foreach (var raw in lines)
            {
                var line = E(raw);
                line = Regex.Replace(line, @"^answer:(\d+):(\d+):", m => "<a href=\"lcr://line/" + m.Groups[1].Value + "\">line " + m.Groups[1].Value + "</a>:");
                line = Regex.Replace(line, @"^answer:(\d+):", m => "<a href=\"lcr://line/" + m.Groups[1].Value + "\">line " + m.Groups[1].Value + "</a>:");
                line = Regex.Replace(line, @"^answer: ", "");
                line = Regex.Replace(line, @"^tests:\d+:(\d+:)?", "in the test code:");
                line = Regex.Replace(line, @"^tests: ", "in the test code: ");
                sb.Append(line).Append('\n');
            }
            return sb.ToString().TrimEnd('\n');
        }

        static string AnswerWarnings(string log)
        {
            var kept = (log ?? "").Split('\n').Where(l => Regex.IsMatch(l, @"^answer:\d+:(\d+:)? warning:")).Take(12);
            return GccLog(string.Join("\n", kept));
        }
    }
}
