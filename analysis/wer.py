"""Word error rate, WER = (S + D + I) / N  (dossier section 4, "Primary task metric").

S = substitutions, D = deletions, I = insertions, N = number of words in the REFERENCE.
Standard word-level Levenshtein alignment. Pure Python, no dependencies.

Normalisation matches Assets/Scripts/Voice/VoiceIntentParser.cs Normalize(): lower-case, apostrophes dropped
("what's" -> "whats"), every other non-alphanumeric character is a word separator.

IMPORTANT: this computes a metric from a (reference, hypothesis) pair that a person supplies. It does not
transcribe anything and it never invents transcripts. A WER is only meaningful when the references are the
words the participant was actually instructed to say and the hypotheses are what the recognizer really returned.
"""
from __future__ import annotations

from dataclasses import dataclass
from typing import List, Sequence, Tuple


def normalize(text: str | None) -> List[str]:
    if not text:
        return []
    out = []
    cur = []
    for ch in text.lower():
        if ch.isalnum():
            cur.append(ch)
        elif ch in ("'", "’"):
            continue
        else:
            if cur:
                out.append("".join(cur))
                cur = []
    if cur:
        out.append("".join(cur))
    return out


@dataclass(frozen=True)
class WerResult:
    substitutions: int
    deletions: int
    insertions: int
    reference_words: int

    @property
    def errors(self) -> int:
        return self.substitutions + self.deletions + self.insertions

    @property
    def wer(self) -> float:
        """Raises ValueError if the reference is empty (WER is undefined, not 0)."""
        if self.reference_words == 0:
            raise ValueError("WER is undefined for an empty reference")
        return self.errors / self.reference_words


def word_errors(reference: str | None, hypothesis: str | None) -> WerResult:
    ref = normalize(reference)
    hyp = normalize(hypothesis)
    n, m = len(ref), len(hyp)
    # dp[i][j] = (cost, S, D, I) aligning ref[:i] with hyp[:j]
    dp: List[List[Tuple[int, int, int, int]]] = [[(0, 0, 0, 0)] * (m + 1) for _ in range(n + 1)]
    for i in range(1, n + 1):
        c, s, d, ins = dp[i - 1][0]
        dp[i][0] = (c + 1, s, d + 1, ins)
    for j in range(1, m + 1):
        c, s, d, ins = dp[0][j - 1]
        dp[0][j] = (c + 1, s, d, ins + 1)
    for i in range(1, n + 1):
        for j in range(1, m + 1):
            if ref[i - 1] == hyp[j - 1]:
                dp[i][j] = dp[i - 1][j - 1]
                continue
            c, s, d, ins = dp[i - 1][j - 1]
            best = (c + 1, s + 1, d, ins)                      # substitution
            c, s, d, ins = dp[i - 1][j]
            cand = (c + 1, s, d + 1, ins)                      # deletion
            if cand[0] < best[0]:
                best = cand
            c, s, d, ins = dp[i][j - 1]
            cand = (c + 1, s, d, ins + 1)                      # insertion
            if cand[0] < best[0]:
                best = cand
            dp[i][j] = best
    _, s, d, ins = dp[n][m]
    return WerResult(substitutions=s, deletions=d, insertions=ins, reference_words=n)


def corpus_wer(pairs: Sequence[Tuple[str, str]]) -> WerResult:
    """Corpus-level WER: error counts and reference words are summed over all pairs before dividing."""
    s = d = i = n = 0
    for ref, hyp in pairs:
        r = word_errors(ref, hyp)
        s += r.substitutions
        d += r.deletions
        i += r.insertions
        n += r.reference_words
    return WerResult(s, d, i, n)
