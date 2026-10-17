"""Paired statistics named in RESEARCH_AND_IMPLEMENTATION_GUIDE.md section 2.2:
Student's t-test (paired), Cohen's d, and the Wilcoxon signed-rank test.

Pure Python (math only). The tests cross-check every number against scipy when it is installed.

These functions only compute statistics from numbers they are given. They say nothing about whether a study
design is valid, whether the data are real participant data, or whether N is large enough. Callers must not
report p-values from simulated or self-test data as findings.

Design choices, all PENDING CONFIRMATION by the professor (the guide names the tests but not the variants):
  * t-test is the PAIRED (dependent samples) test; the manuscript says "paired Student's t-test".
  * Cohen's d is reported two ways, clearly labelled: d_z (mean difference / SD of differences, the usual
    paired-design effect size) and d_av (mean difference / average of the two conditions' SDs).
  * Wilcoxon: zero differences are dropped (Wilcoxon's method); exact two-sided p when there are no ties and
    n <= 50, otherwise a normal approximation with tie correction and no continuity correction (scipy defaults).
"""
from __future__ import annotations

import math
from dataclasses import dataclass
from typing import List, Sequence


class StatsError(ValueError):
    """Raised for inputs from which a statistic cannot be computed (never silently returns NaN)."""


def _check_pair(a: Sequence[float], b: Sequence[float]) -> None:
    if len(a) != len(b):
        raise StatsError(f"paired samples must have equal length (got {len(a)} and {len(b)})")
    if len(a) < 2:
        raise StatsError(f"need at least 2 pairs (got {len(a)})")
    for x in list(a) + list(b):
        if isinstance(x, bool) or not isinstance(x, (int, float)) or not math.isfinite(x):
            raise StatsError(f"non-finite or non-numeric value: {x!r}")


def _mean(v: Sequence[float]) -> float:
    return math.fsum(v) / len(v)


def _sd(v: Sequence[float]) -> float:
    m = _mean(v)
    return math.sqrt(math.fsum((x - m) ** 2 for x in v) / (len(v) - 1))


# ---------- regularised incomplete beta (Numerical Recipes continued fraction) ----------

def _betacf(a: float, b: float, x: float) -> float:
    tiny = 1e-300
    qab, qap, qam = a + b, a + 1.0, a - 1.0
    c = 1.0
    d = 1.0 - qab * x / qap
    if abs(d) < tiny:
        d = tiny
    d = 1.0 / d
    h = d
    for m in range(1, 1000):
        m2 = 2 * m
        aa = m * (b - m) * x / ((qam + m2) * (a + m2))
        d = 1.0 + aa * d
        d = tiny if abs(d) < tiny else d
        c = 1.0 + aa / c
        c = tiny if abs(c) < tiny else c
        d = 1.0 / d
        h *= d * c
        aa = -(a + m) * (qab + m) * x / ((a + m2) * (qap + m2))
        d = 1.0 + aa * d
        d = tiny if abs(d) < tiny else d
        c = 1.0 + aa / c
        c = tiny if abs(c) < tiny else c
        d = 1.0 / d
        delta = d * c
        h *= delta
        if abs(delta - 1.0) < 3e-16:
            return h
    raise StatsError("incomplete beta did not converge")


def _betai(a: float, b: float, x: float) -> float:
    if x <= 0.0:
        return 0.0
    if x >= 1.0:
        return 1.0
    ln_bt = math.lgamma(a + b) - math.lgamma(a) - math.lgamma(b) + a * math.log(x) + b * math.log1p(-x)
    bt = math.exp(ln_bt)
    if x < (a + 1.0) / (a + b + 2.0):
        return bt * _betacf(a, b, x) / a
    return 1.0 - bt * _betacf(b, a, 1.0 - x) / b


def _t_two_sided_p(t: float, df: int) -> float:
    return _betai(df / 2.0, 0.5, df / (df + t * t))


# ---------- paired t-test and effect sizes ----------

@dataclass(frozen=True)
class PairedT:
    n: int
    mean_difference: float   # mean(a - b)
    sd_difference: float
    t: float
    df: int
    p_two_sided: float


def paired_t_test(a: Sequence[float], b: Sequence[float]) -> PairedT:
    _check_pair(a, b)
    diffs = [x - y for x, y in zip(a, b)]
    n = len(diffs)
    md, sd = _mean(diffs), _sd(diffs)
    if sd == 0.0:
        raise StatsError("all paired differences are identical (zero variance): the t statistic is undefined")
    t = md / (sd / math.sqrt(n))
    return PairedT(n=n, mean_difference=md, sd_difference=sd, t=t, df=n - 1, p_two_sided=_t_two_sided_p(t, n - 1))


@dataclass(frozen=True)
class EffectSizes:
    d_z: float    # mean(a-b) / sd(a-b)
    d_av: float   # mean(a-b) / sqrt((var(a)+var(b))/2)


def cohens_d(a: Sequence[float], b: Sequence[float]) -> EffectSizes:
    _check_pair(a, b)
    diffs = [x - y for x, y in zip(a, b)]
    sd_diff = _sd(diffs)
    if sd_diff == 0.0:
        raise StatsError("all paired differences are identical (zero variance): d_z is undefined")
    pooled = math.sqrt((_sd(a) ** 2 + _sd(b) ** 2) / 2.0)
    if pooled == 0.0:
        raise StatsError("both conditions have zero variance: d_av is undefined")
    md = _mean(diffs)
    return EffectSizes(d_z=md / sd_diff, d_av=md / pooled)


# ---------- Wilcoxon signed-rank ----------

@dataclass(frozen=True)
class Wilcoxon:
    n_effective: int         # pairs left after dropping zero differences
    n_zero_dropped: int
    w_plus: float
    w_minus: float
    statistic: float         # min(w_plus, w_minus)
    p_two_sided: float
    method: str              # "exact" or "normal-approximation"


def _average_ranks(values: List[float]) -> List[float]:
    order = sorted(range(len(values)), key=lambda i: values[i])
    ranks = [0.0] * len(values)
    i = 0
    while i < len(order):
        j = i
        while j + 1 < len(order) and values[order[j + 1]] == values[order[i]]:
            j += 1
        avg = (i + j) / 2.0 + 1.0
        for k in range(i, j + 1):
            ranks[order[k]] = avg
        i = j + 1
    return ranks


def _exact_cdf_counts(n: int) -> List[int]:
    """counts[s] = number of subsets of {1..n} with sum s."""
    total = n * (n + 1) // 2
    counts = [0] * (total + 1)
    counts[0] = 1
    for r in range(1, n + 1):
        for s in range(total, r - 1, -1):
            counts[s] += counts[s - r]
    return counts


def wilcoxon_signed_rank(a: Sequence[float], b: Sequence[float], continuity_correction: bool = False) -> Wilcoxon:
    _check_pair(a, b)
    diffs_all = [x - y for x, y in zip(a, b)]
    diffs = [d for d in diffs_all if d != 0.0]
    zeros = len(diffs_all) - len(diffs)
    n = len(diffs)
    if n < 1:
        raise StatsError("all paired differences are zero: the Wilcoxon statistic is undefined")
    absd = [abs(d) for d in diffs]
    ranks = _average_ranks(absd)
    w_plus = math.fsum(r for r, d in zip(ranks, diffs) if d > 0)
    w_minus = math.fsum(r for r, d in zip(ranks, diffs) if d < 0)
    stat = min(w_plus, w_minus)
    has_ties = len(set(absd)) != len(absd)

    if not has_ties and n <= 50:
        counts = _exact_cdf_counts(n)
        total_subsets = 2 ** n
        cdf = sum(counts[: int(stat) + 1])
        p = min(1.0, 2.0 * cdf / total_subsets)
        return Wilcoxon(n, zeros, w_plus, w_minus, stat, p, "exact")

    mu = n * (n + 1) / 4.0
    tie_term = 0.0
    seen: dict = {}
    for v in absd:
        seen[v] = seen.get(v, 0) + 1
    for t in seen.values():
        tie_term += t ** 3 - t
    var = n * (n + 1) * (2 * n + 1) / 24.0 - tie_term / 48.0
    if var <= 0.0:
        raise StatsError("variance of the Wilcoxon statistic is zero")
    d = stat - mu
    if continuity_correction:
        d += 0.5
    z = d / math.sqrt(var)
    p = min(1.0, math.erfc(abs(z) / math.sqrt(2.0)))
    return Wilcoxon(n, zeros, w_plus, w_minus, stat, p, "normal-approximation")
