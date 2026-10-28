# Study data (real participants) - how to fill and analyse

Everything here must come from what you actually observed or collected. Do not copy numbers from the SIMULATED_* files or from the calculators' built-in sample data.

Privacy: use pseudonymous codes P01..P10 only. Keep the code-to-name list and the signed consent forms OUTSIDE this repository. Do not commit videos.

Copy each *_TEMPLATE.csv to the same name without `_TEMPLATE` (e.g. `coding_sheet.csv`) and fill it in.

## coding_sheet.csv (one row per command attempt series, watched in the video)
- participant_code: P01..P10
- condition: `voice` or `controller` (what the person used for that command)
- command_phrase: what they were asked to do, e.g. `select that`, `move left`, `grab this object`, `drop this object`
- attempt_count: how many times they tried until it worked (or gave up)
- first_try_success: 1 if it worked on the first try, else 0
- seconds_to_result: seconds from starting to speak (or press) to the visible result; leave empty if it never worked
- notes: free text (noise, mis-heard word, etc.)

## sus_responses.csv: q1..q10 are the 10 standard SUS answers, 1 to 5.
## tlx_responses.csv: the six NASA-TLX subscales, 0 to 100.

## Analyse
`python telemetry/study/analyze_study.py` prints N, mean, SD and median for SUS, raw NASA-TLX and the task metrics, and writes `study_summary.csv`.
