// Standalone (no Unity) tests for the pure-C# parts:
// VoiceIntentParser, FittsMath, TrialRecord, TrialCsvWriter.
// These are NOT Unity Editor tests and do not prove the Unity project compiles.
// Run with Tests/Standalone/run_standalone_tests.ps1. Exit code 0 = all passed.
using System;
using System.Globalization;
using System.IO;
using System.Threading;
using Group05.VoiceVR;

internal static class StandaloneTests
{
    private static int passed, failed;

    private static void Check(bool ok, string name)
    {
        if (ok) { passed++; Console.WriteLine("PASS  " + name); }
        else { failed++; Console.WriteLine("FAIL  " + name); }
    }

    private static void Intent(string text, IntentKind expected)
    {
        ParsedIntent p = VoiceIntentParser.Parse(text);
        bool ok = p.Kind == expected && p.Status == ParseStatus.Ok && p.IsActionable;
        Check(ok, "parse '" + text + "' -> " + expected + (ok ? "" : " (got " + p + ")"));
    }

    private static void Rejected(string text, ParseStatus expected)
    {
        ParsedIntent p = VoiceIntentParser.Parse(text);
        bool ok = p.Status == expected && p.Kind == IntentKind.Unknown && !p.IsActionable && p.ParseScore == 0f && p.Error != null;
        Check(ok, "reject '" + text + "' as " + expected + (ok ? "" : " (got " + p + ")"));
    }

    private static bool Near(float a, float b) { return Math.Abs(a - b) < 1e-3f; }

    private static TrialRecord Sample(DataOrigin origin, string participant, int index)
    {
        DateTime t0 = new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);
        return new TrialRecord
        {
            Origin = origin,
            SessionId = "S1",
            ParticipantPseudonym = participant,
            Condition = TrialRecord.ConditionGazeVoice,
            TrialIndex = index,
            StartUtc = t0,
            EndUtc = t0.AddSeconds(2),
            TargetName = "red cube",
            Action = "select_move_release",
            Outcome = TrialOutcome.Success,
            FailureReason = "",
            ErrorCount = 0,
            Modality = InputModality.KeyboardFallback,
            DistanceM = 1.2f,
            WidthM = 0.15f,
            MovementTimeS = 1.5f,
            CommandText = "select that"
        };
    }

    private static int CountCsvColumns(string row)
    {
        int n = 1; bool q = false;
        foreach (char c in row) { if (c == '"') q = !q; else if (c == ',' && !q) n++; }
        return n;
    }

    private static int Main()
    {
        // ---------- Parser: supported vocabulary ----------
        Intent("Select that", IntentKind.Select);
        Intent("Grab this", IntentKind.Select);
        Intent("pick up the cube", IntentKind.Select);
        Intent("  SELECT   THAT!  ", IntentKind.Select);
        Intent("select target", IntentKind.Select);
        Intent("Move there", IntentKind.Move);
        Intent("move to gaze", IntentKind.Move);
        Intent("please move it here", IntentKind.Move);
        Intent("put that over there", IntentKind.Move);
        Intent("push it left", IntentKind.Move);
        Intent("Release", IntentKind.Release);
        Intent("drop it", IntentKind.Release);
        Intent("put it down", IntentKind.Release);
        Intent("Cancel", IntentKind.Cancel);
        Intent("never mind", IntentKind.Cancel);
        Intent("go back", IntentKind.Cancel);
        Intent("Reset", IntentKind.Reset);
        Intent("start over", IntentKind.Reset);
        Intent("What can I say?", IntentKind.Help);
        Intent("help", IntentKind.Help);
        Intent("Focus", IntentKind.Focus);
        Intent("what is this", IntentKind.Focus);
        Intent("What's that?", IntentKind.Focus);

        // ---------- Parser: safe rejection ----------
        Rejected("", ParseStatus.Empty);
        Rejected("   ", ParseStatus.Empty);
        Rejected(null, ParseStatus.Empty);
        Rejected("delete scene", ParseStatus.Unknown);
        Rejected("banana", ParseStatus.Unknown);
        Rejected("make it rain", ParseStatus.Unknown);
        Rejected("selection of cubes", ParseStatus.Unknown);       // whole-word matching only
        Rejected("do not release", ParseStatus.Negated);
        Rejected("don't release it", ParseStatus.Negated);
        Rejected("Do NOT select that", ParseStatus.Negated);
        Rejected("never release", ParseStatus.Negated);
        Rejected("no cancel", ParseStatus.Negated);
        Rejected("select that, not that", ParseStatus.Negated);
        Rejected("select and release", ParseStatus.Ambiguous);
        Rejected("grab it then move there", ParseStatus.Ambiguous);
        Rejected("cancel and reset", ParseStatus.Ambiguous);
        Check(VoiceIntentParser.Parse("never mind").Status == ParseStatus.Ok, "'never mind' is Cancel, not a negation");
        Check(VoiceIntentParser.Parse("put it down").Status == ParseStatus.Ok, "'put it down' is not ambiguous with 'put'");

        // ---------- Parser: extracted names and score ----------
        ParsedIntent m = VoiceIntentParser.Parse("move it to the red cube");
        Check(m.Kind == IntentKind.Move && m.DestinationName == "red cube" && m.TargetName == null, "move it to the red cube: dest=red cube, target=gaze");
        ParsedIntent m2 = VoiceIntentParser.Parse("move the blue sphere to the shelf");
        Check(m2.TargetName == "blue sphere" && m2.DestinationName == "shelf", "move the blue sphere to the shelf: target and dest");
        Check(VoiceIntentParser.Parse("move there").DestinationName == null, "'there' means gaze destination (null name)");
        Check(VoiceIntentParser.Parse("move left").Direction == "left", "direction extracted");
        Check(VoiceIntentParser.Parse("move left").TargetName == null, "direction word is not a target name");
        Check(VoiceIntentParser.Parse("select the red cube").TargetName == "red cube", "select the red cube: named target");
        Check(VoiceIntentParser.Parse("select that").TargetName == null, "'that' means gaze target (null name)");
        Check(VoiceIntentParser.Parse("select that").ParseScore == 1f, "exact phrase parse score = 1");
        Check(VoiceIntentParser.Parse("please could you maybe select the very small red cube now").ParseScore < 1f, "wordy phrase has parse score < 1");
        Check(VoiceIntentParser.Parse("select that").Error == null, "no error on success");
        Check(VoiceIntentParser.Parse("select that").NormalizedText == "select that", "normalized text kept");

        // ---------- Grammar / fallback key phrases must all parse ----------
        bool grammarOk = VoiceIntentParser.KeywordGrammar.Length > 0;
        foreach (string phrase in VoiceIntentParser.KeywordGrammar)
            if (!VoiceIntentParser.Parse(phrase).IsActionable) { grammarOk = false; Console.WriteLine("  grammar phrase not actionable: " + phrase); }
        Check(grammarOk, "every KeywordGrammar phrase parses to an actionable intent");
        bool supportedOk = true;
        foreach (string phrase in VoiceIntentParser.SupportedPhrases)
            if (!VoiceIntentParser.Parse(phrase).IsActionable) { supportedOk = false; Console.WriteLine("  help phrase not actionable: " + phrase); }
        Check(supportedOk, "every SupportedPhrases (HELP list) entry parses");
        IntentKind[] expectedKeys = { IntentKind.Select, IntentKind.Move, IntentKind.Release, IntentKind.Cancel, IntentKind.Reset, IntentKind.Focus, IntentKind.Help };
        bool keysOk = VoiceIntentParser.FallbackKeyPhrases.Length == expectedKeys.Length;
        for (int i = 0; keysOk && i < expectedKeys.Length; i++)
            keysOk = VoiceIntentParser.Parse(VoiceIntentParser.FallbackKeyPhrases[i]).Kind == expectedKeys[i];
        Check(keysOk, "fallback keys 1-7 map to Select, Move, Release, Cancel, Reset, Focus, Help");

        // ---------- FittsMath ----------
        FittsResult r = FittsMath.Compute(3f, 1f, 1.5f);
        Check(r.IsValid && Near(r.IdBits, 2f), "ID(D=3W) = 2 bits");
        Check(Near(r.ThroughputBps, 2f / 1.5f), "TP = ID/MT = 1.333");
        Check(Near(FittsMath.Compute(1f, 1f, 1f).IdBits, 1f), "ID(D=W) = 1 bit");
        Check(Near(FittsMath.Compute(1.2f, 0.15f, 1f).IdBits, (float)(Math.Log(9.0) / Math.Log(2.0))), "ID(1.2, 0.15) = log2(9)");
        Check(r.RawMovementTimeS == 1.5f && r.UsedMovementTimeS == 1.5f && !r.SafeguardApplied, "no safeguard above floor; raw == used");
        FittsResult g = FittsMath.Compute(3f, 1f, 0.05f);
        Check(g.IsValid && g.SafeguardApplied && g.RawMovementTimeS == 0.05f && Near(g.UsedMovementTimeS, 0.1f), "safeguard: raw 0.05 kept, used 0.1");
        Check(Near(g.ThroughputBps, 2f / 0.1f), "safeguard TP uses the floor (20 bps)");
        FittsResult ng = FittsMath.Compute(3f, 1f, 0.05f, 0f);
        Check(ng.IsValid && !ng.SafeguardApplied && Near(ng.UsedMovementTimeS, 0.05f), "safeguard can be disabled (min = 0)");
        Check(FittsMath.Compute(0f, 0.2f, 1f).IsValid && FittsMath.Compute(0f, 0.2f, 1f).IdBits == 0f, "D = 0 is valid with ID = 0");
        Check(!FittsMath.Compute(1f, 0f, 1f).IsValid && FittsMath.Compute(1f, 0f, 1f).Error != null, "W = 0 invalid with message");
        Check(!FittsMath.Compute(1f, -0.1f, 1f).IsValid, "W < 0 invalid");
        Check(!FittsMath.Compute(-1f, 1f, 1f).IsValid, "D < 0 invalid");
        Check(!FittsMath.Compute(1f, 1f, 0f).IsValid, "MT = 0 invalid");
        Check(!FittsMath.Compute(1f, 1f, -2f).IsValid, "MT < 0 invalid");
        Check(!FittsMath.Compute(float.NaN, 1f, 1f).IsValid, "D NaN invalid");
        Check(!FittsMath.Compute(float.PositiveInfinity, 1f, 1f).IsValid, "D Inf invalid");
        Check(!FittsMath.Compute(1f, float.NaN, 1f).IsValid, "W NaN invalid");
        Check(!FittsMath.Compute(1f, 1f, float.NaN).IsValid, "MT NaN invalid");
        Check(!FittsMath.Compute(1f, 1f, 1f, -1f).IsValid, "negative safeguard invalid");
        FittsResult bad = FittsMath.Compute(1f, 0f, 1f);
        Check(float.IsNaN(bad.IdBits) && float.IsNaN(bad.ThroughputBps), "invalid result carries NaN fields, flagged by IsValid=false");

        // ---------- CSV helpers ----------
        Check(FittsMath.CsvEscape("plain") == "plain", "csv: plain unchanged");
        Check(FittsMath.CsvEscape("a,b") == "\"a,b\"", "csv: comma quoted");
        Check(FittsMath.CsvEscape("say \"hi\"") == "\"say \"\"hi\"\"\"", "csv: quotes doubled");
        Check(FittsMath.CsvEscape(null) == "", "csv: null -> empty");
        Check(FittsMath.CsvRow("a", "b,c", "") == "a,\"b,c\",", "csv: row join");
        Check(FittsMath.F(float.NaN, 3) == "", "csv: NaN -> empty field");
        CultureInfo old = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
        Check(FittsMath.F(1.5f, 2) == "1.50", "csv: invariant decimal point under de-DE");
        Check(Sample(DataOrigin.Selftest, "SELFTEST", 1).ToCsvRow().Contains("1.200"), "trial row uses invariant decimals under de-DE");
        Thread.CurrentThread.CurrentCulture = old;

        // ---------- TrialRecord schema ----------
        Check(CountCsvColumns(TrialRecord.CsvHeader) == TrialRecord.ColumnCount, "header has " + TrialRecord.ColumnCount + " columns");
        TrialRecord st = Sample(DataOrigin.Selftest, "SELFTEST", 1);
        Check(CountCsvColumns(st.ToCsvRow()) == TrialRecord.ColumnCount, "row column count == header column count");
        Check(st.Validate().Count == 0, "valid selftest record validates");
        string row = st.ToCsvRow();
        Check(row.StartsWith("0.1-proposed,selftest,S1,SELFTEST,gaze_voice,1,S1-T001,"), "row identity columns");
        Check(row.Contains(",success,"), "outcome column");
        Check(row.Contains(",KeyboardFallback,"), "input modality recorded (not Voice)");
        Check(Near(st.Fitts.ThroughputBps, st.Fitts.IdBits / 1.5f), "record throughput = ID / MT");
        TrialRecord realOk = Sample(DataOrigin.Real, "P07", 2);
        Check(realOk.Validate().Count == 0, "Real record with pseudonym P07 validates");
        Check(Sample(DataOrigin.Real, "A057", 1).Validate().Count > 0, "roll number rejected as Real pseudonym");
        Check(Sample(DataOrigin.Real, "Sakshi", 1).Validate().Count > 0, "name rejected as Real pseudonym");
        Check(Sample(DataOrigin.Real, "x@y.com", 1).Validate().Count > 0, "email rejected as Real pseudonym");
        Check(Sample(DataOrigin.Real, "SELFTEST", 1).Validate().Count > 0, "Real data cannot use SELFTEST");
        Check(Sample(DataOrigin.Selftest, "P01", 1).Validate().Count > 0, "Selftest data cannot masquerade as participant P01");
        Check(Sample(DataOrigin.Simulated, "SIM", 1).Validate().Count == 0, "Simulated record with SIM validates");
        TrialRecord badCond = Sample(DataOrigin.Selftest, "SELFTEST", 1); badCond.Condition = "gaze_dwell";
        Check(badCond.Validate().Count > 0, "gaze_dwell is not an approved core condition");
        TrialRecord latNonVoice = Sample(DataOrigin.Selftest, "SELFTEST", 1); latNonVoice.RecognitionLatencyMs = 120f;
        Check(latNonVoice.Validate().Count > 0, "latency rejected for non-voice input");
        TrialRecord latVoice = Sample(DataOrigin.Selftest, "SELFTEST", 1); latVoice.Modality = InputModality.Voice; latVoice.RecognitionLatencyMs = 120f;
        Check(latVoice.Validate().Count == 0 && latVoice.ToCsvRow().Contains(",120.0,"), "latency accepted and formatted for voice input");
        TrialRecord failNoReason = Sample(DataOrigin.Selftest, "SELFTEST", 1); failNoReason.Outcome = TrialOutcome.Failure; failNoReason.FailureReason = "";
        Check(failNoReason.Validate().Count > 0, "failure without reason rejected");
        TrialRecord failed1 = Sample(DataOrigin.Selftest, "SELFTEST", 1); failed1.Outcome = TrialOutcome.Failure; failed1.FailureReason = "no_gaze_target";
        Check(failed1.Validate().Count == 0 && failed1.ToCsvRow().Contains(",failure,no_gaze_target,"), "failed trial records outcome and reason");
        string[] cells = failed1.ToCsvRow().Split(',');
        Check(cells[21] == "", "failed trial has blank throughput");
        TrialRecord badGeom = Sample(DataOrigin.Selftest, "SELFTEST", 1); badGeom.WidthM = 0f;
        string badRow = badGeom.ToCsvRow();
        Check(badGeom.Fitts.IsValid == false && badRow.EndsWith("width must be > 0") && CountCsvColumns(badRow) == TrialRecord.ColumnCount, "invalid geometry: blank ID/TP and explicit fitts_note");
        TrialRecord safe = Sample(DataOrigin.Selftest, "SELFTEST", 1); safe.MovementTimeS = 0.05f;
        string[] sc = safe.ToCsvRow().Split(',');
        Check(sc[18] == "0.050" && sc[19] == "0.100" && sc[20] == "1", "safeguard visible: raw 0.050, used 0.100, flag 1");
        TrialRecord longText = Sample(DataOrigin.Selftest, "SELFTEST", 1); longText.CommandText = new string('a', 200) + "\nline2,comma";
        Check(CountCsvColumns(longText.ToCsvRow()) == TrialRecord.ColumnCount && !longText.ToCsvRow().Contains("\n"), "command text truncated and newline-free");
        TrialRecord badSession = Sample(DataOrigin.Selftest, "SELFTEST", 1); badSession.SessionId = "../evil";
        Check(badSession.Validate().Count > 0, "unsafe session id rejected");

        // ---------- TrialCsvWriter (temp folder only) ----------
        string dir = Path.Combine(Path.GetTempPath(), "group05_trialcsv_test_" + Guid.NewGuid().ToString("N"));
        var w1 = new TrialCsvWriter(dir, "S1", DataOrigin.Selftest);
        Check(w1.TryWrite(Sample(DataOrigin.Selftest, "SELFTEST", 1)), "writer: first row written");
        Check(w1.TryWrite(Sample(DataOrigin.Selftest, "SELFTEST", 2)), "writer: second row appended");
        string[] lines = File.ReadAllLines(w1.FilePath);
        Check(lines.Length == 3 && lines[0] == TrialRecord.CsvHeader, "writer: header once + two rows");
        Check(Path.GetFileName(w1.FilePath) == "trials_selftest_S1.csv", "writer: session-stamped, origin-labelled name");
        string firstContent = File.ReadAllText(w1.FilePath);
        var w2 = new TrialCsvWriter(dir, "S1", DataOrigin.Selftest);
        Check(w2.TryWrite(Sample(DataOrigin.Selftest, "SELFTEST", 1)), "writer: second writer with same session id succeeds");
        Check(Path.GetFileName(w2.FilePath) == "trials_selftest_S1_2.csv", "writer: collision gets _2 suffix");
        Check(File.ReadAllText(w1.FilePath) == firstContent, "writer: existing file NOT overwritten");
        Check(!w1.TryWrite(Sample(DataOrigin.Real, "P01", 3)) && w1.LastError != null, "writer: origin mismatch refused with message");
        TrialRecord inv = Sample(DataOrigin.Selftest, "SELFTEST", 4); inv.Condition = "nope";
        Check(!w1.TryWrite(inv) && w1.LastError.Contains("invalid record"), "writer: invalid record refused, not thrown");
        Check(File.ReadAllLines(w1.FilePath).Length == 3, "writer: refused rows leave the file unchanged");
        Check(TrialCsvWriter.SanitizeToken("../..\\x y") == "xy", "sanitize strips separators, dots and spaces");
        Check(TrialCsvWriter.SanitizeToken("...") == "x" && TrialCsvWriter.SanitizeToken(null) == "x", "sanitize falls back to 'x'");
        var wEvil = new TrialCsvWriter(dir, "../evil", DataOrigin.Selftest);
        Check(wEvil.FilePath != null && Path.GetDirectoryName(Path.GetFullPath(wEvil.FilePath)) == Path.GetFullPath(dir), "writer: hostile session id cannot leave folder");
        var wNull = new TrialCsvWriter(null, "S1", DataOrigin.Selftest);
        Check(!wNull.TryWrite(Sample(DataOrigin.Selftest, "SELFTEST", 1)) && wNull.LastError != null, "writer: null folder reported, no crash");
        Check(!w1.TryWrite(null), "writer: null record reported, no crash");
        Check(TrialCsvWriter.NewSessionId(new DateTime(2026, 10, 17, 14, 5, 9, DateTimeKind.Utc)) == "20261017T140509Z", "session id format");
        // Clean up only the files/folder this test created (non-recursive).
        foreach (string f in Directory.GetFiles(dir)) File.Delete(f);
        Directory.Delete(dir);

        // ---------- GazeInteractionModel: focus ----------
        {
            var gm = new GazeInteractionModel { MinFocusSeconds = 0.3f };
            Check(!gm.HasFocus && gm.Focus == FocusState.None && gm.Selection == SelectionState.None, "model starts with no focus and no selection");
            Check(gm.SetFocus(true, 1, "red cube", 1.0f) && gm.HasFocus && gm.FocusedName == "red cube", "focus enters a target");
            Check(!gm.SetFocus(true, 1, "red cube", 2.0f) && gm.FocusSince == 1.0f, "same target again: no change, focus time preserved");
            Check(gm.SetFocus(true, 2, "blue sphere", 3.0f) && gm.FocusedId == 2 && gm.FocusSince == 3.0f, "switching targets resets focus time");
            Check(Near(gm.FocusAge(3.5f), 0.5f), "focus age measured from entry");
            Check(gm.SetFocus(false, 0, null, 4.0f) && !gm.HasFocus && gm.FocusedName == null, "gaze away clears focus");
            Check(!gm.SetFocus(false, 0, null, 5.0f), "clearing an empty focus is not a change");

            // ---------- confirmation gating ----------
            var s = new GazeInteractionModel { MinFocusSeconds = 0.3f };
            CommandResult r0 = s.Handle(VoiceIntentParser.Parse("select that"), 1f);
            Check(r0.Outcome == CommandOutcome.Rejected && r0.Reason == "no_focus", "SELECT without focus is rejected (no_focus)");
            s.SetFocus(true, 7, "red cube", 10f);
            CommandResult r1 = s.Handle(VoiceIntentParser.Parse("select that"), 10.1f);
            Check(r1.Outcome == CommandOutcome.Rejected && r1.Reason == "focus_too_recent" && !s.HasSelection, "SELECT before min focus time is rejected");
            CommandResult r2 = s.Handle(VoiceIntentParser.Parse("select the blue sphere"), 11f);
            Check(r2.Outcome == CommandOutcome.Rejected && r2.Reason == "target_mismatch" && !s.HasSelection, "SELECT naming another target is rejected");
            CommandResult r3 = s.Handle(VoiceIntentParser.Parse("do not select that"), 11f);
            Check(r3.Outcome == CommandOutcome.Rejected && r3.Reason == "parse_negated" && !s.HasSelection, "negated SELECT never selects");
            CommandResult r4 = s.Handle(VoiceIntentParser.Parse("select and release"), 11f);
            Check(r4.Outcome == CommandOutcome.Rejected && r4.Reason == "parse_ambiguous" && !s.HasSelection, "ambiguous command never acts");
            CommandResult r5 = s.Handle(VoiceIntentParser.Parse("banana"), 11f);
            Check(r5.Outcome == CommandOutcome.Rejected && r5.Reason == "parse_unknown", "unknown command rejected");
            CommandResult r6 = s.Handle(VoiceIntentParser.Parse(""), 11f);
            Check(r6.Outcome == CommandOutcome.Rejected && r6.Reason == "parse_empty", "empty command rejected");
            Check(s.HasFocus && !s.HasSelection, "rejections leave focus unchanged and select nothing");
            CommandResult r7 = s.Handle(VoiceIntentParser.Parse("select the red cube"), 11f);
            Check(r7.Outcome == CommandOutcome.Executed && r7.Action == CommandAction.SelectedTarget && s.HasSelection && s.SelectedId == 7, "SELECT naming the focused target (held long enough) selects it");

            // ---------- selection is separate from focus ----------
            s.SetFocus(true, 8, "blue sphere", 12f);
            Check(s.HasSelection && s.SelectedId == 7 && s.FocusedId == 8, "focus moves; selection stays");
            CommandResult r8 = s.Handle(VoiceIntentParser.Parse("select that"), 13f);
            Check(r8.Outcome == CommandOutcome.Rejected && r8.Reason == "already_selected" && s.SelectedId == 7, "second SELECT is rejected while one is selected");
            CommandResult mv = s.Handle(VoiceIntentParser.Parse("move there"), 13f);
            Check(mv.Outcome == CommandOutcome.NotImplemented && mv.Reason == "not_implemented_phase3" && s.SelectedId == 7, "MOVE is NotImplemented and changes nothing");
            CommandResult rl = s.Handle(VoiceIntentParser.Parse("release"), 13f);
            Check(rl.Outcome == CommandOutcome.NotImplemented && s.HasSelection, "RELEASE is NotImplemented and keeps the selection");
            CommandResult fc = s.Handle(VoiceIntentParser.Parse("what is this"), 13f);
            Check(fc.Outcome == CommandOutcome.Executed && fc.Action == CommandAction.ReportedFocus && fc.TargetName == "blue sphere" && s.SelectedId == 7, "FOCUS reports the focused target without changing state");
            CommandResult hp = s.Handle(VoiceIntentParser.Parse("help"), 13f);
            Check(hp.Outcome == CommandOutcome.Executed && hp.Action == CommandAction.ShowedHelp && hp.Message.Contains("not available"), "HELP shows the help text");
            CommandResult cn = s.Handle(VoiceIntentParser.Parse("cancel"), 13f);
            Check(cn.Outcome == CommandOutcome.Cancelled && cn.Action == CommandAction.ClearedSelection && !s.HasSelection && s.HasFocus, "CANCEL clears the selection only");
            CommandResult cn2 = s.Handle(VoiceIntentParser.Parse("cancel"), 13f);
            Check(cn2.Outcome == CommandOutcome.Rejected && cn2.Reason == "nothing_selected", "CANCEL with nothing selected is rejected");
            s.SetFocus(true, 9, "green capsule", 20f);
            s.Handle(VoiceIntentParser.Parse("select that"), 21f);
            CommandResult rs = s.Handle(VoiceIntentParser.Parse("reset"), 21f);
            Check(rs.Outcome == CommandOutcome.Executed && rs.Action == CommandAction.ClearedSelection && !s.HasSelection, "RESET clears the selection");

            // ---------- destroyed targets ----------
            var d = new GazeInteractionModel { MinFocusSeconds = 0f };
            d.SetFocus(true, 5, "a", 0f);
            d.Handle(VoiceIntentParser.Parse("select that"), 1f);
            bool fcl, scl;
            d.TargetLost(5, out fcl, out scl);
            Check(fcl && scl && !d.HasFocus && !d.HasSelection, "TargetLost clears focus and selection of the destroyed target");
            d.SetFocus(true, 6, "b", 2f);
            d.TargetLost(99, out fcl, out scl);
            Check(!fcl && !scl && d.HasFocus, "TargetLost for an unrelated id changes nothing");

            // zero hold time allows immediate select (configurable)
            var z = new GazeInteractionModel { MinFocusSeconds = 0f };
            z.SetFocus(true, 1, "a", 5f);
            Check(z.Handle(VoiceIntentParser.Parse("select that"), 5f).Outcome == CommandOutcome.Executed, "MinFocusSeconds = 0 allows immediate select");
        }

        // ---------- Gaze event log (separate schema) ----------
        {
            Check(CountCsvColumns(GazeEventRecord.CsvHeader) == GazeEventRecord.ColumnCount, "event header column count");
            DateTime t0 = new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);
            var ev = new GazeEventRecord { Origin = DataOrigin.Selftest, SessionId = "S9", EventIndex = 1, RealtimeS = 1.5f, Utc = t0, EventType = GazeEventRecord.FocusEnter, TargetName = "red cube" };
            Check(ev.Validate().Count == 0 && CountCsvColumns(ev.ToCsvRow()) == GazeEventRecord.ColumnCount, "event row validates and matches header");
            Check(ev.ToCsvRow().StartsWith("0.1-proposed-events,selftest,S9,1,1.500,"), "event row identity and invariant formatting");
            var cmd = new GazeEventRecord { Origin = DataOrigin.Selftest, SessionId = "S9", EventIndex = 2, RealtimeS = 2f, Utc = t0, EventType = GazeEventRecord.Command, TargetName = "red cube", Modality = InputModality.KeyboardFallback, Intent = "Select", Outcome = "rejected", Reason = "focus_too_recent" };
            Check(cmd.ToCsvRow().Contains(",KeyboardFallback,Select,rejected,focus_too_recent"), "command event records modality, intent, outcome, reason");
            var real = new GazeEventRecord { Origin = DataOrigin.Real, SessionId = "S9", EventIndex = 1, Utc = t0, EventType = "x" };
            Check(real.Validate().Count > 0, "Real origin refused in the event log");
            string dir2 = Path.Combine(Path.GetTempPath(), "group05_events_test_" + Guid.NewGuid().ToString("N"));
            var ew = new GazeEventCsvWriter(dir2, "S9", DataOrigin.Selftest);
            Check(ew.TryWrite(ev) && ew.TryWrite(cmd), "event writer writes two rows");
            string[] el = File.ReadAllLines(ew.FilePath);
            Check(el.Length == 3 && el[0] == GazeEventRecord.CsvHeader && Path.GetFileName(ew.FilePath) == "gaze_events_selftest_S9.csv", "event file: header once, labelled name");
            var ew2 = new GazeEventCsvWriter(dir2, "S9", DataOrigin.Selftest);
            string before = File.ReadAllText(ew.FilePath);
            Check(ew2.TryWrite(ev) && Path.GetFileName(ew2.FilePath) == "gaze_events_selftest_S9_2.csv" && File.ReadAllText(ew.FilePath) == before, "event writer never overwrites an existing file");
            Check(!ew.TryWrite(real) && ew.LastError != null, "event writer refuses Real data without throwing");
            Check(!new GazeEventCsvWriter(null, "S9", DataOrigin.Selftest).TryWrite(ev), "event writer with null folder fails gracefully");
            foreach (string f in Directory.GetFiles(dir2)) File.Delete(f);
            Directory.Delete(dir2);
        }

        Console.WriteLine();
        Console.WriteLine("Passed: " + passed + "  Failed: " + failed);
        return failed == 0 ? 0 : 1;
    }
}
