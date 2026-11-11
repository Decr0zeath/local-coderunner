# Product

<!-- impeccable:product-schema 1 -->

## Platform

Windows desktop (WinForms on .NET Framework 4.8). Not web, iOS or Android.

## Users

Students of DATASTRUCT (Data Structures and Algorithms, University of San Jose-Recoletos) sitting a practical exam at a lab PC, under a proctor, against a 60-minute clock. They write C in whichever IDE they prefer and come to this window to read the questions, paste an answer, and check it.

The course owner, Engr. Violdan E. Bayocot, packs the exam, gives out the start password in the room, and grades the uploaded submissions. Proctors use a separate password for rescue actions.

## Product Purpose

A local, offline stand-in for Moodle's CodeRunner, written after the LMS's CodeRunner was rate-limited during Practical Exam 02 on 2026-11-01. The first use is the PE02 re-sit with PE02's own questions. Success means every student can Check as often as they choose without any network, and the course owner gets every answer and its Check history back intact.

## Operating Context

- Lab PCs run Windows 10 with gcc 15.2 on PATH. The internet is cut for the whole exam, and the PCs do not talk to each other.
- Students download the exe from the LMS just before the cut. The questions and tests stay encrypted until the proctor announces the start password.
- At Submit or time-out the exe writes a zip and shows a receipt code that students copy onto the paper front page. They upload the zip to the LMS once the internet is back.
- If a PC dies, a proctor moves the save to another PC by USB.

## Capabilities and Constraints

- Each student's ID, from the course owner's roster CSV, fixes one random variant of each of the three questions, shown in order Q1 to Q3.
- No Precheck. Check runs every test: the shown examples appear with expected and got, the hidden ones as a count passed. It also shows the mark and its penalty.
- Penalty regime 0, 3, 6, 9 ... percent of the question's points, the best Check stands. A failed compile, or a Question 3 loop word anywhere in the answer, counts as a Check.
- Answers are pasted in, not loaded from files.
- At Submit, any answer changed since its last Check is graded as one more Check.
- A proctor password can add minutes or reopen a submitted exam, and each use is logged.
- Screens are mixed: everything must work at 1366 x 768 and use the room on 1920 x 1080.

## Brand Commitments

The window carries the course's question-card look that students saw on the LMS for PE02 (confirmed by the course owner, 2026-11-11). It's defined in `class-contents/150. Practical Exam 02/build-pracex.py` (`CARD`, `PANEL`, `CODE_INLINE`), with colours from the Lost Century palette in `css/shell.css`.

## Evidence on Hand

The questions, tests and model answers come from `build-pracex.py` via `pe02-local-coderunner.json`. Model answers never ship in the exe.

## Product Principles

- The student's time is the scarce thing: nothing on screen competes with the statement, the answer and the result.
- Every Check and every proctor action leaves a trace the course owner can grade from.
- Never cost a student a Check they did not choose to make.
