# Local CodeRunner

Notes from 2026-11-01, after Practical Exam 02, on why PE02's CodeRunner failed and the options
weighed. The plan they settled on, a server on one lab PC under Safe Exam Browser, was replaced
on 2026-11-11 by what is now built in [`local-coderunner/`](local-coderunner/README.md): one
`.exe` on each student PC, with the internet and the lab network off. The next section says what
changed and why; the server plan is kept below it as the record.

## What was built instead, 2026-11-11

For the PE02 re-sit, the course owner chose a fully offline sitting. Students write in any IDE
and Check in an exe that holds the questions, the hidden tests and the class list. The exe
draws a variant per student, enforces the 60 minutes, and applies the `0, 3, 6, ...` penalty
with no Precheck. At the end it writes a zip that students upload once the internet is back.

The two objections to an exe on each PC, in "Options set aside" below, are met this way:

- **The hidden tests ship in the exe.** They are encrypted under a start password that the
  proctor writes on the board, so a copy downloaded early holds nothing usable. During the
  sitting they can still be reached with a debugger on an offline, proctored PC. PE02's own tests
  are already public on the course site, an exposure the course owner accepted.
- **The answers end up on every PC.** Each student uploads a zip, and a receipt code on the
  paper front page ties the zip to what the exe saw at Submit. `grade.py` reruns every Check on
  the course owner's PC and trusts no mark worked out in the lab.

The server plan's own requirements no longer apply: a host PC with admin rights, the network
test, Safe Exam Browser, and a separate Windows account. Neither does the risk of running
student code on a shared host, since each answer now runs on the student's own PC.

## What happened at PE02

- PE02 ran as a Moodle quiz under Safe Exam Browser: three CodeRunner questions, four variants
  each (`class-contents/150. Practical Exam 02/`).
- Precheck and Check came back rate-limited, so nobody could run code for the whole sitting.
- The LMS still kept every answer box. The answers were copied by hand, one per student, into
  `.c` files, and `instructors-only/grader/pe02_grade.py` marked them with the Moodle tests, with
  no penalty and `--pity-enabled`.

## Why it failed

CodeRunner compiles nothing on the Moodle site. Every Precheck and Check is sent to a Jobe
server. Out of the box, CodeRunner uses a Jobe server at the University of Canterbury that is
meant for testing only, and its default key allows 60 runs per hour per Moodle site. A rate
limit is what that key gives, so the university's Moodle is almost certainly still on it.

The limit cannot be designed around. A question whose template runs every test in one program
uses one run per Check, but only while the answer runs cleanly: an answer that crashes makes
CodeRunner rerun each test on its own, 12 runs for Question 2. A full room passes 60 in minutes.

The real fix is the university running its own Jobe server. That is a site-admin setting, and
the course owner is not a Moodle admin, so it is IT's to do. It can be asked for; the plan below
does not wait on it.

Sources: [CodeRunner documentation](https://stack-demo.maths.ed.ac.uk/demo/question/type/coderunner/docs/index.md),
[qtype_coderunner on moodle.org](https://moodle.org/plugins/qtype_coderunner?lang=ca).

## The first plan, set aside 2026-11-11

Kept as the record of what was weighed. Nothing in this section was built.

Build our own CodeRunner: one program, packaged as an `.exe`, that serves the exam over the lab
network from one PC. Students still sit under Safe Exam Browser, with the internet available but
everything except the exam blocked. Safe Exam Browser is a locked browser that opens whatever
site its configuration names, so it opens this server instead of the LMS. Students install
nothing else.

### What students see

The same screens as the CodeRunner quiz:

- Question numbers along the side, one question per page, and a timer.
- The statement, then a code editor with the function header preloaded, line numbers and C
  highlighting.
- **Precheck** runs the shown tests, free. **Check** runs every test, hidden ones included, with
  the `0, 3, 6, ...` penalty. Both show the result table: test, expected, got, pass or fail.
- **Submit** finishes the attempt and locks the answers. It also happens by itself at the end
  time.

### What runs on the host PC

- Compiling goes through a queue, so a room pressing Check together waits a few seconds instead
  of failing.
- It compiles with gcc 15.2 and the course flags, the compiler on the lab machines and in the
  grader, so Check matches what students see in labs.
- Hidden tests and model answers stay on the host. Students only ever see output.
- Every answer is saved as it changes, so a browser crash or a server restart loses nothing.
- The timer runs on the host's clock, so it shows the real end time. PE02's front page had to
  warn that the LMS timer might not.
- At the end it writes one folder per student with their `.c` files, a scores CSV and the
  feedback page, in the layout `pe02_grade.py` already uses. Nothing is copied by hand.
- The questions come from one build script, as `build-pracex.py` does now, so each statement,
  test and mark is written once.
- Safe Exam Browser sends a hash of its configuration with each request, so the server can
  refuse any browser that is not Safe Exam Browser running our configuration, as Moodle does.

### The hard part: running student code safely

This is what Jobe does for Moodle, and it needs the most care. On the host, a student's program
could read the test files or delete things. Even `#include` on a test file makes gcc print its
lines in an error. So student code compiles and runs under a separate Windows account that
cannot open the exam folder, with time, memory and process limits, and is stopped when its time
runs out. A spare lab PC is a better host than a personal laptop.

### Where results live

Student names and results are written outside this repository, like the grader's answers
folder: `main` is published in full on GitHub Pages.

### No Moodle admin needed

The server never touches the LMS.

| Part | Who does it |
|---|---|
| Safe Exam Browser on the lab PCs | Already installed; PE02 used it |
| The `.seb` file that opens the server and blocks everything else | The course owner, with Safe Exam Browser's free Config Tool. If Moodle's quiz settings made the configuration before, it is now made by hand |
| Giving students the `.seb` file | The course owner, as a file on the course page in the LMS |
| Student logins | The server's own list, from the class roster, not LMS accounts |
| Marks into the LMS | The course owner: the server writes a CSV for the gradebook's CSV import, which teachers can normally use, and the feedback page is pasted in as before |
| Firewall rule and the separate account | Whoever administers the host PC |
| Lab PCs reaching the host | The lab network; if it blocks them, the lab technician |

### The host PC

Any ordinary Windows PC in the lab, on the students' network. No server hardware.

Measured on the course owner's desktop, a Ryzen 7 7700 with 8 cores, on 2026-11-01, with a PE02
Question 2 answer and the course flags:

| | |
|---|---|
| One compile | about 75 ms, after a first, cold compile of about 1.9 s |
| One run | 32 ms |
| 40 compiles at once, 8 at a time | 1.5 s in all |

A lab PC several times slower still clears a full room in a few seconds.

The host needs:

- the lab network, wired if possible;
- gcc 15.2, as on the student machines;
- admin rights, for the firewall rule and the separate account;
- power, with sleep turned off, and no student at it.

It is one machine. A crash or restart loses nothing, since the answers are on its disk. If it
dies outright the exam pauses, as it would if Moodle went down; a second lab PC with the program
ready takes over once the saved answers are copied onto it.

#### Network test

Before anything is built, check that the lab PCs can reach the host. On the host, with Python:

```powershell
mkdir C:\nettest; cd C:\nettest
python -m http.server 8000
```

It serves whatever folder it starts in, hence the empty one. Allow it when Windows Firewall
asks, and find the host's IPv4 address with `ipconfig` in a second window. On a lab PC, open
`http://<that address>:8000`. An empty directory listing means the plan works. A timeout means
the lab network keeps PCs apart, and the host has to be one the lab technician approves.

## Options set aside

| Option | Why not |
|---|---|
| Keep the LMS's CodeRunner | The rate limit is on the LMS side, and changing its Jobe server needs a Moodle admin |
| Real Moodle, CodeRunner and Jobe run locally in Docker | Identical, and imports the XML unchanged, but it means running a whole Moodle (accounts, enrolments, a database) and Docker on Windows. Jobe compiles with Linux gcc, where `long` is 64-bit instead of Windows' 32-bit, so results can differ from the labs |
| An offline exam: internet cut, students edit in VS Code with a `precheck.exe` for the shown tests, and upload `.c` files to an LMS Assignment at the end | Workable, and the internet can be cut, but the course owner wants the CodeRunner screens under Safe Exam Browser |
| A custom IDE `.exe` on each student PC | An `.exe` unpacks in minutes, so hidden tests cannot ship in it and there is no real Check; the answers also end up on every PC, to be collected. **Chosen after all on 2026-11-11, in the form at the top of these notes:** a start password, a receipt on paper, and regrading on the course owner's PC answer the two objections well enough for an offline, proctored sitting |
| No host: each student PC runs its own copy of the server | The same two problems: hidden tests on every PC, and answers spread over every PC |

## If the LMS holds answers again

The quiz's Responses report downloads as a CSV holding each student's answer text. A short
script turns that into the `.c` files, so nothing has to be copied by hand.

## Still open

- **How the exe runs on a real lab PC.** It was built and tested on the course owner's desktop,
  which has gcc 15.2 on PATH. Still untried on a lab PC:
  - Windows SmartScreen on an unsigned download from the LMS;
  - whether lab policy lets an unsigned program run at all;
  - whether a reboot-restore tool such as Deep Freeze wipes the saves.
- **The roster CSV** for the re-sit, which the course owner will provide.
- The syllabus says "Practical examinations are conducted offline at the laboratory machines"
  (`000. Course Orientation/06-the-syllabus/05-sec-policies.tex`). PE02 under Safe Exam Browser
  was not offline. The re-sit in Local CodeRunner is offline, so the line holds for it.
- `--460. Laboratory Exercise 12/` holds CodeRunner questions, which would hit the same limit
  on the LMS. Local CodeRunner could run them once their build script writes the exam JSON.

## Next steps

1. Pack a trial with `--sample-roster` and open it on one lab PC, from the LMS download.
2. Pack the re-sit with the class list, and follow `local-coderunner/README.md` before, during
   and after the sitting.
3. Write PE03's build script to emit the same exam JSON.
