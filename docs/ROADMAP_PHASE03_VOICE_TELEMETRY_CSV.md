# Roadmap Phase 03 — Voice Telemetry CSV

**Status: COMPLETE (verified 2026-10-22), with the limits listed below.**

Verification evidence:
- Standalone suite: 331 passed, 0 failed (exit 0). External compile check: 30 files, 3 of 3 configurations OK (exit 0). Unity compiled the current files (`Assembly-CSharp.dll` newer than the last source edit).
- **Runtime:** real Play Mode sessions with a real microphone produced two files in `C:\Users\Admin\AppData\LocalLow\DefaultCompany\Group_05_Sakshi_Aryan\VoiceVR\`: `voice_telemetry_20261021T122836Z-547b.csv` (7 rows) and `voice_telemetry_20261022T174523Z-1c70.csv` (36 rows). Both have the exact 15-column header, 15 fields in every row, `sequence_id` 1..n with no gaps, one `session_id` per file, and monotonic UTC timestamps. Low-confidence phrases appear as `ignored / low_confidence` with an empty outcome; accepted commands carry `executed`, `cancelled` or `no_focus`; an undo-type phrase below the floor appears as `low_confidence_undo_allowed`. Recognizer speech-end-to-event times were 464 to 787 ms (recognizer-reported, not end-to-end latency).
- The previous status text said nothing had run in Unity; that was true when it was written and is no longer true.

Exactly-once check (2026-10-23): the Unity Editor log for the session showed 48 recognizer callbacks (8 ignored, 40 accepted) and the three voice CSVs hold 48 rows (8 `ignored/low_confidence`, 40 `accepted`), so no callback was dropped or doubled.
Typed/keyboard check (2026-10-23, Play Mode, commands sent through `Submit`, not physical key presses): 2 TextFallback and 2 KeyboardFallback commands were dispatched and appear in the gaze event log (`gaze_events_selftest_20261022T192231Z.csv`, modality `TextFallback` / `KeyboardFallback`), while the voice CSV folder was unchanged (same 3 files, same line counts, no new file).

Limits (not claimed): no recognizer-error row and no `parse_rejected` row has been observed at runtime (both are covered by the standalone tests only); the physical F8/number-key presses were not exercised, only the same `Submit` path they call. Nothing here is evidence about Quest hardware or native Quest voice recognition.

(Original status line, kept for history: "IMPLEMENTED, RUNTIME VERIFICATION PENDING. Nothing has yet been run in Unity.")

Schema: the 15-column specification approved for Phase 2A (schema version `1`). The existing gaze event CSV and trial CSV schemas are **unchanged** (a test pins both headers byte for byte).

## Files

| File | Role |
|---|---|
| `Assets/Scripts/Telemetry/VoiceTelemetryCsvWriter.cs` | New. `VoiceTelemetryRecord` (schema, validation, serialization, decision/reason/outcome mapping) and `VoiceTelemetryCsvWriter` (append-only session writer). Pure C#, no Unity dependency. |
| `Assets/Scripts/Voice/DesktopVoiceInput.cs` | Writes one row per recognizer event; exposes `ReportActionOutcome` and `VoiceTelemetryPath`. The `[VoiceFocusDiag]` block is untouched. |
| `Assets/Scripts/Interaction/GazeFocusSystem.cs` | One added line in `SubmitParsed` reporting the command outcome to the voice input. No behaviour change. |
| `Tests/Standalone/StandaloneTests.cs` | 47 new checks. |
| `Tests/Standalone/run_standalone_tests.ps1`, `compile_check_unity_scripts.ps1` | The new file added to the source lists. |

Pre-existing telemetry, reused or left alone: `SessionCsvFile` (not reused: it puts a data-origin token in the file name), `FittsMath.CsvRow` (reused for RFC 4180 escaping),
`GazeEventCsvWriter` (unchanged, still writes `telemetry/selftest`), `TrialRecord` / `TrialCsvWriter` (unchanged, tested, not yet wired to the scene), `FittsTargetTelemetryLogger` (starter stub, not in the scene, unchanged).

## Where the file goes

`<Application.persistentDataPath>/VoiceVR/voice_telemetry_<sessionId>.csv`. On this Windows machine the expected folder is
`C:\Users\Admin\AppData\LocalLow\DefaultCompany\Group_05_Sakshi_Aryan\VoiceVR\` (company and product name from `ProjectSettings.asset`). **That path has not been confirmed at runtime**;
the component logs `[VoiceTelemetry] writing to <path>` when the first row is written.
The file is created by the first successful write, never overwritten (a taken name gets `_2`, `_3`, ...), and is opened, appended and closed for every row, so a Play Mode stop or crash loses nothing.

## Columns

| # | Column | Source / rule |
|---|---|---|
| 1 | `schema_version` | constant `1` |
| 2 | `session_id` | `yyyyMMddTHHmmssZ-xxxx` (UTC start plus 4 random hex characters); one per `DesktopVoiceInput` enable; owned by the writer |
| 3 | `sequence_id` | 1, 2, 3 ... within the session; owned by the writer; a refused or failed write does not consume a number |
| 4 | `timestamp_utc` | `DateTime.UtcNow` at callback entry, `yyyy-MM-ddTHH:mm:ss.fffZ` |
| 5 | `realtime_s` | `Time.realtimeSinceStartup` at callback entry, 3 decimals, finite and >= 0 |
| 6 | `platform` | `Application.platform` name (`WindowsEditor`, `WindowsPlayer`) |
| 7 | `recognized_phrase` | the grammar phrase the recogniser returned (empty on an error row); never audio |
| 8 | `confidence_rank` | `args.confidence` (High / Medium / Low / Rejected); empty on an error row |
| 9 | `minimum_confidence_rank` | the configured floor (Low / Medium / High) |
| 10 | `intent` | parsed intent, or empty when the parser found no action |
| 11 | `decision` | `accepted`, `ignored` or `error` |
| 12 | `reason` | `confidence_pass`, `low_confidence_undo_allowed`, `low_confidence`, `parse_rejected`, `recognizer_error` |
| 13 | `speech_end_to_event_ms` | recogniser-reported speech end to callback; empty when unavailable. **Not a validated end-to-end latency (D-11).** |
| 14 | `parse_duration_ms` | time spent in `VoiceIntentParser.Parse` for this phrase |
| 15 | `action_outcome` | `executed`, `cancelled`, `not_implemented`, or the rejection reason (for example `no_focus`); empty if no outcome was observed |

Missing or inapplicable values are empty fields. `NaN`, `Infinity` and invented zeroes are never written (a non-finite or negative measurement is refused). Numbers use the invariant culture.

## Which events get a row

* Written: a recognised phrase that is accepted; a recognised phrase ignored for low confidence; a recognised phrase the parser rejects; a speech-system error event (`PhraseRecognitionSystem.OnError`).
* Not written: typed F8 text, keyboard shortcuts 1–7, speech the recogniser never returned, microphone start failures that are exceptions rather than error events, anything synthetic.
* There is no `data_origin` and no participant column, as specified. A developer test session and a real participant session are therefore not distinguishable inside the file.

## Choices made where the specification left room (flag for review)

1. **Outcome hook.** The command result is known only inside `GazeFocusSystem`, which depends on `DesktopVoiceInput`, not the reverse. One call per command passes the outcome back synchronously; the row is written once, after the handler returns. No row is ever updated.
2. **`decision` for a parse-rejected phrase:** `ignored` with `parse_rejected` (cannot currently occur: every grammar phrase parses). A command the pipeline accepted but the action rejected stays `accepted`, with the failure in `action_outcome`.
3. **Extra reason `low_confidence_undo_allowed`** for cancel, reset, release, help and focus accepted below the floor, so `confidence_pass` is never reported for a phrase below the floor.
4. **Timestamp precision:** milliseconds (the specification example showed seconds). `realtime_s` carries the same moment.
5. **Session id uniqueness:** a random suffix, so two sessions in the same second differ. The id is not shared with the gaze event log; the two files can only be joined by time.
6. **Error rows are written from `Update`** (the thread of `OnError` is unverified), so an error row's timestamp is the next frame, and the specific `SpeechError` value stays in the existing warning log line only.
7. **A failed write disables telemetry for the session** after one warning; it never affects interaction.

## Example (illustrative, from the unit test; not research data)

```
schema_version,session_id,sequence_id,timestamp_utc,realtime_s,platform,recognized_phrase,confidence_rank,minimum_confidence_rank,intent,decision,reason,speech_end_to_event_ms,parse_duration_ms,action_outcome
1,example_session,1,2026-10-19T10:00:00.000Z,12.450,WindowsPlayer,move left,Medium,Medium,Move,accepted,confidence_pass,420,0.02,executed
```

## Automated tests (47 new, all in `Tests/Standalone/StandaloneTests.cs`)

Header and column order; both existing headers unchanged; exact row serialization; empty fields for missing values; no `NaN`/`Infinity` text; refusal of non-finite and negative values; invariant formatting under a German culture;
escaping of commas, quotes and newlines with read-back; validation of decision, UTC timestamp, realtime, durations, sequence and session id; the decision/reason mapping driven by the real confidence policy and parser (nine cases);
intent and outcome columns; session id format and uniqueness; on real temp files: no file before the first row, file name pattern, header once, append, sequence 1-2-3, writer-owned session id, collision handling without touching the earlier file,
refused rows create no file and consume no sequence number, null record, a write failure that returns false without throwing, missing folder, and a hostile session id that cannot leave the folder.

## Not yet verified (needs Unity or the user)

* The Unity batch self-test on the current tree (Unity is open). Expected: 40/40, but not run.
* That `DesktopVoiceInput` actually writes a file in Play Mode, the real path, and the content of real rows. Run Play Mode, press F9, say two or three grammar phrases (one on purpose too quietly), stop, then open the file.
* That the compiled Unity assembly accepts the new members (the external Roslyn compile check passes in three configurations; it is not Unity's own compiler).
* The error-row path (needs a real recogniser error).
* Whether `speech_end_to_event_ms` is trustworthy (D-11).

## Out of scope here

Trial sequencing, the Fitts trial runner, the controller condition, analysis changes and participant workflow belong to later phases and were not started.
