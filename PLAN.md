# Plan

What comes next for Local CodeRunner, and the decisions already made. Nothing here is built
unless it says so. The file formats are in `FORMAT.md`. How to use v1 is in `README.md`.

This file replaces `ForNewRepo.md` (2026-11-11) and `LOCAL-Coderunner.md` (2026-11-01), which
are in git history, and the exam builder brief of 2026-11-18, which is kept in `private/`.

## Where things stand, 2026-11-18

- v1 ran the PE02 re-sit on 2026-11-11: one exe per exam, fully offline. The course owner: it did
  what was wanted. It was then moved out of the course repository into this one.
- The next sitting is PE03. Its date is not set. Its questions will be written once the exam
  builder works, so **the exam builder must work before PE03's date**.
- PE03 to PE06 cover linked lists and a circular queue; traversals, a BST comparison count and
  extracting the maximum; rotations, a chained hash table and AVL insertion; graph traversal,
  Kruskal and backtracking. Format 1 cannot express all of that.

## Order of work

Before PE03:

1. **The format spec.** `FORMAT.md`: format 1 is written down as it is. Settle format 2's
   additions while writing the builder.
2. **The question builder:** variants, questions, their checks, the defect scoring.
3. **The exam builder:** the exam checks, the JSON, the instructor guide and the answer key.
4. **The PE02 acceptance test.**
5. **The app reads format 2.** `pack.py` refuses a `format` it does not know. Its self-test is
   written for PE02's answers today (`self_test_cases` in `pack.py` edits PE02's model answers to
   make its wrong ones), so generalise it, then add a multi-function answer, a hidden `malloc`
   counter, and a student who redefines a given struct.

PE03 is then written with the builder and sat with v1's per-exam exe.

After PE03:

6. **The standalone app and the exam file.** Same features as v1. Done when one app binary opens
   two different exam files, the self-test passes, and grading a sitting gives the same marks v1
   would.
7. **The re-sit fixes:** the receipt typed back, Show in folder, drag to upload.
8. **Automatic upload with personal codes.** This removes what went wrong at the re-sit.
9. **Live sync and the dashboard**, only if IT can provide an allow-listed network.
10. **Grades into the LMS**, once a token is available.

Steps 8 onward make it a product to maintain, so decide first how many more exams it will serve.

Left over from the move: tag v1 as `v1-per-exam-exe`, and fix `pack.py`'s `DEFAULT_EXAM` and the
usage examples at the top of `pack.py` and `grade.py`, which still point into the course
repository. Until then, pass `--exam`.

## The exam builder

### Why

Every practical exam so far has its own `build-pracex.py` in the course repository. PE02's is
1,442 lines, and most of it is not about PE02:

- the questions, their Python oracles and their seeded defects (PE02's own);
- a compile-and-run checker, the mark arithmetic and the defect scoring (the same for every exam);
- a Moodle XML writer, with a Twig imitation that reads the XML back (no longer needed);
- writers for the instructor guide, the answer key and the exam JSON (the same for every exam,
  apart from their wording).

Copying the script for each of four more exams means four copies of the checker drifting apart.

The script also checks model answers with its own harness, not the app's. It compiles without
`-ftrack-macro-expansion=0` and `-lm`, and does not set aside a pasted `main()`. A question
should be checked by the harness that will mark it.

### What lives where

| This repository | The course repository, one exam folder each |
|---|---|
| The builder: a library and a command line | The question files: statements, tests, model answers, oracles, seeded defects |
| The format spec, `FORMAT.md` | What the builder writes: the exam JSON, the instructor guide, the answer key |
| `lcr.py`, `pack.py`, `grade.py`, the exe | The front page, the web page, the README, and the packs |

The course repository is the record of what was set, and its rules keep generated exam material
there. The builder reads question files from a path and writes into a path, as `pack.py --exam`
does. Use full paths.

### How it is built

- Python, standard library only, run on Windows with `PYTHONIOENCODING=utf-8`. The oracles are
  Python, and `lcr.py` already is the harness in Python.
- **One harness.** The builder checks through `lcr.check` (`lcr.py`), which compiles with the
  exe's flags and sets aside a pasted `main()`. `pack.py`'s self-test, which runs answers through
  both the exe and `lcr.py`, keeps the two harnesses from drifting.
- **`expect` comes from an independent Python oracle**, never from running the model answer.
  Otherwise a wrong model answer certifies itself.
- **All or nothing.** Any failure writes nothing and exits 1.

### Layers and their checks

| Layer | Holds | Checks |
|---|---|---|
| Variant | statement, functions, support code, preload, model answer, rules, tests with marks | the model answer and its support code compile without a warning; every test passes under `lcr.check`; the rules accept the model answer; the marks add up to the points |
| Question | its variants, points, question code, seeded defects | every variant has the same number of tests, with the same marks and the same shown pattern, so the draw is fair; each defect changes the answer, and its score is recorded |
| Exam | its questions, code, title, minutes, penalty, time limit | numbers run 1 to n; IDs are unique; the total is right; the exported JSON is read back and checked against `FORMAT.md` |

### What it writes

- The exam JSON, the input to `pack.py`.
- The instructor guide, `instructors-only/README.md`: every variant with its statement, model
  answer, test table, and what each seeded defect scores.
- The answer key, `instructors-only/pracex<NN>-answer-key.html`, in the course's lab-key style.
  Take PE02's template (`KEY_PAGE` in its `build-pracex.py`) as the default.

Statements are written in a small markup, backticks for code and `**` for bold, as PE02's script
does (`inline_html`). The builder turns them into format 1's HTML paragraphs.

### A sketch of a question file

The names are illustrative; settle them while writing the builder.

```python
from lcr_build import Exam, Question, Test

def last_index(a, n, x):           # the oracle: index and comparison count
    count = 0
    for i in range(n - 1, -1, -1):
        count += 1
        if a[i] == x:
            return i, count
    return -1, count

q1 = Question(number=1, code="SEQ", points=10,
              title="Sequential search with a comparison counter")
q1.variant("A", functions=["int last_index_of(const int badges[], int n, int key, int *comparisons)"],
           statement=["A door records badge numbers ...", "Set `*comparisons` to 0 ..."],
           answer=MODEL_A, tests=[Test(label="a match in the middle", code=..., expect=..., mark=1, shown=True), ...])
q1.defect("A", "the loop stops at `i > 0`", lambda a: a.replace("i >= 0", "i > 0"))

exam = Exam(code="PE03", title="Practical Exam 03", minutes=60, penalty="0, 3, 6, ...",
            time_limit=3, questions=[q1, q2, q3])
```

Run it as, for example:

```powershell
python lcr_build.py --exam "<full path>\exam.py" --out "<full path to the exam folder>"
```

Test generators, such as PE02's `counted_tests`, which writes each test's C code and gets its
`expect` from the oracle, stay in the question files. They are particular to a question.

### Kinds of question format 2 must allow

- **Build a structure.** The node struct and `printList` are a shown support block; the tests
  pass arrays.
- **Debug given code.** The broken code is the `preload`. The tests cannot tell a fix from a
  rewrite, so a clean rewrite scores full marks.
- **Several functions in one answer**, such as a queue whose tests fill, drain and refill it
  across the wrap.
- **Memory checks.** AddressSanitizer does not work on the course toolchain. A hidden support
  block can wrap `malloc` and `free` with a live count that tests check is zero after a list is
  freed. If the wrapper stores each block's size in front of it, `free` can also overwrite the
  block, so a use after free fails every time instead of passing by luck. Each test already runs
  as its own process, so a crash costs only that test.

### Acceptance test: rebuild PE02

PE02's source material is in `private/PE02/source/`, copied from the course repository. `private/`
is gitignored: it holds model answers, and this repository is public.

1. Re-express PE02's three questions (12 variants, 16 seeded defects) as question files in
   `private/PE02/`, from `private/PE02/source/build-pracex.py`.
2. The export must equal `private/PE02/source/pe02-local-coderunner.json` field by field after
   parsing. Any difference is either a bug or written down with its reason.
3. Every defect score must equal the table that ends `private/PE02/source/instructors-README.md`,
   to one decimal.
4. **Never pack PE02 again.** Packing makes new passwords and a new draw, and the re-sit's zips
   grade only with the pack they were sat with.

## The standalone app (v2)

### The requirement from the re-sit

Students did not follow the paper instructions. Many left the receipt boxes on the front page
empty, many could not find `Desktop\PE02`, and many had a hard time uploading the zip to the LMS.
`grade.py` was changed the same day to cope (see `README.md`, "After the exam").

So: **students should not handle files or copy codes by hand.** Whatever must happen, the app
does it or will not go on without it.

### The course owner's idea

One program, installed once. For each exam the teacher makes a file that only the program can
read. It holds everything about the exam: the students expected, the questions and their
randomness, and every setting. No new exe per exam. Possibly a cloud feature as well.

### Recommended shape

1. **The app is v1's exe without the payload.** It already reads its exam from its own tail
   (`Pack.ReadFrom(Application.ExecutablePath)` in `Program.cs`). Reading the same bytes from a
   chosen file (an Open button, a file association, or a command-line argument) is a small
   change.
2. **The exam file** (suggested extension `.lcrexam`) is v1's pack as a file of its own.
3. **The teacher's file** is v1's `pack.json`, with `PASSWORDS.txt`. It never goes to students.
4. **One harness.** v1 has the harness, penalty and crypto twice, in C# and in `lcr.py`, and
   `pack.py` exists partly to catch drift. Building the teacher side from the same C# code would
   leave one implementation. If Python stays for authoring, keep the self-test.
5. **"Only the program can read it" cannot be the protection.** .NET decompiles in seconds, so
   any key inside the app is public. The app holds no secret, the exam file opens only with the
   start password, and model answers never ship to students.
6. **Version everything** (`FORMAT.md`, "Versions"), so a v1 exam opens in v2 and grading uses
   the matching harness.
7. **The roster.** Every student's file carries the whole class list, readable once the start
   password is known. Acceptable for now; worth reducing later.
8. **Installing.** The lab technician installs or allow-lists one binary once, into the lab image
   if the PCs reset. A fixed binary also makes code signing worth considering; today every pack
   is a new unsigned exe.

### Fixes from the re-sit

Needed with or without the cloud:

- **The receipt typed back.** After Submit, the app shows the code, hides it once the student
  says it is written down, and shows the marks only when the student types it back from the
  paper. Students want their marks, so they write it down.
- **No folder hunting.** A Show in folder button on the receipt screen, and a file tile the
  student drags straight into the LMS upload box.
- **Upload in the room.** The internet comes back once everyone has submitted. The proctor takes
  the front page only after seeing the receipt filled in and the LMS showing Submitted.
- **The front page.** Short numbered "After you Submit" steps beside the receipt boxes.

### Cloud

**The cloud never sits between a student and Check.** PE02 failed on 2026-11-01 because an
outside server was overloaded. Compiling and running stay on the lab PC, the app works with no
network at all, and the cloud is only where results end up.

In order of value:

1. **Automatic upload.** The app uploads the submission itself as soon as it is online, retries
   until it succeeds, and shows "Uploaded" with the time.
2. **Live sync on an allow-listed network.** If IT can block the internet except for one exam
   server, each Check is sent as it happens. The server becomes the record, so no receipt is
   needed; a dead PC resumes on another PC with no USB; and a proctor dashboard shows who
   started, the time left, and who has not submitted. If the network drops, the app carries on
   and catches up.
3. **Grades into the LMS** through Moodle's web services. Needs a token from the site admin.

**Identity.** Once uploads go to a server, the start password is not enough: everyone in the room
knows it. Print a personal code on each student's front page, and have the app ask for it at
Start. Nobody can begin without it, so it cannot be skipped the way the receipt was.

**Hosting.** One teacher's scale fits a free tier. Supabase gives a database, file storage,
logins and a table view usable as the dashboard. Cloudflare Workers with R2 is lighter if only
uploads are needed. Check first that the campus network reaches it, and the university's position
on student data with an outside service under the Data Privacy Act (RA 10173). Store only ID,
name and code.

**Never compile in the cloud.** Running students' C on a server needs careful isolation, and it
is the Jobe load problem again.

## Decisions already made

Settled by the course owner. Do not re-ask them.

- Local CodeRunner from now on, not Moodle's CodeRunner.
- Fully offline lab PCs. Students get the program from the LMS just before the internet goes off.
- A start password announced in the room.
- One random variant per question, fixed by student ID, shown in order.
- Answers are pasted in, not loaded from files.
- No Precheck. Check shows the example tests with expected and got, the hidden-test count
  passed, and the mark with its penalty.
- The `0, 3, 6, ...` regime, best Check kept. Failed compiles and rule breaks count as Checks. A
  no-loop rule reads comments too, as Moodle's did.
- The time limit is enforced. An unchecked changed answer is graded at Submit.
- Rescue: a proctor password adds time or reopens an exam, and saves move by USB when a PC dies.
- The look is the course's question-card look from the PE02 LMS questions (`PRODUCT.md`).
- Model answers never reach students: `pack.py` removes them when packing.

Options weighed on 2026-11-01 and set aside:

| Option | Why not |
|---|---|
| Keep the LMS's CodeRunner | It sends every run to a Jobe server. The default one, at the University of Canterbury, allows 60 runs an hour per Moodle site, and changing it needs a Moodle admin |
| A server on one lab PC, under Safe Exam Browser | Planned first, then replaced on 2026-11-11 by the exe on each PC: no host PC, admin rights, network test or separate account needed, and no student code running on a shared host |
| Moodle, CodeRunner and Jobe in Docker | A whole Moodle to run, and Jobe's Linux gcc has a 64-bit `long` where the lab's Windows gcc has 32, so results can differ |

## Open questions for the course owner

- PE03's date.
- Whether the re-sit fixes (receipt typed back, Show in folder) must be in v1's exe for PE03, or
  can wait for v2.
- The builder: whether it still writes Moodle XML. Recommended: no.
- Authoring in Python, as above, or the teacher side in C# for one harness. Recommended: Python,
  with `pack.py`'s self-test guarding the two harnesses.
- Whether the statement markup stays backticks and bold, or becomes full Markdown.
- The exam file's extension and the app's name.
- Installed or portable, and who puts it on the lab PCs.
- Cloud: will IT allow-list one server, what the university allows for student data, and which
  host.
- Personal codes printed on each student's front page: acceptable?
- Whether the roster stays in the student's file.
- Still open from PE02's `content-queries.md`: confirming that a pasted `main()` is set aside and
  that a changed answer is graded at Submit.
- Laboratory Exercise 12 holds CodeRunner questions, which would hit the same limit on the LMS.
  Should the builder take them on?

## Do not

- Put the cloud between a student and Check, or compile students' code in the cloud.
- Rely on a key hidden in the app.
- Put model answers in anything students get, or in this repository outside `private/`.
- Pack PE02 again, or write into the course repository's
  `class-contents/150. Practical Exam 02/instructors-only/pack-2026-11-11/`.
- Add macOS or Linux support. The labs are Windows 10.
