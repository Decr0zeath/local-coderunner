"""What pack.py and grade.py share with the exe: building and checking an
answer, the penalty, the sealed pack, and the receipt.

The exe does the same in src/Engine.cs and src/Crypto.cs. pack.py runs the
exe's self-test against this file before it packs anything, so a change made
to one side and not the other stops the pack.
"""

import base64
import gzip
import hashlib
import hmac
import json
import os
import re
import secrets
import shutil
import struct
import subprocess
import tempfile
from pathlib import Path

# The course flags. -ftrack-macro-expansion=0 keeps an error inside a renamed
# main() pointing at the student's line rather than at the #define.
FLAGS = ["-Wall", "-Wextra", "-std=c17", "-g", "-ftrack-macro-expansion=0"]
LINK = ["-lm"]  # Jobe linked the maths library into every C program.
OUTPUT_CAP = 64 * 1024
GCC_ENV = dict(os.environ, LC_ALL="C")  # plain quotes in gcc's messages

CRASHES = {
    0xC0000005: "crashed: a bad pointer, or an index outside the array",
    0xC00000FD: "crashed: stack overflow, a recursion that never stops",
    0xC0000094: "crashed: division by zero",
}

MAGIC = b"LCRPACK1"
# Crockford's base 32: no I, L, O or U, so a code read off the board cannot be
# mistyped as a lookalike.
ALPHABET = "0123456789ABCDEFGHJKMNPQRSTVWXYZ"
ITERATIONS = 200_000


# --------------------------------------------------------------------------
# Checking an answer
# --------------------------------------------------------------------------

def harness(header, answer, tests):
    """The answer and every test in one program; `prog i` runs test i. The
    student's own main(), if they pasted one, is renamed out of the way."""
    cases = "".join(
        f"    case {i}: {{\n" + "".join(f"        {line}\n" for line in t["code"].split("\n"))
        + "        break;\n    }\n"
        for i, t in enumerate(tests))
    return (header + "#define main lcr_student_main\n"
            f'#line 1 "answer"\n{answer}\n#undef main\n#line 1 "tests"\n'
            "int main(int argc, char **argv)\n{\n"
            "    switch (argc > 1 ? atoi(argv[1]) : -1) {\n" + cases + "    }\n"
            "    return 0;\n}\n")


def tidy(text):
    """CodeRunner's exact match: trailing spaces off each line, and trailing
    blank lines dropped."""
    return "\n".join(line.rstrip() for line in text.split("\n")).rstrip("\n")


def clean_answer(text):
    return text.replace("\r\n", "\n").replace("\r", "\n")


def broken_rule(var, answer):
    for rule in var["rules"]:
        hit = re.search(rule["pattern"], answer) is not None
        if hit != (rule["kind"] == "require"):
            return rule["message"]
    return None


def run_test(exe, i, time_limit):
    try:
        done = subprocess.run([str(exe), str(i)], stdin=subprocess.DEVNULL,
                              capture_output=True, timeout=time_limit)
    except subprocess.TimeoutExpired:
        return "", f"still running after {time_limit:g} seconds"
    out = done.stdout + (b"\n" + done.stderr if done.stderr else b"")
    if len(out) > OUTPUT_CAP:
        return "", "printed more than 64 KB"
    text = out.decode("utf-8", "replace").replace("\r\n", "\n")
    code = done.returncode & 0xFFFFFFFF
    if code:
        return text, CRASHES.get(code, f"ended with exit code {code}")
    return text, None


def check(exam, var, answer, tmp, cc="gcc"):
    """One Check, as the exe runs it. `outcome` is "ran", "compile" or "rule";
    only "ran" can score."""
    answer = clean_answer(answer)
    tests = var["tests"]
    result = dict(outcome="ran", message="", passed=[False] * len(tests),
                  got=[""] * len(tests), trouble=[None] * len(tests), fraction=0.0)
    why = broken_rule(var, answer)
    if why:
        result.update(outcome="rule", message=why)
        return result
    # A fresh folder per Check, as the exe does: Windows can keep a just-killed
    # or just-scanned program locked, so a reused name cannot always be overwritten.
    work = Path(tempfile.mkdtemp(prefix="lcr-", dir=tmp))
    exe = work / "lcr-check.exe"
    try:
        done = subprocess.run([cc, *FLAGS, "-pipe", "-x", "c", "-", "-x", "none", "-o", str(exe),
                               *LINK],
                              input=harness(exam["header"], answer, tests).encode("utf-8"),
                              capture_output=True, env=GCC_ENV, cwd=work)
        log = done.stderr.decode("utf-8", "replace").replace("\r\n", "\n")
        if done.returncode != 0 or not exe.exists():
            result.update(outcome="compile", message=log)
            return result
        result["message"] = log
        for i, t in enumerate(tests):
            got, trouble = run_test(exe, i, exam["time_limit"])
            result["got"][i], result["trouble"][i] = got, trouble
            result["passed"][i] = trouble is None and tidy(got) == tidy(t["expect"])
        total = sum(t["mark"] for t in tests)
        result["fraction"] = sum(t["mark"] for t, ok in zip(tests, result["passed"]) if ok) / total
        return result
    finally:
        shutil.rmtree(work, ignore_errors=True)


def penalty(regime, previous):
    """The percentage a Check costs after `previous` earlier Checks, under a
    Moodle penalty regime such as "0, 3, 6, ...". After the listed values an
    ellipsis carries on by the last step; without one, the last value holds."""
    parts = [p.strip() for p in regime.split(",") if p.strip()]
    more = parts and parts[-1] == "..."
    values = [float(p) for p in parts if p != "..."]
    if previous < len(values):
        return min(values[previous], 100.0)
    step = values[-1] - values[-2] if more and len(values) > 1 else 0.0
    return min(values[-1] + step * (previous - len(values) + 1), 100.0)


def mark(points, fraction, penalty_percent):
    """Moodle's adaptive mark: the penalty comes off the fraction, not off
    what was earned, and never takes it below zero."""
    return points * max(0.0, fraction - penalty_percent / 100.0)


# --------------------------------------------------------------------------
# Codes, sealing and receipts
# --------------------------------------------------------------------------

def normalize_code(text):
    text = text.upper().translate(str.maketrans("OIL", "011"))
    return "".join(ch for ch in text if ch in ALPHABET)


def new_code(groups=2, size=5):
    return "-".join("".join(secrets.choice(ALPHABET) for _ in range(size))
                    for _ in range(groups))


def derive(code, salt, iterations, length):
    return hashlib.pbkdf2_hmac("sha256", normalize_code(code).encode("ascii"), salt,
                               iterations, length)


def hmac256(key, *parts):
    h = hmac.new(key, digestmod=hashlib.sha256)
    for p in parts:
        h.update(p)
    return h.digest()


def keystream_xor(key, nonce, data):
    """HMAC-SHA256 in counter mode as a stream cipher: block i of the stream is
    HMAC(key, nonce || i). The standard library has no AES, and the exe's
    .NET Framework has no AES-GCM; this needs neither."""
    out = bytearray(len(data))
    for block in range((len(data) + 31) // 32):
        stream = hmac256(key, nonce, struct.pack(">Q", block))
        start = block * 32
        for j, b in enumerate(data[start:start + 32]):
            out[start + j] = b ^ stream[j]
    return bytes(out)


def seal_pack(plain, password, title):
    """The encrypted exam the exe carries at its end. Only `title` is readable
    without the start password."""
    salt, nonce = secrets.token_bytes(16), secrets.token_bytes(16)
    header = json.dumps(dict(format=1, title=title, iterations=ITERATIONS,
                             salt=base64.b64encode(salt).decode("ascii"),
                             nonce=base64.b64encode(nonce).decode("ascii"))).encode("utf-8")
    key = derive(password, salt, ITERATIONS, 64)
    body = keystream_xor(key[:32], nonce, gzip.compress(plain, mtime=0))
    tag = hmac256(key[32:], header, body)
    return header + body + tag + struct.pack("<IQ", len(header), len(body)) + MAGIC


def open_pack(blob, password):
    if blob[-8:] != MAGIC:
        raise ValueError("no pack at the end of this file")
    header_len, body_len = struct.unpack("<IQ", blob[-20:-8])
    end = len(blob) - 20
    tag = blob[end - 32:end]
    body = blob[end - 32 - body_len:end - 32]
    header = blob[end - 32 - body_len - header_len:end - 32 - body_len]
    meta = json.loads(header)
    key = derive(password, base64.b64decode(meta["salt"]), meta["iterations"], 64)
    if not hmac.compare_digest(hmac256(key[32:], header, body), tag):
        raise ValueError("wrong password, or the pack is damaged")
    return gzip.decompress(keystream_xor(key[:32], base64.b64decode(meta["nonce"]), body))


def receipt(data):
    """60 bits of SHA-256, as 12 characters of base 32 in three groups."""
    value = int.from_bytes(hashlib.sha256(data).digest()[:8], "big") >> 4
    chars = "".join(ALPHABET[(value >> (5 * i)) & 31] for i in range(11, -1, -1))
    return f"{chars[0:4]}-{chars[4:8]}-{chars[8:12]}"
