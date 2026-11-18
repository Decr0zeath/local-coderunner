# CLAUDE.md

Local CodeRunner: an offline stand-in for Moodle's CodeRunner for proctored C practical exams in
Windows labs. Students paste C answers into a WinForms exe, which compiles and tests them with the
lab PC's own gcc, applies Moodle's penalty regime, and writes a sealed zip for grading.

## Where things are

- `PLAN.md`: **read this first.** What comes next, in order: the exam builder (it must work
  before PE03's date), then the standalone v2 app. It also lists the decisions the course owner
  has already made (do not re-ask them) and the open questions.
- `FORMAT.md`: every format v1 reads and writes, checked against the code, and format 2's
  proposed additions. Keep it in step with the code.
- `README.md`: packing, the day itself, and grading, for the course owner and proctors.
- `PRODUCT.md`: users, platform and brand. The design skill reads it.
- v1 as it was used for the PE02 re-sit, imported from the course repository:
  - `src/*.cs`, `LocalCodeRunner.csproj`, `app.manifest`: the exe (C# WinForms, `net48`).
  - `lcr.py`: the harness, penalty, crypto and receipt again in Python. `lcr.check` runs one
    answer against a variant's tests.
  - `pack.py`: builds, self-tests and packs an exam into the exe.
  - `grade.py`: reruns every recorded Check from the submissions.
- `private/`: gitignored. PE02's source material for the builder's acceptance test, copied from
  the course repository (VEB-DATASTRUCT); see `private/README.md`. Model answers stay here.

`pack.py`'s `DEFAULT_EXAM` still points into the course repository. Pass `--exam` explicitly
until it is fixed. A path such as `class-contents/...` means a folder in the course repository,
not in this one.

## Commands

Run from the repository root. You need the .NET SDK (any recent one, targeting `net48`), Python 3,
and gcc on PATH.

```powershell
$env:PYTHONIOENCODING = "utf-8"
# Trial pack with made-up students. This also runs the build and the self-test.
python pack.py --exam <exam.json> --sample-roster 5 --out <new empty folder>
# Real pack
python pack.py --exam <exam.json> --roster <class list.csv> --out <new empty folder> --title "..."
# Grade
python grade.py --pack <pack folder> --submissions <LMS download> --out <results> [--receipts receipts.csv] [--lab-saves <folders>]
```

- There is no separate test suite. **`pack.py`'s self-test is the test.** It builds the exe and
  runs about 65 answers through both the exe and `lcr.py`. It also compares the crypto, receipts,
  penalties and marks. Any disagreement, or a model answer that misses a test, stops the pack.
- `pack.py` treats a build warning as a failure. Keep the C# warning-free.
- Build through `pack.py`, which builds on a temp copy. A `dotnet build` in place leaves `bin/`
  and `obj/` at the root. They are gitignored, but still delete them afterwards.
- The exe has two headless modes that `pack.py` uses: `--selftest <in.json> <out.json>` and
  `--verify <password> <out.json>`.

## Rules for changing the code

- **One harness, written twice.** `src/Engine.cs`, `src/Crypto.cs` and `Session.PenaltyFor`/`MarkFor`
  must behave exactly like `lcr.py`. Change both sides in the same change, then run a trial pack
  to prove they still agree. (v2 may remove the Python copy, see `PLAN.md`.)
- **Target .NET Framework 4.8**, which ships with Windows 10, so the lab PCs need nothing
  installed. Its APIs do not include AES-GCM, which is why the crypto is PBKDF2 plus HMAC-SHA256
  in counter mode. Do not swap in APIs that are newer than net48.
- **Windows only.** Do not add macOS or Linux support.
- The statement and result panes are rendered by the WinForms `WebBrowser` control (IE11), so the
  CSS in `src/Html.cs` must work in IE11. Its palette repeats `src/Theme.cs`: change both together.
- The look must work at 1366 × 768 and make use of the space at 1920 × 1080.
- Compile line: `-Wall -Wextra -std=c17 -g -ftrack-macro-expansion=0`, linked with `-lm`. Each
  test runs as its own process with the exam's `time_limit`. Output is compared the way
  CodeRunner's exact match does it.
- Saves, packs and zips are versioned formats that are already in use. A v1 pack and its
  submissions must keep grading. Never change a format without bumping its version.

## Security model, and what must never be committed

**This repository is public.**

- The app holds no secret. An exam opens only with the start password. Never rely on a key
  hidden in the exe, because .NET decompiles in seconds.
- Model answers never go into anything students receive. `pack.py` strips `answer` before
  sealing, and that must stay true.
- Exam material for development (question files, model answers, instructor guides, answer
  keys) goes only in the gitignored `private/` folder.
- Never commit any of the following: an exam JSON (it holds model answers), a pack folder
  (`PASSWORDS.txt`, `pack.json`, `assignments.csv`, the packed exe), a class list or roster CSV,
  student submissions, `.save` files, or grading output. `.gitignore` covers the usual names.
  Check `git status` before every commit anyway.
- Never put the cloud between a student and Check, and never compile students' code in the cloud.

## Writing style

The docs here are short declarative sentences, written for the course owner and proctors rather
than for developers. Match that style when you edit them. Keep `README.md`, `FORMAT.md` and `PLAN.md`
accurate whenever the behaviour they describe changes.
