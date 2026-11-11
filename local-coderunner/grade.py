r"""Grades the zips students uploaded from Local CodeRunner.

    $env:PYTHONIOENCODING = "utf-8"
    python grade.py --pack "..\class-contents\150. Practical Exam 02\instructors-only\resit-pack"
        --submissions <LMS download> [more folders] --out <results folder>
        [--receipts receipts.csv] [--lab-saves <folders copied off the lab PCs>] [--pity-enabled]

--submissions is searched for submissions however students wrapped them: zips
inside zips to any depth, the LMS's "Download all submissions" zip included;
the zip's files extracted and zipped again; or a .save sent in place of the
zip, which counts only when no zip came for that ID. Copies with the same
receipt count once. Files that held no submission are listed at the end.

Nothing a lab PC worked out is trusted: every Check in each student's history
is compiled and run again here, and the penalty and mark recomputed in order.
The seal shows whether the history was edited with a copy of the exe's key; the
receipt is the stronger check. Type the front pages' receipts into --receipts
(columns id, receipt; a blank box stays blank). Where a front page has none,
--lab-saves takes the Desktop\PE02 folders copied off the lab PCs: those never
left the room, so their receipt stands in for the paper's.

Written into --out:

    answers/<name>/<variant>.c   the answer that set each mark, in the layout the
                                 PE02 grader (pe02_grade.py) reads
    resit-scores.csv             one row per student, with every flag and the lab PC used
    resit-checks.csv             every Check, recomputed
    resit-feedback.html          each student's feedback, to paste into the LMS
"""

import argparse
import base64
import csv
import hashlib
import hmac
import html
import io
import json
import re
import sys
import tempfile
import zipfile
from collections import defaultdict
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.dont_write_bytecode = True  # no __pycache__ beside lcr.py in the repository
import lcr  # noqa: E402

HONORARY = 0.2  # --pity-enabled raises a question below this share of its points to it
MAX_DEPTH = 6  # zips inside zips
SLIP = 2  # a receipt this close to the zip's is a copying slip: a forged one cannot land so near


def norm_id(s):
    return re.sub(r"\s", "", s or "").upper()


def safe(name):
    return re.sub(r'[<>:"/\\|?*]', "_", name).strip(". ") or "student"


def upload_of(name):
    """The LMS upload an entry belongs to: the student's folder or file-name
    prefix in the LMS download (both end at "_assignsubmission_"), else the
    entry's top folder."""
    parts = name.split("/")
    for i, p in enumerate(parts):
        if "_assignsubmission_" in p:
            return "/".join(parts[:i] + [p.split("_assignsubmission_", 1)[0]])
    return parts[0]


def submissions_in(root):
    """(found, unread) under root. found holds every history.json, with the
    seal.txt beside it, and every .save; unread the files that held neither,
    by upload, so a student who sent only .c files shows up."""
    found, unread = [], []

    def visit(where, names, read, depth):
        names = [n for n in names if not n.endswith("/")]
        lower = {n.lower(): n for n in names}
        beside_history = {n.lower().rpartition("/")[0] for n in names if n.lower().rpartition("/")[2] == "history.json"}
        before, made, loose = len(found), set(), []
        for n in names:
            folder, _, base = n.lower().rpartition("/")
            if base == "history.json":
                seal = lower.get(folder + "/seal.txt" if folder else "seal.txt")
                found.append(dict(source=where + n, kind="zip", data=read(n), seal=read(seal) if seal else b""))
                made.add(upload_of(n))
            elif base.endswith(".save"):
                found.append(dict(source=where + n, kind="save", data=read(n)))
                made.add(upload_of(n))
            elif base.endswith(".zip") and depth < MAX_DEPTH:
                try:
                    z = zipfile.ZipFile(io.BytesIO(read(n)))
                except zipfile.BadZipFile:
                    loose.append((n, "a .zip that does not open"))
                    continue
                inner = visit(f"{where}{n} > ", z.namelist(), z.read, depth + 1)
                if inner is None:
                    peek = [m for m in z.namelist() if not m.endswith("/")]
                    loose.append((n, "a zip with no submission in it: "
                                  + (", ".join(peek[:5]) + (", ..." if len(peek) > 5 else "") if peek else "empty")))
                else:
                    made.add(upload_of(n))
                    unread.extend(inner)
            elif folder not in beside_history:
                loose.append((n, "not a Local CodeRunner file"))
        mine = [(where + n, why) for n, why in loose if upload_of(n) not in made]
        return mine if len(found) > before or depth == 0 else None

    root = Path(root)
    if root.is_file():
        unread.extend(visit("", [root.name], lambda n: root.read_bytes(), 0))
    else:
        files = sorted(p.relative_to(root).as_posix() for p in root.rglob("*") if p.is_file())
        unread.extend(visit("", files, lambda n: (root / n).read_bytes(), 0))
    return found, unread


def read_item(item, key):
    """One found submission as a row, before its Checks are run: who, the
    receipt, and whether the seal holds. None if it cannot be read."""
    try:
        if item["kind"] == "zip":
            history = item["data"]
            h = json.loads(history)
            seal_ok = item["seal"].decode("ascii", "replace").strip() == hmac.new(key, history, hashlib.sha256).hexdigest()
            receipt = lcr.receipt(history)
        else:
            head, _, body = item["data"].partition(b"\n")
            h = json.loads(body)
            mac = head.decode("ascii", "replace").split(" ")
            seal_ok = mac == ["LCRSAVE1", hmac.new(key, body, hashlib.sha256).hexdigest()]
            # A .save keeps the receipt the exe showed at Submit, under its seal; one never submitted has none.
            receipt = h.get("receipt") or ""
    except (ValueError, UnicodeDecodeError):
        return None
    if not isinstance(h, dict) or "checks" not in h:
        return None
    return dict(source=item["source"], kind=item["kind"], id=str(h.get("id", "")), name=str(h.get("name", "")),
                receipt=receipt, seal_ok=seal_ok, history=h, flags=[], checks=[], best={}, best_check={})


def pick(rows):
    """One submission per ID. Students often send one twice, the PE02 folder
    zipped with both files in it or the zip beside a re-zipped copy: copies with
    the same receipt are one. A .save counts only when no zip came. Different
    receipts all stay, flagged, for the front page to decide."""
    by_id = defaultdict(list)
    for r in rows:
        by_id[norm_id(r["id"])].append(r)
    kept = []
    for group in by_id.values():
        zips = [r for r in group if r["kind"] == "zip"]
        if zips:
            group = zips
        else:
            # The exe writes the Desktop and AppData saves together; the higher seq is the later.
            group = [max(group, key=lambda r: r["history"].get("seq", 0))]
            group[0]["flags"].append("graded from a .save: no zip came for this ID")
        unique = {}
        for r in group:
            unique.setdefault((r["receipt"], r["seal_ok"]), r)
        group = list(unique.values())
        if len(group) > 1:
            receipts = sorted({r["receipt"] for r in group})
            for r in group:
                r["flags"].append(f"{len(group)} different zips for this ID ({', '.join(receipts)}): keep the one "
                                  "whose receipt is on the front page")
        kept.extend(group)
    return kept


def lab_receipts(folders, key):
    """ID -> the receipt of the latest submission the copied lab PC folders hold."""
    latest = {}
    for folder in folders:
        for item in submissions_in(folder)[0]:
            r = read_item(item, key)
            if not r or not r["seal_ok"] or not r["receipt"] or r["history"].get("status") != "submitted":
                continue
            k, when = norm_id(r["id"]), r["history"].get("submitted") or ""
            if k not in latest or when > latest[k][0]:
                latest[k] = (when, r["receipt"])
    return {k: v[1] for k, v in latest.items()}


def distance(a, b):
    """Edits between two receipts, so a skipped character counts once."""
    a, b = lcr.normalize_code(a), lcr.normalize_code(b)
    prev = list(range(len(b) + 1))
    for i, x in enumerate(a, 1):
        cur = [i]
        for j, y in enumerate(b, 1):
            cur.append(min(prev[j] + 1, cur[j - 1] + 1, prev[j - 1] + (x != y)))
        prev = cur
    return prev[-1]


def compare_receipt(r, trusted, where):
    n = distance(trusted, r["receipt"])
    if n == 0:
        return f"{where} matches"
    if n <= SLIP:
        r["flags"].append(f"the {where} receipt is {n} character{'s' if n > 1 else ''} off this zip's: "
                          "a copying slip, most likely; look at the handwriting")
        return f"{where} {n} off"
    r["flags"].append(f"the {where} receipt is not this zip's: the zip was changed or swapped")
    return f"{where.upper()} DIFFERS"


def grade_one(row, pack, exam, roster, tmp):
    h = row["history"]
    if not row["seal_ok"]:
        what = "history.json" if row["kind"] == "zip" else "the .save"
        row["flags"].append(f"seal does not match: {what} was changed after the exe wrote it")
    if h.get("pack") != pack["pack"]:
        row["flags"].append(f"made by another pack ({h.get('pack')}), not {pack['pack']}")
    student = roster.get(norm_id(h.get("id")))
    if not student:
        row["flags"].append("ID number not on this pack's roster")
    elif {str(k): v for k, v in h.get("draw", {}).items()} != student["draw"]:
        row["flags"].append("questions differ from the ones this pack drew for this ID")
    if h.get("status") != "submitted":
        row["flags"].append("not submitted")
    for e in h.get("events", []):
        if "proctor" in e.get("what", "") or "moved" in e.get("what", ""):
            row["flags"].append(f"{e['at'][11:16]} {e['what']}")

    variants = {v["id"]: v for q in exam["questions"] for v in q["variants"]}
    points = {q["number"]: q["points"] for q in exam["questions"]}
    count = defaultdict(int)
    for c in h.get("checks", []):
        q = c["q"]
        var = variants.get(c["variant"])
        if var is None:
            row["flags"].append(f"Check on an unknown variant {c['variant']}")
            continue
        pen = lcr.penalty(exam["penalty"], count[q])
        count[q] += 1
        again = lcr.check(exam, var, c["code"], tmp)
        mark = lcr.mark(points[q], again["fraction"], pen)
        same = (again["outcome"] == c["outcome"] and again["passed"] == c["passed"]
                and abs(pen - c["penalty"]) < 1e-9 and abs(mark - c["mark"]) < 1e-6)
        if not same:
            row["flags"].append(f"Q{q} Check {count[q]}: the lab PC recorded {c['outcome']} "
                                f"{sum(c['passed'])} passed, {c['mark']:.2f}; here {again['outcome']} "
                                f"{sum(again['passed'])} passed, {mark:.2f}")
        rec = dict(q=q, n=count[q], variant=var["id"], at=c["at"], outcome=again["outcome"],
                   passed=again["passed"], tests=var["tests"], penalty=pen, mark=mark,
                   recorded=c["mark"], at_submit=c.get("at_submit", False), code=c["code"],
                   message=again["message"])
        row["checks"].append(rec)
        if q not in row["best"] or mark >= row["best"][q]:
            row["best"][q], row["best_check"][q] = mark, rec
    for q in points:
        row["best"].setdefault(q, 0.0)
    return row


def feedback_html(rows, exam, pity):
    css = ("font-family: 'Segoe UI', Arial, sans-serif; font-size: 15px; line-height: 1.55; color: #1a1816;"
           " background: #faf7f1; border: 1px solid #c9bfa8; border-radius: 12px; padding: 20px 26px;"
           " margin: 0 0 28px;")
    th = "text-align: left; font-weight: 600; color: #6b665e; padding: 4px 14px 4px 0; border-bottom: 1px solid #c9bfa8;"
    td = "padding: 4px 14px 4px 0; border-bottom: 1px solid #e3dcc8; vertical-align: top;"
    mono = "font-family: Consolas, monospace;"
    out = ["<!DOCTYPE html><html><head><meta charset='utf-8'><title>Re-sit feedback</title></head>"
           "<body style='background:#efe9dc; padding: 24px;'>"]
    for r in rows:
        e = html.escape
        out.append(f"<!-- {e(r['id'])} -->\n<div style=\"{css}\">")
        out.append(f"<p style='margin:0 0 4px; font-size: 18px; font-weight: 600;'>{e(r['name'])}</p>")
        out.append(f"<p style='margin:0 0 14px; color:#6b665e;'><span style='{mono}'>{e(r['id'])}</span>"
                   f" &nbsp; receipt <span style='{mono}'>{e(r['receipt'])}</span></p>")
        for q in exam["questions"]:
            n, pts = q["number"], q["points"]
            mine = [c for c in r["checks"] if c["q"] == n]
            out.append(f"<p style='margin: 16px 0 6px; font-weight: 600;'>Question {n} &middot; {e(q['title'])}"
                       f" &nbsp; <span style='{mono}'>{r['final'][n]:.2f} / {pts:g}</span></p>")
            if not mine:
                out.append("<p style='margin:0; color:#6b665e;'>No Check, so no answer was graded.</p>")
                continue
            out.append(f"<table style='border-collapse: collapse; font-size: 14px;'><tr><th style='{th}'>Check</th>"
                       f"<th style='{th}'>Time</th><th style='{th}'>Result</th><th style='{th}'>Penalty</th>"
                       f"<th style='{th}'>Mark</th></tr>")
            for c in mine:
                res = ("did not compile" if c["outcome"] == "compile" else "not accepted: " + c["message"]
                       if c["outcome"] == "rule" else f"{sum(c['passed'])} of {len(c['passed'])} tests passed")
                star = " &larr; counts" if c is r["best_check"].get(n) else ""
                out.append(f"<tr><td style='{td}{mono}'>{c['n']}{' (at Submit)' if c['at_submit'] else ''}</td>"
                           f"<td style='{td}{mono}'>{e(c['at'][11:16])}</td><td style='{td}'>{e(res)}</td>"
                           f"<td style='{td}{mono}'>{c['penalty']:g}%</td>"
                           f"<td style='{td}{mono}'>{c['mark']:.2f}{star}</td></tr>")
            out.append("</table>")
            best = r["best_check"].get(n)
            if best and best["outcome"] == "ran":
                failed = [t["label"] for t, ok in zip(best["tests"], best["passed"]) if not ok]
                if failed:
                    out.append("<p style='margin: 8px 0 0;'>The counted Check failed: " + e("; ".join(failed)) + ".</p>")
            if r["honorary"].get(n):
                out.append(f"<p style='margin: 6px 0 0; color:#6b665e;'>Raised to {HONORARY:.0%} of the points: "
                           f"{r['honorary'][n]:.2f} honorary.</p>")
        total = sum(q["points"] for q in exam["questions"])
        out.append(f"<p style='margin: 18px 0 0; font-weight: 600;'>Total &nbsp; <span style='{mono}'>"
                   f"{sum(r['final'].values()):.2f} / {total:g}</span></p></div>")
    out.append("</body></html>")
    return "\n".join(out)


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--pack", type=Path, required=True, help="the folder pack.py wrote")
    ap.add_argument("--submissions", type=Path, nargs="+", required=True,
                    help="the LMS download, and any other folders or zips to search")
    ap.add_argument("--out", type=Path, required=True)
    ap.add_argument("--receipts", type=Path, help="CSV of id,receipt typed from the front pages")
    ap.add_argument("--lab-saves", type=Path, nargs="+", default=[],
                    help="Desktop\\PE02 folders copied off the lab PCs, for front pages with no receipt")
    ap.add_argument("--pity-enabled", action="store_true", dest="pity",
                    # argparse %-formats help text, so the % sign is doubled.
                    help=f"raise any question below {HONORARY * 100:.0f}%% of its points to that")
    args = ap.parse_args()

    out = args.out.resolve()
    pack = json.loads((args.pack / "pack.json").read_text(encoding="utf-8"))
    exam = pack["exam"]
    key = base64.b64decode(pack["seal_key"])
    roster = {norm_id(s["id"]): s for s in pack["roster"]}
    paper = {}
    if args.receipts:
        for r in csv.DictReader(args.receipts.read_text(encoding="utf-8-sig").splitlines()):
            code = lcr.normalize_code(r.get("receipt") or "")
            if code:
                paper[norm_id(r.get("id"))] = code
    lab = lab_receipts(args.lab_saves, key)

    found, unread = [], []
    for root in args.submissions:
        f, u = submissions_in(root)
        found += f
        unread += u
    rows = []
    for item in found:
        r = read_item(item, key)
        if r:
            rows.append(r)
        else:
            unread.append((item["source"], "damaged or edited: it does not read as a Local CodeRunner history"))
    rows = pick(rows)
    if not rows:
        sys.exit("No Local CodeRunner submissions under " + ", ".join(map(str, args.submissions)))
    with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as tmp:
        for i, r in enumerate(rows, 1):
            print(f"[{i}/{len(rows)}] {r['source']}")
            grade_one(r, pack, exam, roster, tmp)

    by_id = defaultdict(list)
    for r in rows:
        by_id[norm_id(r["id"])].append(r)
    for r in rows:
        k = norm_id(r["id"])
        if k in paper:
            r["paper"] = compare_receipt(r, paper[k], "paper")
        elif k in lab:
            r["paper"] = compare_receipt(r, lab[k], "lab PC")
        else:
            r["paper"] = "no receipt" if paper or lab else ""
        r["final"], r["honorary"] = {}, {}
        for q in exam["questions"]:
            n, pts = q["number"], q["points"]
            m = r["best"][n]
            if args.pity and m < HONORARY * pts:
                r["honorary"][n] = HONORARY * pts - m
                m = HONORARY * pts
            r["final"][n] = m
    rows.sort(key=lambda r: r["name"].lower())
    absent = [s for k, s in roster.items() if k not in by_id]

    out.mkdir(parents=True, exist_ok=True)
    for r in rows:
        twin = len(by_id[norm_id(r["id"])]) > 1
        folder = out / "answers" / safe(f"{r['name']} {r['receipt']}" if twin else r["name"])
        folder.mkdir(parents=True, exist_ok=True)
        for n, c in r["best_check"].items():
            (folder / f"{c['variant']}.c").write_text(c["code"].rstrip("\n") + "\n", encoding="utf-8", newline="\n")
    qs = exam["questions"]
    total = sum(q["points"] for q in qs)
    with (out / "resit-scores.csv").open("w", newline="", encoding="utf-8-sig") as f:
        w = csv.writer(f)
        w.writerow(["id", "name"] + [f"Q{q['number']} ({q['points']:g})" for q in qs]
                   + [f"total ({total:g})", "honorary"] + [f"Q{q['number']} Checks" for q in qs]
                   + ["receipt", "receipt check", "seal", "lab PC", "started", "submitted", "flags", "graded from"])
        for r in rows:
            h = r["history"]
            w.writerow([r["id"], r["name"]] + [f"{r['final'][q['number']]:.2f}" for q in qs]
                       + [f"{sum(r['final'].values()):.2f}", f"{sum(r['honorary'].values()):.2f}"]
                       + [sum(1 for c in r["checks"] if c["q"] == q["number"]) for q in qs]
                       + [r["receipt"], r["paper"], "ok" if r["seal_ok"] else "BROKEN", h.get("machine", ""),
                          (h.get("started") or "")[:16].replace("T", " "),
                          (h.get("submitted") or "")[:16].replace("T", " "), " | ".join(r["flags"]), r["source"]])
        for s in absent:
            w.writerow([s["id"], s["name"]] + [""] * (len(qs) + 2 + len(qs))
                       + ["", "", "", "", "", "", "no submission found", ""])
    with (out / "resit-checks.csv").open("w", newline="", encoding="utf-8-sig") as f:
        w = csv.writer(f)
        w.writerow(["id", "name", "question", "check", "variant", "at", "outcome", "passed", "tests",
                    "penalty %", "mark", "lab PC's mark", "at Submit"])
        for r in rows:
            for c in r["checks"]:
                w.writerow([r["id"], r["name"], c["q"], c["n"], c["variant"], c["at"], c["outcome"],
                            sum(c["passed"]), len(c["passed"]), f"{c['penalty']:g}", f"{c['mark']:.2f}",
                            f"{c['recorded']:.2f}", "yes" if c["at_submit"] else ""])
    (out / "resit-feedback.html").write_text(feedback_html(rows, exam, args.pity), encoding="utf-8")

    flagged = [r for r in rows if r["flags"]]
    print(f"\n{len(rows)} submissions graded, {len(absent)} students on the roster with none, {len(flagged)} flagged.")
    for r in flagged:
        print(f"  {r['name']} ({r['id']}): " + "; ".join(r["flags"]))
    if unread:
        print(f"\n{len(unread)} files held no submission; look at them by hand:")
        for where, why in unread:
            print(f"  {where}: {why}")
    on_lab = [s["name"] for s in absent if norm_id(s["id"]) in lab]
    if on_lab:
        print(f"\nNo upload, but a lab PC holds a submitted zip for {len(on_lab)}: " + ", ".join(sorted(on_lab))
              + ". Add the lab folders to --submissions to grade it.")
    bare = [r for r in rows if r["paper"] == "no receipt"]
    if bare:
        by_pc = defaultdict(list)
        for r in bare:
            by_pc[r["history"].get("machine") or "unknown PC"].append(r["name"])
        print(f"\n{len(bare)} with no receipt on paper or from a lab PC. Copy Desktop\\PE02 off these PCs "
              "and pass the copies as --lab-saves:")
        for pc in sorted(by_pc):
            print(f"  {pc}: " + ", ".join(sorted(by_pc[pc])))
    print(f"Wrote {out}")


if __name__ == "__main__":
    main()
