# Local CodeRunner as its own repository

A brief for moving `local-coderunner/` out of the DATASTRUCT course repository and turning it
into a standalone offline exam app. Written 2026-11-11, the day the PE02 re-sit was sat with it.
The first four sections describe what exists. Everything from "The new design" on is a plan:
none of it is built yet.

## Why it exists

- **2026-11-01, Practical Exam 02.** It ran as a Moodle quiz with three CodeRunner questions,
  under Safe Exam Browser. Precheck and Check came back rate-limited for the whole sitting.
  CodeRunner compiles nothing on the Moodle site: it sends every run to a Jobe server, and the
  default one (University of Canterbury, meant for testing) allows 60 runs an hour per Moodle
  site. The real fix, the university running its own Jobe, is a Moodle admin's job.
- **2026-11-11, the re-sit.** Local CodeRunner was built for it: one `.exe` per exam, running
  fully offline on each lab PC, with PE02's own questions and a class list of 133 students.

The full record of the 11-01 failure and the options weighed is `LOCAL-Coderunner.md` at the root
of the course repository.

## How the re-sit went

The course owner: the exe did what was wanted. Students did not follow the paper instructions:

- many left the receipt boxes on the printed front page empty;
- many could not find `Desktop\PE02`, where the exe writes the zip;
- many had a hard time uploading the zip to the LMS.

This is the main requirement for the new design: **students should not handle files or copy
codes by hand.** Whatever must happen, the app does it or will not go on without it.

`grade.py` was changed the same day to cope: it finds a submission however it was wrapped (zip
in a zip, extracted and zipped again, a `.save` sent instead), treats a blank receipt as missing
rather than wrong, and takes `--lab-saves`, the `Desktop\PE02` folders copied off the lab PCs,
in place of a missing paper receipt.

## What to copy

Everything in `local-coderunner/`:

| File | What it is |
|---|---|
| `src/*.cs` | The exe: C# WinForms on .NET Framework 4.8, which ships with Windows 10 |
| `LocalCodeRunner.csproj`, `app.manifest` | Its project (net48, deterministic build) |
| `lcr.py` | The harness, penalty, crypto and receipt again in Python, for `pack.py` and `grade.py` |
| `pack.py` | Builds the exe, self-tests it against `lcr.py`, packs an exam and class list into it |
| `grade.py` | Reruns every recorded Check from the uploaded zips and recomputes every mark |
| `README.md` | How to pack, run the day, and grade |
| `PRODUCT.md` | Users, purpose and brand commitments, for the design skill |

Also take `LOCAL-Coderunner.md` (history) and an exam to develop against. PE02's is
`class-contents/150. Practical Exam 02/pe02-local-coderunner.json`. **It holds the model
answers**, so it only goes into a private repository.

Do not copy `class-contents/150. Practical Exam 02/instructors-only/resit-pack/`. It holds the
start and proctor passwords, the names and ID numbers of 133 students, and the model answers.
It stays where it is until the re-sit is graded.

## How v1 works

### The flow

- **Packing.** `pack.py --roster <csv> --out <new folder>` builds the exe, runs about 65 answers
  through both the exe and `lcr.py` (model answers, a syntax error, a crash, a hang, a pasted
  `main()`, the loop rule) and stops on any disagreement. It then draws one variant of each
  question per student, makes a start password and a proctor password, and appends the encrypted
  exam to the exe. Packing again makes new passwords and a new draw.
- **Opening.** The student types the start password (written on the board), then their student
  number, and confirms the name shown. The 60 minutes start at Start and keep running while the
  window is closed.
- **Check.** The answer is pasted in. The exe compiles the answer and every test into one program
  with the lab PC's own gcc (`-Wall -Wextra -std=c17 -g -ftrack-macro-expansion=0`, linked with
  `-lm` as Jobe did), runs each test as its own process with a 3-second limit, and compares
  output the way CodeRunner's exact match does. A pasted `main()` is renamed out of the way.
- **Penalty.** Moodle's adaptive regime, `0, 3, 6, ...`: mark = points × (fraction passed −
  penalty), never below zero, best Check stands. A failed compile or a broken rule counts.
- **Saving.** A sealed save to `Desktop\<code>\<code>-<id>.save` after every change, a copy in
  `%LOCALAPPDATA%\LocalCodeRunner\<pack>\`, and a registry marker of the first start.
- **Submit**, or 00:00. Any answer changed since its last Check is checked once more. The exe
  writes `Desktop\<code>\<code>-<id>.zip` and shows a receipt code.
- **Proctor password.** Adds minutes, reopens a submitted exam, resumes a save moved to another
  PC by USB, and restarts a student whose save is lost. Every use is logged.
- **Grading.** `grade.py` reruns every Check on the teacher's PC, flags any difference from what
  the lab PC recorded, compares the receipt, and writes the scores, every Check, the LMS feedback
  HTML, and the counted answers as `.c` files.

### Formats

- **Exam source** (written by the course repo's `build-pracex.py`): `format`, `code`, `title`,
  `minutes`, `penalty` (a Moodle regime string), `header` (the `#include` lines), `time_limit`
  (seconds per test), and `questions[]` of `number`, `title`, `points`, `variants[]`. A variant
  has `id`, `v` (its letter), `fn`, `statement` (HTML paragraphs), `signature`, `preload`,
  `answer` (the model answer), `rules[]` of `{kind: forbid|require, pattern, message}`, and
  `tests[]` of `{label, code, expect, mark, shown}`.
- **The pack**, appended to the exe: `header JSON | body | tag (32) | u32 header length | u64
  body length | "LCRPACK1"`. The header holds `format`, `title`, `iterations`, `salt`, `nonce`;
  only the title is readable without the password. Key = PBKDF2-SHA256(start password, salt,
  200,000 rounds, 64 bytes): the first half encrypts, the second half authenticates. Body =
  gzip(bundle) XOR a keystream whose block *i* is HMAC-SHA256(key, nonce ‖ u64 *i*). Tag =
  HMAC-SHA256(MAC key, header ‖ body). Neither the Python standard library nor .NET Framework
  4.8 has AES-GCM, which is why it is built this way.
- **The bundle**: `format`, `pack` (id), `title`, `seal_key`, `proctor` (salt, iterations and
  hash of the proctor password), `exam` (the source with every `answer` removed), and `roster[]`
  of `{id, name, draw}`.
- **The save**: `LCRSAVE1 <hex HMAC(seal_key, body)>\n` then the state JSON, with the receipt
  added after Submit.
- **The zip**: `history.json` (the state: draw, times, machine name, every Check with its code,
  events), `seal.txt` (hex HMAC(seal_key, history)), `receipt.txt`, and the counted answer of
  each question as `<variant id>.c`.
- **The receipt**: the first 60 bits of SHA-256(history.json) in Crockford base 32, as
  `XXXX-XXXX-XXXX`. Passwords use the same alphabet, which has no I, L, O or U; typed input maps
  O to 0 and I and L to 1.

### What protects what

- Before the start password: everything is ciphertext.
- During the exam: the hidden tests are in memory on an offline, proctored PC. A student with a
  decompiler and debugger could reach them. PE02's tests were already public; the owner accepted
  this.
- After the exam: the seal key is inside the pack, so the seal stops only casual editing. The
  receipt on paper is the real check. Marks from the lab PC are never trusted.

## Decisions the course owner has already made

Carry these over; do not re-ask them.

- Fully offline lab PCs. Students get the program from the LMS just before the internet goes off.
- A start password announced in the room. A random variant per question, fixed by student ID,
  shown in order Q1 to Q3. Answers are pasted into the program, not loaded from files.
- No Precheck. Penalty regime `0, 3, 6, ...`, best Check kept. Failed compiles and rule breaks
  count as Checks. Question 3's no-loop rule includes comments, as Moodle's did.
- Check shows the example tests with expected and got, the hidden-test count passed, and the
  mark with its penalty.
- The 60 minutes are enforced. An unchecked changed answer is graded at Submit.
- Rescue: a proctor password adds time or reopens an exam, and saves move by USB when a PC dies.
- The look is the course's question-card look from the PE02 LMS questions (palette in
  `src/Theme.cs`, repeated as CSS in `src/Html.cs` for the IE11-rendered panes).

## The lab and the dev machine

- Lab PCs: Windows 10, MinGW-w64 GCC 15.2.0 on PATH, C17. They may reset on reboot (Deep Freeze
  or similar). Windows SmartScreen warns on an unsigned exe. Screens range from 1366 × 768 to
  1920 × 1080.
- Course compile line: `gcc -Wall -Wextra -std=c17 -g file.c -o file.exe`.
- Building needs any recent .NET SDK (it targets net48; the first build downloads the reference
  assemblies from NuGet), Python 3 and gcc. Build through `pack.py`, or in a copy of the folder:
  a build in place leaves `bin/` and `obj/` behind.
- Everything the students see is Windows only. No macOS or Linux.

## The new design

### The owner's idea

One standalone program, installed once. For each exam the teacher makes a file that only the
program can read. It holds everything about the exam: the students expected, the questions and
their randomness, and every setting. No new `.exe` per exam. Possibly a cloud feature as well.

### Recommended shape

1. **The app is v1's exe without the payload.** It already reads its exam from its own tail
   (`Pack.ReadFrom(Application.ExecutablePath)` in `Program.cs`). Reading the same bytes from a
   chosen file instead (an Open button, a file association, or a command-line argument) is a
   small change, not a redesign.
2. **The exam file** (suggested extension `.lcrexam`) is v1's pack as a file of its own:
   questions without answers, tests, the roster with each student's draw, the settings, the seal
   key and the proctor hash, encrypted under the start password.
3. **The teacher's file** is v1's `pack.json`: model answers, seal key, roster and draw, plus the
   passwords. It never goes to students.
4. **One harness.** v1 implements the harness, penalty and crypto twice (C# and `lcr.py`) and
   `pack.py` exists partly to catch drift. Build the teacher side (making exam files, grading)
   from the same C# code, as a teacher mode or a command-line tool, so there is one
   implementation. If Python stays for authoring, keep the self-test.
5. **"Only the program can read it" cannot be the protection.** .NET decompiles in seconds, so
   any key inside the app is public. Keep v1's rule: the app holds no secret, the exam file opens
   only with the start password, and model answers never ship to students.
6. **Version everything.** A format number in the exam file, and the app version recorded in
   every submission, so a v1 exam opens in v2 and grading uses the matching harness.
7. **The roster.** Every student's file carries the whole class list, readable once the start
   password is known. Acceptable for now; worth reducing later.
8. **Installing.** The lab technician installs or allow-lists one binary once, into the lab
   image if the PCs reset. A fixed binary also makes code signing worth considering, since
   today every pack is a new unsigned exe.

### Fixes from the re-sit, needed with or without the cloud

- **Receipt typed back.** After Submit, the app shows the code, hides it once the student says
  it is written down, and shows the marks only when the student types it back from the paper.
  Students want their marks, so they write it down.
- **No folder hunting.** A Show in folder button on the receipt screen, and a file tile the
  student drags straight into the LMS upload box.
- **Upload in the room.** The internet comes back once everyone has submitted, and the proctor
  takes the front page only after seeing the receipt filled in and the LMS showing Submitted.
  No code needed.
- **Front page.** Short numbered "After you Submit" steps beside the receipt boxes.

### Cloud

**The rule: the cloud never sits between a student and Check.** 11-01 failed because an outside
server was overloaded. Compiling and running stay on the lab PC, the app works with no network
at all, and the cloud is only where results end up.

In order of value:

1. **Automatic upload.** The app uploads the submission itself as soon as it is online, retries
   until it succeeds, and shows "Uploaded" with the time. No folder, no LMS form.
2. **Live sync on an allow-listed network.** If IT can block the internet except for one exam
   server, each Check is sent as it happens. The server becomes the record, so no receipt is
   needed; a dead PC resumes on another PC from the server, with no USB; and a proctor dashboard
   shows who started, the time left, and who has not submitted. If the network drops, the app
   carries on and catches up.
3. **Grades into the LMS** through Moodle's web services. Needs a token from the site admin.

**Identity.** Once uploads go to a server, the start password is not enough: everyone in the
room knows it, so anyone could upload as anyone. Reverse the paper's role: print a personal code
on each student's front page, and have the app ask for it at Start. Paper to app is enforced,
because nobody can begin without it. App to paper, today's receipt, depended on students, and
that is what failed.

**Hosting.** One teacher's scale fits a free tier. Supabase gives a database, file storage,
logins and a table view usable as the dashboard. Cloudflare Workers with R2 is lighter if only
uploads are needed. Check first that the campus network reaches it, and the university's position
on student data with an outside service under the Data Privacy Act (RA 10173). Store only ID,
name and code.

**Never compile in the cloud.** Running students' C on a server needs careful isolation, and it
is the Jobe load problem again.

## Roadmap

0. **Grade the PE02 re-sit with v1 as it is**, from the course repository: it needs the
   `resit-pack/` folder and today's `grade.py`. Then import v1 into the new repository and tag it
   (say `v1-per-exam-exe`).
1. **Standalone app and exam file.** Same features as v1. Done when one app binary opens two
   different exam files, the self-test passes, and grading a sitting gives the same marks v1
   would.
2. **The re-sit fixes:** receipt typed back, Show in folder, drag to upload.
3. **Automatic upload with personal codes.** This is the step that removes what went wrong at
   the re-sit.
4. **Live sync and the dashboard**, only if IT can provide the allow-listed network.
5. **Grades into the LMS**, once a token is available.

Step 1 is contained. Steps 3 onward make it a product to maintain, so decide first how many more
exams it will serve.

## Moving it

1. Finish step 0 first.
2. Create a **private** repository, since the exams it holds carry model answers.
3. Copy the files listed in "What to copy". No `bin/` or `obj/` exist to leave out.
4. Fix the paths that point into the course repository: `pack.py`'s `DEFAULT_EXAM`, and the
   README's links to `../LOCAL-Coderunner.md` and `../class-contents/...`.
5. Back in the course repository, update what points at `local-coderunner/`: the root
   `README.md`, `LOCAL-Coderunner.md`, the PE02 `README.md`, `build-pracex.py` (it stays there as
   the exporter of the exam source), the re-sit front page's `.tex` comments and `preamble.tex`,
   and PE02's `content-queries.md`.

## Open questions for the owner

- The exam file's extension and the app's name.
- Installed or portable, and who puts it on the lab PCs.
- The next exam it should serve, and its date: that sets how far to go before it.
- Cloud: will IT allow-list one server, what the university allows for student data, and which
  host.
- Personal codes printed on each student's front page: acceptable?
- The teacher side in C# (one harness) or kept in Python.
- Whether the roster stays in the student's file.
- Still open from PE02's `content-queries.md`: confirming that a pasted `main()` is set aside and
  that a changed answer is graded at Submit.

## Do not

- Put the cloud between a student and Check, or compile students' code in the cloud.
- Rely on a key hidden in the app.
- Put model answers in the file students get.
- Repack the PE02 re-sit: that changes its passwords and draw, and `grade.py` must grade against
  the exe students actually used.
- Add macOS or Linux support; the labs are Windows 10.
