r"""Packs an exam into one Local CodeRunner exe for the students to download.

    $env:PYTHONIOENCODING = "utf-8"
    python pack.py --roster <class list.csv>
        --out "..\class-contents\150. Practical Exam 02\instructors-only\resit-pack"
        --title "Practical Exam 02 · Re-sit"

It builds the exe with the .NET SDK, then checks that the exe and lcr.py give
the same result on the same answers, the model answers among them. Only then
does it draw each student's variants, encrypt the exam under a new start
password, and write into --out:

    PE02-LocalCodeRunner.exe   what students download; the questions inside
                               open only with the start password
    PASSWORDS.txt              the start password and the proctor password
    assignments.csv            each student's variant of each question
    pack.json                  everything grade.py needs, model answers included

The course owner keeps packs in the repository, in the exam's instructors-only/
folder. GitHub Pages serves main, so a pushed pack's PASSWORDS.txt and roster are
fetchable by anyone who has the path.
"""

import argparse
import base64
import csv
import datetime
import hashlib
import json
import re
import secrets
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parent
sys.path.insert(0, str(HERE))
sys.dont_write_bytecode = True  # no __pycache__ beside lcr.py in the repository
import lcr  # noqa: E402

DEFAULT_EXAM = REPO / "class-contents" / "150. Practical Exam 02" / "pe02-local-coderunner.json"


# --------------------------------------------------------------------------
# The roster
# --------------------------------------------------------------------------

def read_roster(path, id_col, name_col):
    """ID numbers and names from a CSV with a header row. The ID column is the
    one whose header mentions an ID or a number; the name is a "name" column,
    or "last name" and "first name" joined."""
    text = Path(path).read_bytes().decode("utf-8-sig", "replace")
    rows = list(csv.DictReader(text.splitlines()))
    if not rows:
        sys.exit(f"{path}: no rows under a header")
    headers = list(rows[0].keys())

    def find(pattern):
        return [h for h in headers if h and re.search(pattern, h, re.I)]

    ids = [id_col] if id_col else find(r"\bid\b|id ?(no|num|number)|student ?(no|num|number)")
    if len(ids) != 1 or ids[0] not in headers:
        sys.exit(f"{path}: cannot tell which column holds the ID number ({headers}); "
                 "name it with --id-column")
    if name_col:
        names = [name_col.split("+")]
    elif find(r"^\s*(full ?)?name\s*$"):
        names = [find(r"^\s*(full ?)?name\s*$")[:1]]
    elif find(r"last|surname|family") and find(r"first|given"):
        names = [[find(r"last|surname|family")[0], find(r"first|given")[0]]]
    else:
        sys.exit(f"{path}: cannot tell which column holds the name ({headers}); "
                 "name it with --name-column, or join two with --name-column \"Last name+First name\"")
    cols = names[0]
    for c in cols:
        if c not in headers:
            sys.exit(f"{path}: no column {c!r}")

    roster, seen = [], set()
    for n, row in enumerate(rows, 2):
        sid = (row[ids[0]] or "").strip()
        parts = [(row[c] or "").strip() for c in cols]
        name = ", ".join(p for p in parts if p) if len(parts) == 2 else parts[0]
        if not sid and not name:
            continue
        if not sid or not name:
            sys.exit(f"{path}, line {n}: a row with no ID number or no name")
        key = re.sub(r"\s", "", sid).upper()
        if key in seen:
            sys.exit(f"{path}, line {n}: ID number {sid} appears twice")
        seen.add(key)
        roster.append(dict(id=sid, name=name))
    print(f"Roster: {len(roster)} students, ID from {ids[0]!r}, name from {' + '.join(cols)!r}")
    return roster


# --------------------------------------------------------------------------
# Building and checking the exe
# --------------------------------------------------------------------------

def build_exe(work):
    """dotnet build on a copy, so bin/ and obj/ never land in the repository."""
    src = work / "src"
    src.mkdir()
    for name in ("LocalCodeRunner.csproj", "app.manifest"):
        shutil.copy2(HERE / name, src / name)
    shutil.copytree(HERE / "src", src / "src")
    done = subprocess.run(["dotnet", "build", str(src / "LocalCodeRunner.csproj"), "-c", "Release",
                           "-nologo", "-v", "q", "-o", str(work / "bin")],
                          capture_output=True, text=True)
    exe = work / "bin" / "LocalCodeRunner.exe"
    if done.returncode != 0 or not exe.exists():
        sys.exit("The exe did not build:\n" + done.stdout + done.stderr)
    if re.search(r"[1-9]\d* Warning\(s\)", done.stdout):
        sys.exit("The exe built with warnings:\n" + done.stdout)
    return exe


def self_test_cases(exam):
    """Answers that exercise every path: right, partly wrong, not compiling,
    crashing, hanging, a pasted main(), and Question 3's two rules."""
    cases = []
    hang_done = False
    for q in exam["questions"]:
        for v in q["variants"]:
            model = v["answer"]
            body = model.index("{") + 1
            add = [model, model + "\nint broken = ;\n", model + "\nint main(void)\n{\n    return 1;\n}\n"]
            if q["number"] == 3:
                add += [model.replace("n < 10U", "n < 100U"), model + "\n/* for */\n",
                        model.replace(f"{v['fn']}(n / 10U", "(0U * (n / 10U)")]
            else:
                add += [model.replace("(*comparisons)++;", ""),
                        model[:body] + "\n    if (n > 6) { volatile int *p = 0; *p = 1; }" + model[body:]]
                if not hang_done:
                    add.append(model[:body] + "\n    while (n == 1) { }" + model[body:])
                    hang_done = True
            cases += [dict(q=q["number"], v=v["v"], answer=a) for a in add]
    return cases


def self_test(exe, exam, work):
    cases = self_test_cases(exam)
    salt, nonce = secrets.token_bytes(16), secrets.token_bytes(16)
    data = secrets.token_bytes(1000)
    marks = [[10, 0.7, 3], [25, 0.04, 6], [15, 1.0, 0], [25, 0.8, 6]]
    crypto = dict(code="ab-c0 1il-ou", salt=base64.b64encode(salt).decode(), iterations=1000,
                  nonce=base64.b64encode(nonce).decode(), data=base64.b64encode(data).decode(),
                  regime=exam["penalty"], marks=marks)
    stripped = dict(exam, questions=[dict(q, variants=[{k: x for k, x in v.items() if k != "answer"}
                                                       for v in q["variants"]])
                                     for q in exam["questions"]])
    (work / "selftest-in.json").write_text(json.dumps(dict(exam=stripped, cases=cases, crypto=crypto)),
                                           encoding="utf-8")
    done = subprocess.run([str(exe), "--selftest", str(work / "selftest-in.json"),
                           str(work / "selftest-out.json")], timeout=1800)
    if done.returncode != 0:
        sys.exit("The exe's self-test did not run")
    got = json.loads((work / "selftest-out.json").read_text(encoding="utf-8-sig"))

    errors = []
    by_id = {(q["number"], v["v"]): v for q in exam["questions"] for v in q["variants"]}
    tmp = work / "py"
    tmp.mkdir()
    for case, theirs in zip(cases, got["checks"]):
        var = by_id[(case["q"], case["v"])]
        ours = lcr.check(exam, var, case["answer"], tmp)
        where = f"Q{case['q']}{case['v']} case {cases.index(case)}"
        if (ours["outcome"], ours["passed"]) != (theirs["outcome"], theirs["passed"]) \
                or abs(ours["fraction"] - theirs["fraction"]) > 1e-9:
            errors.append(f"{where}: lcr.py {ours['outcome']} {ours['passed']}, "
                          f"exe {theirs['outcome']} {theirs['passed']}")
        if case["answer"] == var["answer"] and ours["fraction"] != 1.0:
            errors.append(f"{where}: the model answer does not pass every test")
    if len(got["checks"]) != len(cases):
        errors.append("the exe returned a different number of checks")

    key = lcr.derive(crypto["code"], salt, 1000, 64)
    want = dict(normalized=lcr.normalize_code(crypto["code"]), key=key.hex(),
                stream=lcr.keystream_xor(key[:32], nonce, data).hex(),
                hmac=lcr.hmac256(key[32:], data, salt).hex(), receipt=lcr.receipt(data),
                penalties=[lcr.penalty(exam["penalty"], i) for i in range(40)],
                marks=[round(lcr.mark(*m), 9) for m in marks])
    for k, v in want.items():
        if got["crypto"][k] != v:
            errors.append(f"crypto: {k} differs between lcr.py and the exe")
    if errors:
        sys.exit("lcr.py and the exe disagree; nothing written:\n  " + "\n  ".join(errors))
    print(f"Self-test: {len(cases)} answers checked both ways, crypto and penalties agree")


# --------------------------------------------------------------------------
# Packing
# --------------------------------------------------------------------------

def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--exam", type=Path, default=DEFAULT_EXAM, help="the exam JSON (default: PE02's)")
    ap.add_argument("--roster", type=Path, help="the class list CSV")
    ap.add_argument("--sample-roster", type=int, metavar="N",
                    help="no class list: N made-up students, for a trial run only")
    ap.add_argument("--out", type=Path, required=True, help="a new, empty folder for the pack")
    ap.add_argument("--title", help="the exam's name in the window, e.g. \"Practical Exam 02 · Re-sit\"")
    ap.add_argument("--minutes", type=int, help="override the exam's time limit")
    ap.add_argument("--id-column")
    ap.add_argument("--name-column")
    args = ap.parse_args()

    out = args.out.resolve()
    if bool(args.roster) == bool(args.sample_roster):
        sys.exit("give --roster, or --sample-roster N for a trial")
    if out.exists() and any(out.iterdir()):
        sys.exit(f"{out} is not empty; packing again makes new passwords, so use a new folder")

    exam = json.loads(args.exam.read_text(encoding="utf-8"))
    if args.minutes:
        exam["minutes"] = args.minutes
    title = args.title or exam["title"]
    if args.roster:
        roster = read_roster(args.roster, args.id_column, args.name_column)
    else:
        roster = [dict(id=f"TRIAL-{i:03d}", name=f"Trial Student {i}") for i in range(1, args.sample_roster + 1)]
        print(f"Roster: {len(roster)} made-up students (trial pack)")

    with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as tmp:
        work = Path(tmp)
        exe = build_exe(work)
        self_test(exe, exam, work)

        for s in roster:
            s["draw"] = {str(q["number"]): secrets.choice(q["variants"])["v"] for q in exam["questions"]}
        start, proctor = lcr.new_code(), lcr.new_code()
        while proctor == start:
            proctor = lcr.new_code()
        pack_id, seal_key, proctor_salt = secrets.token_hex(4), secrets.token_bytes(32), secrets.token_bytes(16)
        public_exam = dict(exam, questions=[dict(q, variants=[{k: x for k, x in v.items() if k != "answer"}
                                                              for v in q["variants"]])
                                            for q in exam["questions"]])
        bundle = dict(format=1, pack=pack_id, title=title, seal_key=base64.b64encode(seal_key).decode(),
                      proctor=dict(salt=base64.b64encode(proctor_salt).decode(), iterations=lcr.ITERATIONS,
                                   hash=base64.b64encode(lcr.derive(proctor, proctor_salt, lcr.ITERATIONS, 32)).decode()),
                      exam=public_exam, roster=roster)
        plain = json.dumps(bundle, ensure_ascii=False).encode("utf-8")
        name = f"{exam['code']}-LocalCodeRunner.exe"
        packed = work / name
        packed.write_bytes(exe.read_bytes() + lcr.seal_pack(plain, start, title))

        verdict = work / "verify.json"
        if subprocess.run([str(packed), "--verify", start, str(verdict)], timeout=300).returncode != 0:
            sys.exit("The packed exe does not open with its own start password")
        seen = json.loads(verdict.read_text(encoding="utf-8-sig"))
        if seen["sha256"] != hashlib.sha256(plain).hexdigest() or seen["title"] != title:
            sys.exit("The packed exe opened, but what it read is not what was packed")
        if subprocess.run([str(packed), "--verify", "WRONG-PASS1", str(verdict)], timeout=300).returncode == 0:
            sys.exit("The packed exe opened with a wrong password")

        out.mkdir(parents=True, exist_ok=True)
        shutil.copy2(packed, out / name)

    built = datetime.datetime.now().astimezone().isoformat(timespec="seconds")
    (out / "PASSWORDS.txt").write_text(
        f"{title}\npack {pack_id}, built {built}\n\n"
        f"Start password    {start}\n"
        f"    Write it on the board when the exam starts, not before.\n\n"
        f"Proctor password  {proctor}\n"
        f"    Never show it to students. It adds time, reopens a submitted exam,\n"
        f"    moves an exam to another PC, and restarts one whose save is lost.\n",
        encoding="utf-8")
    with (out / "assignments.csv").open("w", newline="", encoding="utf-8-sig") as f:
        w = csv.writer(f)
        w.writerow(["id", "name"] + [f"Q{q['number']}" for q in exam["questions"]])
        for s in roster:
            w.writerow([s["id"], s["name"]] + [
                next(v["id"] for v in q["variants"] if v["v"] == s["draw"][str(q["number"])])
                for q in exam["questions"]])
    (out / "pack.json").write_text(json.dumps(dict(
        format=1, pack=pack_id, title=title, built=built, exe=name,
        exe_sha256=hashlib.sha256((out / name).read_bytes()).hexdigest(),
        seal_key=base64.b64encode(seal_key).decode(), exam=exam, roster=roster),
        indent=1, ensure_ascii=False) + "\n", encoding="utf-8")

    print(f"\nWrote {out}:\n  {name}\n  PASSWORDS.txt\n  assignments.csv\n  pack.json")
    print(f"\nStart password    {start}\nProctor password  {proctor}")


if __name__ == "__main__":
    main()
