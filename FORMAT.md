# Formats

Every file Local CodeRunner reads or writes. Format 1 is what v1 uses and what PE02's re-sit was
sat with; it is checked against the code as of 2026-11-18. Format 2 is proposed for the exam
builder and is not built yet (`PLAN.md`).

A v1 pack and its submissions must keep grading. Never change a format without bumping its
version.

## Versions

| What | Version field | Now | Who checks it |
|---|---|---|---|
| The exam source | `format` | 1 | Nobody. v1's exe and `pack.py` ignore it |
| The pack header | `format` | 1 | Written; not checked on open |
| The pack's tail | the last 8 bytes | `LCRPACK1` | The exe and `lcr.open_pack` |
| The bundle | `format` | 1 | Written; not checked on open |
| `pack.json` | `format` | 1 | Written; not checked |
| The save | the first word | `LCRSAVE1` | The exe |
| The state inside a save and a zip | `format` | 1 | Written; not checked |

The app does not record its own version anywhere. Format 2 has to fix both gaps: the app refuses
a format it does not know, and every submission records the app version that made it.

## The exam source

The JSON that `pack.py --exam` reads. PE02's was written by its `build-pracex.py`; from PE03 on
the exam builder writes it. It holds the model answers, so it is never committed here.

### Exam

| Field | Holds | PE02 |
|---|---|---|
| `format` | 1 | `1` |
| `code` | The exam's code. It names the Desktop folder and the files in it | `"PE02"` |
| `title` | The exam's title | `"Practical Exam 02"` |
| `minutes` | The time allowed | `60` |
| `penalty` | A Moodle regime string, see "Penalty and mark" | `"0, 3, 6, ..."` |
| `header` | The `#include` lines, one string ending in a newline | `stdio.h`, `stdlib.h`, `string.h`, `stdbool.h`, `limits.h` |
| `time_limit` | Seconds per test | `3` |
| `questions` | The questions, in the order shown | 3 |

### Question

| Field | Holds |
|---|---|
| `number` | 1 to n |
| `title` | The question's title |
| `points` | Its points. PE02: 10, 25 and 15 |
| `variants` | Its variants. PE02 has 4 per question; one is drawn per student |

### Variant

| Field | Holds |
|---|---|
| `id` | `VEB-DATASTRUCT-` + exam code + question code + letter, as `VEB-DATASTRUCT-PE02SEQA`. It names the answer's `.c` file in the zip |
| `v` | The letter, `A` to `D` |
| `fn` | The name of the one function the student writes |
| `statement` | A list of HTML paragraphs, with plain `<code>` and `<strong>` only. The app styles them |
| `signature` | The function's first line. The app shows it after the first paragraph |
| `preload` | What the answer box starts with: the signature, then `{\n}` |
| `answer` | The model answer. `pack.py` removes it before sealing |
| `rules` | A list of `{kind, pattern, message}`, see below. Often empty |
| `tests` | A list of `{label, code, expect, mark, shown}`, see below |

A variant's test marks add up to its question's points.

**Rules.** `kind` is `forbid` or `require`. `pattern` is a regular expression searched in the
whole answer, comments included, after line endings become `\n`. It runs in both Python's `re`
and .NET's `Regex`, so it must mean the same in both. A broken rule shows `message` and counts
as a Check that scores nothing. PE02's Question 3 has two: one forbids loops, one requires the
function to call itself.

**Tests.** `code` is C statements, placed in a block of their own inside `main`. `expect` is
the exact output. `mark` is what the test is worth. A `shown` test appears in the statement as
an example, and Check shows its expected and got; a hidden one counts only towards "hidden
tests passed". `label` names the test in grading output.

### How a Check is built and run

The answer and every test become one program:

```
<header>
#define main lcr_student_main
#line 1 "answer"
<the answer>
#undef main
#line 1 "tests"
int main(int argc, char **argv)
{
    switch (argc > 1 ? atoi(argv[1]) : -1) {
    case 0: {
        <test 0's code>
        break;
    }
    ...
    }
    return 0;
}
```

- A `main()` the student pastes is renamed `lcr_student_main` and never called.
- Line endings in the answer become `\n` first.
- It compiles with `gcc -Wall -Wextra -std=c17 -g -ftrack-macro-expansion=0`, linked with `-lm`,
  with `LC_ALL=C`. A failed compile counts as a Check that scores nothing.
- Each test runs as its own process, `program <i>`, with no input, under `time_limit`. A test
  that crashes, hangs or prints more than 64 KB fails alone.
- Output is stdout, then a newline and stderr if there is any. It passes when the program ends
  with exit code 0 and the output equals `expect` once both lose trailing spaces on each line and
  trailing blank lines. This is CodeRunner's exact match.
- The fraction is the marks of the passing tests over the variant's total.

`lcr.check(exam, variant, answer, tmp)` in `lcr.py` does all of this and returns the outcome
(`ran`, `compile` or `rule`), the message, and what each test passed and printed.
`src/Engine.cs` does the same in the exe.

### Penalty and mark

- The regime is a list of percentages. Check *k* (counting from 0) costs the *k*-th value. After
  the list, `...` carries on by the last step (`0, 3, 6, ...` gives 9, 12, and so on); without
  `...` the last value holds. Never more than 100.
- Mark = points × max(0, fraction − penalty ÷ 100). The best Check stands.
- At Submit, each answer changed since its last Check is checked once more, with the next
  penalty.

## Format 2: proposed additions

Not built. These are the additions the planned exams need (`PLAN.md`, "The exam builder"). An
exam in format 2 sets `format: 2`. The app opens format 1 and format 2.

1. **Support code per variant.** `support: [{code, shown}]`. Each block is compiled in order
   after `header` and before the answer. A shown block appears read-only above the answer box
   (a node struct, `printList`). A hidden one does not (a `malloc`/`free` counter, a helper that
   builds a list or a tree from an array). Order matters: a counting `malloc` macro comes before
   any code that allocates, and a list builder comes after the struct. Two traps:
   - A student who redefines a shown struct gets a compile error. The statement must say the
     struct is given.
   - Support code must compile without a warning even when a test does not use a helper. A
     `static` helper that no test calls trips `-Wunused-function`, so helpers are not static.
2. **Several functions per answer.** `functions: [{name, signature}]` replaces `fn` and
   `signature`, for a queue or a hash table. `preload` holds a stub for each. The builder adds a
   `require` rule for each definition.
3. **A time limit per test.** An optional `time_limit` on a test overrides the exam's, for
   backtracking.
4. **Versions.** The app refuses a `format` it does not know, and records its own version in
   every save and zip, so a submission is graded with the harness that made it.

**What v1 assumes today**, and format 2 has to change:

- `src/Model.cs` requires `fn` and `signature` on every variant.
- `src/Html.cs` shows one signature after the first paragraph, and highlights only `const`,
  `unsigned` and `int` in it.
- Nothing reads the exam's `format`. A format 2 exam given to v1 either fails on the missing `fn`
  or quietly drops its support code. Until the app reads format 2, `pack.py` must refuse any
  `format` other than 1.

**Left out on purpose:**

- Whole programs that read keyboard input. Every planned topic fits a function question, and
  tests that pass arrays proved fair in PE02. Add a program kind only when an exam needs one.
- Marks given at the machine. The app gives test marks only. Anything marked by talking to the
  student stays on a paper mark sheet.
- Output compared any way other than exact match.

## The pack

The encrypted exam, appended to the end of the exe.

```
header JSON | body | tag (32 bytes) | header length (u32) | body length (u64) | "LCRPACK1"
```

- The two lengths are little-endian.
- The header JSON holds `format` (1), `title`, `iterations` (200,000), `salt` and `nonce` (16
  random bytes each, in base 64). Only the title is readable without the start password.
- Key = PBKDF2-HMAC-SHA256(the start password, salt, iterations, 64 bytes). The password is
  normalised first (see "Codes"). The first 32 bytes encrypt; the last 32 authenticate.
- Body = gzip(bundle) XOR a keystream. Block *i* of the keystream is HMAC-SHA256(encryption key,
  nonce ‖ *i* as a big-endian u64).
- Tag = HMAC-SHA256(authentication key, header ‖ body). A wrong password fails here.

Neither the Python standard library nor .NET Framework 4.8 has AES-GCM, which is why it is
built this way.

### The bundle

The JSON inside the pack:

| Field | Holds |
|---|---|
| `format` | 1 |
| `pack` | The pack's id, 8 hex characters. It names the backup folder and the registry marker |
| `title` | The title given at packing |
| `seal_key` | 32 random bytes, base 64. It seals the saves and zips |
| `proctor` | `{salt, iterations, hash}`: hash = PBKDF2-HMAC-SHA256(the proctor password, salt, iterations, 32 bytes) |
| `exam` | The exam source with every `answer` removed |
| `roster` | `[{id, name, draw}]`, where `draw` maps each question number, as a string, to a letter: `{"1": "B", "2": "A", "3": "D"}` |

## What `pack.py` writes

Into the `--out` folder, which must be new or empty:

| File | Holds |
|---|---|
| `<code>-LocalCodeRunner.exe` | The exe with the pack appended |
| `PASSWORDS.txt` | The start and proctor passwords, the pack id and when it was built |
| `assignments.csv` | `id`, `name`, then each question's drawn variant id |
| `pack.json` | Everything `grade.py` needs: `format`, `pack`, `title`, `built`, `exe`, `exe_sha256`, `seal_key`, `exam` (with the model answers), `roster` |

None of these may be committed.

## The save

Written after every change to `Desktop\<code>\<code>-<id>.save`, with a copy at
`%LOCALAPPDATA%\LocalCodeRunner\<pack>\<id>.save`. Characters not allowed in a file name become
`_` in `<id>`.

```
LCRSAVE1 <hex HMAC-SHA256(seal_key, state JSON)>
<state JSON>
```

The registry key `HKCU\Software\LocalCodeRunner\<pack>\<id>` holds `started` and `deadline`, so
deleting both saves cannot restart the clock.

### The state

| Field | Holds |
|---|---|
| `format` | 1 |
| `pack`, `exam` | The pack id and the exam code |
| `id`, `name` | The student |
| `draw` | Question number to letter, as in the bundle |
| `started`, `deadline` | When Start was pressed, and when the time runs out |
| `remaining`, `allowed` | Seconds left, and seconds allowed including any added time |
| `machine` | The PC's name |
| `status` | `open` or `submitted` |
| `submitted` | When it was submitted |
| `seq` | Counts saves. Of the Desktop save and the backup, the one with the higher count is used |
| `answers` | Question number to the answer box's text |
| `checks` | Every Check, see below |
| `events` | `{at, what, machine, detail}` for each start, reopen, window close, proctor action and submit |
| `receipt` | In the save only, once submitted |

A Check is `{n, q, variant, at, elapsed, code, outcome, message, passed, fraction, penalty, mark,
at_submit, shown}`. `code` is the answer as checked. `passed` has one entry per test. `shown`
holds what the shown tests printed, so a reopened exe can show the last result.

## The zip

Written at Submit to `Desktop\<code>\<code>-<id>.zip`:

| Entry | Holds |
|---|---|
| `history.json` | The state, without `receipt`. The receipt and the seal are computed from these exact bytes |
| `seal.txt` | hex HMAC-SHA256(seal_key, history.json), then a newline |
| `receipt.txt` | `Receipt <code>`, the name, the ID and the time submitted, one per line |
| `<variant id>.c` | For each question, the answer of the Check that set the mark (the later one on a tie), with Windows line endings |

## Codes

- **The alphabet** is Crockford's base 32, `0123456789ABCDEFGHJKMNPQRSTVWXYZ`: no I, L, O or U.
- **Typed codes** are normalised: upper case, O becomes 0, I and L become 1, and anything outside
  the alphabet (spaces, dashes) is dropped.
- **Passwords** are two groups of five, as `7K2QH-M9XRT`. The start and proctor passwords always
  differ.
- **The receipt** is the first 60 bits of SHA-256(history.json), as three groups of four,
  `XXXX-XXXX-XXXX`.
