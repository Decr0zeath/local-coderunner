# Local CodeRunner

An offline stand-in for Moodle's CodeRunner, packed as one `.exe` per exam. Students download it
from the LMS just before the internet is cut. They write C in any editor, paste each answer into
the exe, and press Check. The exe compiles with the lab PC's own gcc and runs every test, the
hidden ones too. At Submit it writes a zip that students upload once the internet is back. The
course owner's `grade.py` reruns every Check in that zip.

First use: the Practical Exam 02 re-sit, with PE02's own questions. Why it exists, and why it is
an exe on each PC rather than a server in the lab, is in [`../LOCAL-Coderunner.md`](../LOCAL-Coderunner.md).
`PRODUCT.md` records who uses it and the decisions the course owner made.

## Files

| File | What it is |
|---|---|
| `pack.py` | Builds the exe, checks it against `lcr.py`, and packs an exam into it for one class list |
| `grade.py` | Grades the uploaded zips: reruns every Check, recomputes every mark, flags anything wrong |
| `lcr.py` | What `pack.py` and `grade.py` share with the exe: the test harness, the penalty, the crypto, the receipt |
| `src/` | The exe, C# WinForms on .NET Framework 4.8, which ships with Windows 10 |
| `LocalCodeRunner.csproj`, `app.manifest` | Its project; `pack.py` builds it on a copy, so `bin/` and `obj/` never land here |
| `PRODUCT.md` | The product record the design skill reads |

An exam's questions come from its own build script. PE02's are in
`../class-contents/150. Practical Exam 02/pe02-local-coderunner.json`, which `build-pracex.py`
writes beside the Moodle XML from the same statements and tests.

## What the exe does

- **Opening.** The questions, tests and class list are encrypted inside the exe. They open only
  with the start password, which the proctor writes on the board. The student then types their
  student number and confirms the name shown.
- **The draw.** `pack.py` draws one random variant of each question per student, so a student gets
  the same questions on every PC and after every restart. `assignments.csv` lists the draw.
- **Check.** It compiles the answer and every test into one program with the course flags,
  `-Wall -Wextra -std=c17 -g`, plus `-lm` as Jobe linked it. Each test then runs as its own
  process with a 3-second limit. Output is compared the way CodeRunner's exact match does,
  ignoring trailing spaces and trailing blank lines, with stderr appended. A `main()` the student
  pastes is renamed out of the way.
- **What a Check shows.** The example tests with expected and got, how many hidden tests
  passed, the mark after penalty, the best so far, and the next Check's cost. gcc's errors link
  to the line in the answer box.
- **Penalty.** The regime is `0, 3, 6, ...` from the exam JSON. Moodle's arithmetic: points ×
  (fraction passed − penalty), never below zero, and the best Check stands. A compile failure,
  or a broken rule such as Question 3's no-loop rule, counts as a Check. Check is greyed out
  until the answer changes.
- **The clock** starts when the student presses Start and keeps running while the exe is closed.
  At 00:00 the exe submits by itself.
- **Saving.** A sealed save goes to `Desktop\PE02\PE02-<student number>.save` after every change.
  A copy goes to `%LOCALAPPDATA%\LocalCodeRunner\`, and a registry marker remembers the first
  start. Deleting the saves therefore cannot restart the clock without the proctor.
- **Submit** grades every answer changed since its last Check as one more Check, as Moodle's
  "Submit all and finish" does. It then writes `Desktop\PE02\PE02-<student number>.zip` and
  shows a 12-character receipt code.

Two differences from CodeRunner on the LMS, both deliberate:

- There is no Precheck.
- A test that crashes or hangs fails alone. CodeRunner stops at the first one, so the tests
  after it fail too. The 11-01 grader, `pe02_grade.py`, already marked this way.

## What it protects, and what it cannot

- **Before the start:** nothing. Without the start password, the questions and tests in the exe
  are ciphertext (PBKDF2 with 200,000 rounds, then HMAC-SHA256 as a stream cipher and as a MAC).
  The one exception is a pack folder pushed before the sitting: GitHub Pages then serves its
  `PASSWORDS.txt` to anyone who has the path.
- **During the exam:** the hidden tests are in the exe's memory, and each test's code is briefly
  compiled into a program on the student's own PC. A student with a debugger, a decompiler and
  the know-how could get at them; the lab PCs are offline and proctored. PE02's tests are also
  already published on the course site, an exposure the course owner accepted.
- **After the exam:** the receipt code is a fingerprint of the zip's Check history. The student
  copies it onto the paper front page, and the proctor initials it against the screen. A zip
  edited at home no longer matches the paper. The seal inside the zip catches casual editing;
  the receipt catches the rest.
- **Marks:** never trusted from the lab PC. `grade.py` reruns every recorded Check on the course
  owner's PC and recomputes the penalties in order.

## Packing an exam

Needs the .NET SDK (any recent one; it targets .NET Framework 4.8), Python 3 and gcc on PATH.
The first build on a new machine downloads the .NET Framework reference assemblies from NuGet.

```powershell
$env:PYTHONIOENCODING = "utf-8"
python pack.py --roster <class list.csv> --out "..\class-contents\150. Practical Exam 02\instructors-only\resit-pack" --title "Practical Exam 02 · Re-sit"
```

- **The roster** is a CSV with a header row. `pack.py` finds the student-number column (a header
  mentioning ID, ID number or student number) and either a name column or last-name and
  first-name columns. If the guess is wrong, name the columns with `--id-column` and
  `--name-column "Last name+First name"`.
- **`--out`** must be a new or empty folder. It receives the exe, `PASSWORDS.txt` (start and
  proctor), `assignments.csv`, and `pack.json`, which holds everything `grade.py` needs, model
  answers and the class list included. The course owner keeps it in the exam's `instructors-only/`
  folder, as PE02's re-sit pack is in `instructors-only/resit-pack/`. Grading needs this exact
  folder, so never lose it.
- **A pack is one sitting.** Packing again makes new passwords and a new draw, and saves from an
  older pack do not open in a newer exe.
- **The self-test.** Before writing anything, the script builds the exe and runs about 65 answers
  through both the exe and `lcr.py`: model answers, a broken counter, a syntax error, a crash, a
  hang, a pasted `main()`, and Question 3's loop and recursion rules. Any disagreement, or a model
  answer that misses a test, stops it.
- `--sample-roster 5` packs made-up students, for a trial in an ordinary lab session.

## Before the day

1. Run a trial pack on one lab PC, from the LMS download, the way students will get it.
   - Windows may say "Windows protected your PC": More info, then Run anyway. If the lab
     blocks unsigned programs outright, the lab technician has to allow it.
   - The unlock screen says whether gcc was found on PATH.
   - If the lab PCs reset on reboot (Deep Freeze or similar), a PC that restarts loses its
     save. Then the PC-dies route below is the only rescue, before the restart if possible.
2. Pack the real exam with the class list. Upload the exe to the LMS hidden, and show it just
   before the internet goes off.
3. Make an LMS assignment that takes one `.zip` file.
4. Print `class-contents/150. Practical Exam 02/resit-front-page/pracex02-resit-front-page.pdf`,
   one per candidate. It has the receipt boxes.

## On the day

| What happens | What the proctor does |
|---|---|
| The start | Write the start password on the board. Never show the proctor password |
| A student needs more time | Proctor in the exam window, type the proctor password, add minutes |
| A student submitted by mistake | Proctor on the receipt screen, then add time if the clock is out, then Reopen. They submit again and get a new receipt |
| A PC dies | Copy `Desktop\PE02\PE02-<student number>.save` to the same folder on a spare PC, open the exe there, and type the proctor password when asked. The clock resumes where the save left it; add minutes for the time lost |
| "Your save file is missing" | The student deleted or lost it. The proctor password starts them again with the same questions, no answers, and the clock running from their first start. It is logged |
| gcc not found | Nothing on that PC can be checked, and no Check is counted. Move the student |
| Collecting | Before each student leaves, compare the receipt on the front page with the screen and initial it |

## After the exam

Once the internet is back, students upload their zip. Download all submissions from the LMS,
type the receipts from the front pages into a CSV with `id,receipt` columns, and run:

```powershell
python grade.py --pack "..\class-contents\150. Practical Exam 02\instructors-only\resit-pack" --submissions <LMS download> --out <results folder> --receipts <receipts.csv>
```

It reads the LMS's own download zip as is, and finds a submission however a student wrapped
it: inside another zip, extracted and zipped again, or the `.save` sent instead, which counts
only when no zip came. Copies with the same receipt count once. Files that held no submission
are listed at the end, by student.

Leave an empty receipt box blank in the CSV. For those students, copy `Desktop\PE02` off the lab
PCs before they are reset (the `lab PC` column, and the end of the run, say which) and add
`--lab-saves <copied folders>`: a lab PC's save never left the room, so its receipt stands in
for the paper's. A student who uploaded nothing, but whose lab PC holds a zip, is named at the
end; adding the copied folders to `--submissions` grades that zip.

It writes `resit-scores.csv`, with marks and every
flag; `resit-checks.csv`, with every Check recomputed; `resit-feedback.html`, each student's
Checks to paste into the LMS; and `answers/<name>/<variant>.c`, the counted answer in the layout
`pe02_grade.py` reads. `--pity-enabled` applies the same 20% floor as the 11-01 grader.

What a flag means:

- **Seal does not match, or the receipt on the front page is not this zip's.** The zip was
  changed after the exam. Mark from the paper receipt's zip, or not at all. The same goes for
  a lab PC's receipt.
- **The receipt is 1 or 2 characters off.** A copying slip: a changed zip cannot land that
  close. Look at the handwriting.
- **The lab PC recorded something else for a Check.** Its gcc behaved differently from yours.
  Rare; look at that Check by hand.
- **Different zips for this ID.** Keep the one whose receipt is on the front page.
- **Graded from a .save.** The student sent the save, not the zip. Its seal and receipt are
  checked the same way.
- **Proctor events.** Added time, a move, a reopen or a restart, each with its time.

## Changing the exe

The exe and `lcr.py` implement the same harness, penalty and crypto, and `pack.py` refuses to
pack when they disagree. Change both sides together. Build only through `pack.py`, or by
copying the project elsewhere: a build in this folder leaves `bin/` and `obj/` behind.

The window carries the course's question-card look (`PRODUCT.md`, Brand Commitments): the
palette in `src/Theme.cs`, repeated as CSS in `src/Html.cs` for the IE11-rendered statement and
result panes.
