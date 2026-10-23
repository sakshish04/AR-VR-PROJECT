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
        foreach (string moveDir in new[] { "up", "down", "forward", "back", "right" })
        {
            ParsedIntent pd = VoiceIntentParser.Parse("move " + moveDir);
            Check(pd.IsActionable && pd.Kind == IntentKind.Move && pd.Direction == moveDir && pd.TargetName == null && pd.DestinationName == null, "'move " + moveDir + "' parses as a Move with direction " + moveDir);
        }
        Check(VoiceIntentParser.Parse("move to gaze").DestinationName == "gaze", "'move to gaze': destination 'gaze' (the caller treats it as the gaze point)");
        Check(VoiceIntentParser.Parse("go back").Kind == IntentKind.Cancel, "'go back' stays Cancel (it is not a Move)");
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
        {
            // Parsing is not enough: a phrase the recognizer can return must also SUCCEED when its preconditions hold. ("select target" used to parse
            // as Select with the object name "target", so it was recognized and then always rejected with target_mismatch.)
            bool noNamedTarget = true, behavesOk = true;
            foreach (string gp in VoiceIntentParser.KeywordGrammar)
            {
                ParsedIntent gParsed = VoiceIntentParser.Parse(gp);
                if (gParsed.TargetName != null) { noNamedTarget = false; Console.WriteLine("  grammar phrase names an object: '" + gp + "' -> '" + gParsed.TargetName + "'"); }

                var gm = new GazeInteractionModel();
                gm.SetFocus(true, 1, "blue sphere", 0f);                                   // looking at a real target for 1 s
                if (gParsed.Kind != IntentKind.Select && gParsed.Kind != IntentKind.Focus && gParsed.Kind != IntentKind.Help)
                    gm.Handle(VoiceIntentParser.Parse("select that"), 1f);                  // move/release/cancel/reset need a selection
                CommandResult gr = gm.Handle(gParsed, 1f);
                bool succeeded = gr.Outcome == CommandOutcome.Executed || gr.Outcome == CommandOutcome.Cancelled;
                if (!succeeded) { behavesOk = false; Console.WriteLine("  grammar phrase rejected although its preconditions hold: '" + gp + "' -> " + gr); }
            }
            Check(noNamedTarget, "no KeywordGrammar phrase names a specific object (the recognizer cannot know object names)");
            Check(behavesOk, "every KeywordGrammar phrase succeeds in the state machine when its preconditions hold (looking at a target / target selected)");
            ParsedIntent selTarget = VoiceIntentParser.Parse("select target");
            Check(selTarget.Kind == IntentKind.Select && selTarget.TargetName == null, "'select target' means the target being looked at (no object name)");
            Check(VoiceIntentParser.Parse("select the target").TargetName == null && VoiceIntentParser.Parse("move target left").TargetName == null, "the generic word 'target' is never read as an object name");
            Check(VoiceIntentParser.Parse("select the red cube").TargetName == "red cube", "named objects still parse as named targets after the generic-word change");
        }
        bool supportedOk = true;
        foreach (string phrase in VoiceIntentParser.SupportedPhrases)
            if (!VoiceIntentParser.Parse(phrase).IsActionable) { supportedOk = false; Console.WriteLine("  help phrase not actionable: " + phrase); }
        Check(supportedOk, "every SupportedPhrases (HELP list) entry parses");
        IntentKind[] expectedKeys = { IntentKind.Select, IntentKind.Move, IntentKind.Release, IntentKind.Cancel, IntentKind.Reset, IntentKind.Focus, IntentKind.Help };
        bool keysOk = VoiceIntentParser.FallbackKeyPhrases.Length == expectedKeys.Length;
        for (int i = 0; keysOk && i < expectedKeys.Length; i++)
            keysOk = VoiceIntentParser.Parse(VoiceIntentParser.FallbackKeyPhrases[i]).Kind == expectedKeys[i];
        Check(keysOk, "fallback keys 1-7 map to Select, Move, Release, Cancel, Reset, Focus, Help");

        // ---------- quantified moves and grab/carry/drop ----------
        {
            // Without a distance nothing changes: the caller's default step (0.5 m) applies.
            foreach (string dn in new[] { "left", "right", "up", "down" })
            {
                ParsedIntent pd0 = VoiceIntentParser.Parse("move " + dn);
                Check(pd0.IsActionable && pd0.Kind == IntentKind.Move && pd0.Direction == dn && !pd0.HasDistance, "'move " + dn + "' has no distance (default step applies)");
            }
            // With a distance, for all four directions, in several spoken and typed forms.
            string[] dirs4 = { "left", "right", "up", "down" };
            foreach (string dn in dirs4)
            {
                ParsedIntent a = VoiceIntentParser.Parse("move " + dn + " 30 cm");
                ParsedIntent b = VoiceIntentParser.Parse("move " + dn + " thirty centimeters");
                ParsedIntent c = VoiceIntentParser.Parse("move this object 30 centimeters to the " + dn);
                ParsedIntent d = VoiceIntentParser.Parse("move this object thirty centimeters to the " + dn);
                bool ok = true;
                foreach (ParsedIntent q in new[] { a, b, c, d })
                    ok &= q.IsActionable && q.Kind == IntentKind.Move && q.Direction == dn && q.HasDistance && Near(q.DistanceMeters, 0.3f) && q.TargetName == null && q.DestinationName == null;
                Check(ok, "'" + dn + "' with 30 cm parses to Move/" + dn + "/0.3 m in digit and word forms, no object name invented");
            }
            Check(Near(VoiceIntentParser.Parse("move left 0.5 meters").DistanceMeters, 0.5f), "0.5 meters = 0.5 m (decimal point kept)");
            Check(Near(VoiceIntentParser.Parse("move right 1.5 m").DistanceMeters, 1.5f), "1.5 m = 1.5 m");
            Check(Near(VoiceIntentParser.Parse("move up 2 meters").DistanceMeters, 2f), "2 meters = 2 m");
            Check(Near(VoiceIntentParser.Parse("move down 10 centimeters").DistanceMeters, 0.1f), "10 centimeters = 0.1 m");
            Check(Near(VoiceIntentParser.Parse("move left 50 mm").DistanceMeters, 0.05f), "50 mm = 0.05 m");
            Check(Near(VoiceIntentParser.Parse("move left one meter").DistanceMeters, 1f), "'one meter' = 1 m");
            Check(Near(VoiceIntentParser.Parse("move left half a meter").DistanceMeters, 0.5f), "'half a meter' = 0.5 m");
            Check(Near(VoiceIntentParser.Parse("move left twenty five centimeters").DistanceMeters, 0.25f), "'twenty five centimeters' = 0.25 m");
            Check(Near(VoiceIntentParser.Parse("move left by 20 centimeters").DistanceMeters, 0.2f), "'by 20 centimeters' = 0.2 m");
            Check(VoiceIntentParser.Parse("move the red cube 30 cm to the left").TargetName == "red cube", "a named object survives next to a distance");
            Check(!VoiceIntentParser.Parse("move there").HasDistance && !VoiceIntentParser.Parse("select that").HasDistance, "commands without a distance report none");

            // The state machine: distance rules (rejected, never guessed or clamped silently).
            var qm = new GazeInteractionModel { MinFocusSeconds = 0.3f };
            qm.SetFocus(true, 1, "red cube", 0f);
            qm.Handle(VoiceIntentParser.Parse("select that"), 1f);
            CommandResult q1 = qm.Handle(VoiceIntentParser.Parse("move left thirty centimeters"), 1.1f);
            Check(q1.Outcome == CommandOutcome.Executed && q1.Action == CommandAction.MoveRequested, "quantified move is accepted when a target is selected");
            CommandResult q0 = qm.Handle(VoiceIntentParser.Parse("move left 0 centimeters"), 1.2f);
            Check(q0.Outcome == CommandOutcome.Rejected && q0.Reason == "invalid_distance", "a zero distance is rejected");
            CommandResult qbig = qm.Handle(VoiceIntentParser.Parse("move left 50 meters"), 1.3f);
            Check(qbig.Outcome == CommandOutcome.Rejected && qbig.Reason == "distance_too_large", "an absurd distance is rejected, not clamped");
            CommandResult qnd = qm.Handle(VoiceIntentParser.Parse("move there 30 centimeters"), 1.4f);
            Check(qnd.Outcome == CommandOutcome.Rejected && qnd.Reason == "distance_needs_direction", "a distance without a direction is rejected");
            var qn = new GazeInteractionModel();
            Check(qn.Handle(VoiceIntentParser.Parse("move left thirty centimeters"), 1f).Reason == "nothing_selected", "quantified move with nothing selected is rejected");

            // Grab / carry / drop.
            Check(VoiceIntentParser.Parse("grab this object").Kind == IntentKind.Grab, "'grab this object' is a Grab");
            Check(VoiceIntentParser.Parse("carry this object").Kind == IntentKind.Grab && VoiceIntentParser.Parse("pick up this object").Kind == IntentKind.Grab, "'carry this object' and 'pick up this object' are Grabs");
            Check(VoiceIntentParser.Parse("grab this").Kind == IntentKind.Select && VoiceIntentParser.Parse("pick up the cube").Kind == IntentKind.Select, "plain 'grab this' / 'pick up the cube' are still Select (old commands unchanged)");
            Check(VoiceIntentParser.Parse("drop this object").Kind == IntentKind.Release && VoiceIntentParser.Parse("drop this object").TargetName == null, "'drop this object' is a Release");
            Check(VoiceIntentParser.Parse("grab this object and drop it").Status == ParseStatus.Ambiguous, "grab and drop in one sentence is rejected as ambiguous");
            Check(VoiceIntentParser.Parse("grab this object").TargetName == null, "'grab this object' uses the gaze (no object name)");

            var gm2 = new GazeInteractionModel { MinFocusSeconds = 0.3f };
            gm2.SetFocus(true, 1, "red cube", 0f);
            CommandResult g1 = gm2.Handle(VoiceIntentParser.Parse("grab this object"), 1f);
            Check(g1.Outcome == CommandOutcome.Executed && g1.Action == CommandAction.GrabStarted && gm2.HasSelection && gm2.IsCarrying, "grab selects and starts carrying");
            Check(gm2.Handle(VoiceIntentParser.Parse("grab this object"), 1.1f).Reason == "already_carrying", "a second grab while carrying is rejected");
            Check(gm2.Handle(VoiceIntentParser.Parse("move left"), 1.2f).Reason == "carrying", "a voice move while carrying is rejected (drop first)");
            CommandResult gd = gm2.Handle(VoiceIntentParser.Parse("drop this object"), 1.3f);
            Check(gd.Outcome == CommandOutcome.Executed && gd.Action == CommandAction.Released && !gm2.IsCarrying && !gm2.HasSelection && gd.Message.StartsWith("Dropped"), "drop ends the carry and releases the object where it is");
            Check(gm2.Handle(VoiceIntentParser.Parse("drop this object"), 1.4f).Reason == "nothing_selected", "a second drop is rejected");
            var gm3 = new GazeInteractionModel { MinFocusSeconds = 0.3f };
            Check(gm3.Handle(VoiceIntentParser.Parse("grab this object"), 1f).Reason == "no_focus", "grab without looking at a target is rejected");
            gm3.SetFocus(true, 2, "blue sphere", 0f);
            Check(gm3.Handle(VoiceIntentParser.Parse("grab this object"), 0.1f).Reason == "focus_too_recent", "grab follows the same dwell rule as select");
            gm3.Handle(VoiceIntentParser.Parse("select that"), 1f);
            Check(gm3.HasSelection && !gm3.IsCarrying, "select alone does not carry");
            Check(gm3.Handle(VoiceIntentParser.Parse("grab this object"), 1.1f).Action == CommandAction.GrabStarted && gm3.IsCarrying, "grab on an already selected target starts carrying it");
            Check(gm3.Handle(VoiceIntentParser.Parse("cancel"), 1.2f).Outcome == CommandOutcome.Cancelled && !gm3.IsCarrying, "cancel ends a carry");
            gm3.Handle(VoiceIntentParser.Parse("grab this object"), 2f);
            gm3.SetMovedCount(1);
            gm3.Handle(VoiceIntentParser.Parse("reset"), 2.1f);
            Check(!gm3.IsCarrying && !gm3.HasSelection, "reset ends a carry");
            gm3.Handle(VoiceIntentParser.Parse("grab this object"), 3f);
            bool fl, sl; gm3.TargetLost(2, out fl, out sl);
            Check(!gm3.IsCarrying && sl, "losing the carried target ends the carry");

            // Every new grammar phrase for a keyword recognizer: understood, no invented object name.
            bool newGrammarOk = true; int quantified = 0;
            foreach (string gp in VoiceIntentParser.KeywordGrammar)
            {
                ParsedIntent pg = VoiceIntentParser.Parse(gp);
                if (pg.HasDistance) { quantified++; if (!pg.IsActionable || pg.Kind != IntentKind.Move || pg.Direction == null || pg.TargetName != null || pg.DestinationName != null) { newGrammarOk = false; Console.WriteLine("  bad quantified grammar phrase: " + gp + " -> " + pg); } }
            }
            Check(newGrammarOk && quantified == 48, "48 quantified-move grammar phrases (4 directions x 6 distances x 2 forms) all parse to a Move with a direction and no object name");
            Check(VoiceIntentParser.Parse("drop this object").IsActionable && VoiceIntentParser.Parse("carry this object").IsActionable, "grab/drop grammar phrases are actionable");
        }

        // ---------- speech helper protocol ----------
        {
            var ph = ExternalRecognizerProtocol.Parse("PHRASE|0.912|2026-10-23T10:22:55.123Z|move left");
            Check(ph.Kind == ExternalRecognizerProtocol.Kind.Phrase && ph.Text == "move left" && Near(ph.Confidence, 0.912f) && ph.HasSpeechEnd, "a PHRASE line parses (text, confidence, speech end)");
            Check(ExternalRecognizerProtocol.Parse("PHRASE|0.5||select that").Kind == ExternalRecognizerProtocol.Kind.Phrase && !ExternalRecognizerProtocol.Parse("PHRASE|0.5||select that").HasSpeechEnd, "a PHRASE line without a speech end is accepted, end unknown");
            Check(ExternalRecognizerProtocol.Parse("PHRASE|0.7|x|what | is this").Text == "what | is this", "the phrase text is everything after the third bar");
            Check(ExternalRecognizerProtocol.Parse("READY|MS-1033-80-DESK").Kind == ExternalRecognizerProtocol.Kind.Ready && ExternalRecognizerProtocol.Parse("ERROR|no mic").Kind == ExternalRecognizerProtocol.Kind.Error, "READY and ERROR lines parse");
            foreach (string badLine in new[] { "", null, "hello", "PHRASE|abc||x", "PHRASE|1.5||x", "PHRASE|-0.1||x", "PHRASE|NaN||x", "PHRASE|0.8||", "PHRASE|0.8", "BOGUS|1|2|3" })
                Check(ExternalRecognizerProtocol.Parse(badLine).Kind == ExternalRecognizerProtocol.Kind.Ignored, "malformed helper line ignored: '" + (badLine ?? "null") + "'");
            Check(ExternalRecognizerProtocol.RankFromConfidence(0.95f) == 0 && ExternalRecognizerProtocol.RankFromConfidence(0.50f) == 0 && ExternalRecognizerProtocol.RankFromConfidence(0.49f) == 1
                  && ExternalRecognizerProtocol.RankFromConfidence(0.22f) == 1 && ExternalRecognizerProtocol.RankFromConfidence(0.20f) == 1 && ExternalRecognizerProtocol.RankFromConfidence(0.19f) == 2 && ExternalRecognizerProtocol.RankFromConfidence(float.NaN) == 2, "confidence maps to High / Medium / Low ranks");
            var hd = ExternalRecognizerProtocol.Parse("HEARD|rejected|0.123|move lift");
            Check(hd.Kind == ExternalRecognizerProtocol.Kind.Heard && hd.Text == "move lift" && Near(hd.Confidence, 0.123f), "a HEARD line (speech detected, not matched) parses");
            Check(ExternalRecognizerProtocol.Parse("HEARD|rejected|0.0|").Kind == ExternalRecognizerProtocol.Kind.Heard && ExternalRecognizerProtocol.Parse("HEARD|rejected").Kind == ExternalRecognizerProtocol.Kind.Ignored, "an empty best guess is still a HEARD line; a truncated one is ignored");
            Check(ExternalRecognizerProtocol.RankName(0) == "High" && ExternalRecognizerProtocol.RankName(1) == "Medium" && ExternalRecognizerProtocol.RankName(2) == "Low", "rank names");
            var t0 = new DateTime(2026, 10, 23, 10, 0, 0, DateTimeKind.Utc);
            var withEnd = ExternalRecognizerProtocol.Parse("PHRASE|0.9|2026-10-23T10:00:00.000Z|drop it");
            Check(Near(ExternalRecognizerProtocol.SinceSpeechEndMs(withEnd, t0.AddMilliseconds(450)), 450f), "speech-end to event is computed in ms");
            Check(float.IsNaN(ExternalRecognizerProtocol.SinceSpeechEndMs(withEnd, t0.AddMilliseconds(-5))) && float.IsNaN(ExternalRecognizerProtocol.SinceSpeechEndMs(withEnd, t0.AddMinutes(5))), "an implausible speech-end time is reported as unknown, not invented");
            // every grammar phrase the helper may print is one the parser understands
            bool allGrammar = true;
            foreach (string gp in VoiceIntentParser.KeywordGrammar)
                if (ExternalRecognizerProtocol.Parse("PHRASE|0.9||" + gp).Text != gp) allGrammar = false;
            Check(allGrammar, "every grammar phrase survives the helper protocol unchanged");
        }

        // ---------- lab docks ----------
        {
            Check(LabPlacementRules.IsOverPad(0.1f, 0.0f, 0.4f), "an item resting on the pad is over it");
            Check(LabPlacementRules.IsOverPad(0.4f, 0.5f, 0.4f), "an item above the pad edge (dropped from hand height) is over it");
            Check(!LabPlacementRules.IsOverPad(0.5f, 0.0f, 0.4f), "an item beside the pad is not over it");
            Check(!LabPlacementRules.IsOverPad(0.1f, 1.5f, 0.4f), "an item far above the pad (held high) is not over it");
            Check(!LabPlacementRules.IsOverPad(0.1f, -0.5f, 0.4f), "an item far below the pad is not over it");
            Check(!LabPlacementRules.IsOverPad(0.1f, 0.0f, 0f) && !LabPlacementRules.IsOverPad(float.NaN, 0f, 0.4f), "a zero radius or NaN is never over the pad");
            Check(!LabPlacementRules.HasLeftPad(0.5f, 0.4f) && LabPlacementRules.HasLeftPad(0.7f, 0.4f), "an item must move clearly away before its dock is free again");
            Check(LabPlacementRules.Progress(3, 9) == "3 / 9" && LabPlacementRules.Progress(-1, 9) == "0 / 9", "progress text");
            Check(LabPlacementRules.FormatDuration(75f) == "1:15" && LabPlacementRules.FormatDuration(5f) == "0:05" && LabPlacementRules.FormatDuration(float.NaN) == "0:00", "duration text");
        }

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
            // MOVE needs a selection; the state machine accepts it and the caller performs it (the model has no positions).
            CommandResult mv = s.Handle(VoiceIntentParser.Parse("move there"), 13f);
            Check(mv.Outcome == CommandOutcome.Executed && mv.Action == CommandAction.MoveRequested && s.SelectedId == 7 && s.HasSelection, "MOVE with a selection is accepted and keeps the selection");
            CommandResult mvDir = s.Handle(VoiceIntentParser.Parse("move left"), 13f);
            Check(mvDir.Outcome == CommandOutcome.Executed && mvDir.Action == CommandAction.MoveRequested, "MOVE with a direction is accepted");
            CommandResult mvOk = s.Handle(VoiceIntentParser.Parse("move the red cube right"), 13f);
            Check(mvOk.Outcome == CommandOutcome.Executed && mvOk.Action == CommandAction.MoveRequested, "MOVE naming the selected target is accepted");
            CommandResult mvBad = s.Handle(VoiceIntentParser.Parse("move the blue sphere left"), 13f);
            Check(mvBad.Outcome == CommandOutcome.Rejected && mvBad.Reason == "target_mismatch" && s.SelectedId == 7, "MOVE naming a different target is rejected and keeps the selection");
            CommandResult fc = s.Handle(VoiceIntentParser.Parse("what is this"), 13f);
            Check(fc.Outcome == CommandOutcome.Executed && fc.Action == CommandAction.ReportedFocus && fc.TargetName == "blue sphere" && s.SelectedId == 7, "FOCUS reports the focused target without changing state");
            CommandResult hp = s.Handle(VoiceIntentParser.Parse("help"), 13f);
            Check(hp.Outcome == CommandOutcome.Executed && hp.Action == CommandAction.ShowedHelp && hp.Message.Contains("release") && hp.Message.Contains("reset") && !hp.Message.Contains("not available"), "HELP describes the implemented commands");
            CommandResult cn = s.Handle(VoiceIntentParser.Parse("cancel"), 13f);
            Check(cn.Outcome == CommandOutcome.Cancelled && cn.Action == CommandAction.ClearedSelection && !s.HasSelection && s.HasFocus, "CANCEL clears the selection only");
            CommandResult cn2 = s.Handle(VoiceIntentParser.Parse("cancel"), 13f);
            Check(cn2.Outcome == CommandOutcome.Rejected && cn2.Reason == "nothing_selected", "CANCEL with nothing selected is rejected");
            // MOVE and RELEASE without a selection
            CommandResult mvNone = s.Handle(VoiceIntentParser.Parse("move there"), 14f);
            Check(mvNone.Outcome == CommandOutcome.Rejected && mvNone.Reason == "nothing_selected" && mvNone.Action == CommandAction.None, "MOVE with nothing selected is rejected (nothing_selected)");
            CommandResult rlNone = s.Handle(VoiceIntentParser.Parse("release"), 14f);
            Check(rlNone.Outcome == CommandOutcome.Rejected && rlNone.Reason == "nothing_selected", "RELEASE with nothing selected is rejected (nothing_selected)");

            // RELEASE commits and deselects (the target stays where it is; the caller does not move it)
            s.SetFocus(true, 7, "red cube", 30f);
            s.Handle(VoiceIntentParser.Parse("select that"), 31f);
            Check(s.HasSelection, "re-select after cancel");
            CommandResult rl = s.Handle(VoiceIntentParser.Parse("release"), 31f);
            Check(rl.Outcome == CommandOutcome.Executed && rl.Action == CommandAction.Released && !s.HasSelection && rl.TargetName == "red cube" && rl.Message.Contains("stays"), "RELEASE executes, deselects and reports the target");
            CommandResult rl2 = s.Handle(VoiceIntentParser.Parse("drop it"), 31f);
            Check(rl2.Outcome == CommandOutcome.Rejected && rl2.Reason == "nothing_selected", "a second RELEASE is rejected (nothing to release)");
            Check(s.HasFocus, "RELEASE does not change focus");

            // RESET: selection only (nothing moved) keeps the original behaviour
            s.SetFocus(true, 9, "green capsule", 40f);
            s.Handle(VoiceIntentParser.Parse("select that"), 41f);
            CommandResult rs = s.Handle(VoiceIntentParser.Parse("reset"), 41f);
            Check(rs.Outcome == CommandOutcome.Executed && rs.Action == CommandAction.ClearedSelection && !s.HasSelection, "RESET clears the selection");
            CommandResult rsNone = s.Handle(VoiceIntentParser.Parse("reset"), 41f);
            Check(rsNone.Outcome == CommandOutcome.Rejected && rsNone.Reason == "nothing_selected", "RESET with nothing selected and nothing moved is rejected");

            // RESET with moved targets: asks the caller to restore start poses, with or without a selection
            s.Handle(VoiceIntentParser.Parse("select that"), 42f);
            s.SetMovedCount(2);
            CommandResult rsMoved = s.Handle(VoiceIntentParser.Parse("reset"), 42f);
            Check(rsMoved.Outcome == CommandOutcome.Executed && rsMoved.Action == CommandAction.RestoredInitialState && !s.HasSelection && s.MovedCount == 0 && rsMoved.Message.Contains("2"), "RESET with moved targets restores the start state and clears the selection");
            s.SetMovedCount(1);
            CommandResult rsMoved2 = s.Handle(VoiceIntentParser.Parse("start over"), 43f);
            Check(rsMoved2.Outcome == CommandOutcome.Executed && rsMoved2.Action == CommandAction.RestoredInitialState && s.MovedCount == 0, "RESET restores moved targets even with no selection");
            s.SetMovedCount(-5);
            Check(s.MovedCount == 0, "moved count never goes negative");

            // CANCEL still only clears selection in the model (the caller undoes the physical moves)
            s.Handle(VoiceIntentParser.Parse("select that"), 44f);
            CommandResult cnMoved = s.Handle(VoiceIntentParser.Parse("cancel"), 44f);
            Check(cnMoved.Outcome == CommandOutcome.Cancelled && cnMoved.Action == CommandAction.ClearedSelection && !s.HasSelection, "CANCEL clears the selection (physical undo is the caller's job)");

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

        // ---------- Voice grammar alternates (added after the logs showed "release" was never delivered but "drop it" was) ----------
        {
            string[,] alt =
            {
                { "release it", "Release" }, { "let go", "Release" }, { "put it down", "Release" }, { "drop it", "Release" }, { "release", "Release" },
                { "cancel that", "Cancel" }, { "never mind", "Cancel" }, { "cancel", "Cancel" },
                { "start over", "Reset" }, { "reset", "Reset" },
                { "select this", "Select" }, { "select that", "Select" }
            };
            for (int i = 0; i < alt.GetLength(0); i++)
            {
                ParsedIntent a = VoiceIntentParser.Parse(alt[i, 0]);
                Check(a.IsActionable && a.Kind.ToString() == alt[i, 1] && a.TargetName == null && a.Direction == null,
                      "'" + alt[i, 0] + "' parses as " + alt[i, 1] + " with no target/direction");
            }
            foreach (string needed in new[] { "release", "drop it", "release it", "let go", "put it down", "cancel", "cancel that", "never mind", "reset", "start over", "select that", "select this" })
                Check(Array.IndexOf(VoiceIntentParser.KeywordGrammar, needed) >= 0, "KeywordGrammar contains '" + needed + "'");
            var seenPhrases = new System.Collections.Generic.HashSet<string>();
            bool noDuplicates = true;
            foreach (string gramPhrase in VoiceIntentParser.KeywordGrammar) if (!seenPhrases.Add(gramPhrase.ToLowerInvariant())) noDuplicates = false;
            Check(noDuplicates, "KeywordGrammar has no duplicate phrases (a duplicate would be one recognizer entry counted twice)");
            foreach (string moveDir in new[] { "left", "right", "up", "down", "forward", "back" })
                Check(Array.IndexOf(VoiceIntentParser.KeywordGrammar, "move " + moveDir) >= 0, "KeywordGrammar contains 'move " + moveDir + "'");
        }

        // ---------- Confidence policy (recognizer is created at its lowest floor; this applies the real bar) ----------
        {
            int H = VoiceConfidencePolicy.RankHigh, M = VoiceConfidencePolicy.RankMedium, L = VoiceConfidencePolicy.RankLow, R = VoiceConfidencePolicy.RankRejected;
            Check(VoiceConfidencePolicy.Decide(H, M, IntentKind.Move, true) == ConfidenceDecision.Accept, "High confidence move accepted (floor Medium)");
            Check(VoiceConfidencePolicy.Decide(M, M, IntentKind.Select, true) == ConfidenceDecision.Accept, "Medium confidence select accepted (floor Medium)");
            Check(VoiceConfidencePolicy.Decide(L, M, IntentKind.Move, true) == ConfidenceDecision.IgnoreWithNotice, "Low confidence MOVE is NOT executed");
            Check(VoiceConfidencePolicy.Decide(L, M, IntentKind.Select, true) == ConfidenceDecision.IgnoreWithNotice, "Low confidence SELECT is NOT executed");
            foreach (IntentKind k in new[] { IntentKind.Cancel, IntentKind.Reset, IntentKind.Release, IntentKind.Help, IntentKind.Focus })
            {
                Check(VoiceConfidencePolicy.Decide(L, M, k, true) == ConfidenceDecision.Accept, "Low confidence " + k + " accepted (undo/info cannot move or select)");
                Check(VoiceConfidencePolicy.Decide(L, M, k, false) == ConfidenceDecision.IgnoreWithNotice, "Low confidence " + k + " ignored when the allowance is switched off");
            }
            Check(VoiceConfidencePolicy.Decide(R, M, IntentKind.Cancel, true) == ConfidenceDecision.IgnoreWithNotice, "Rejected confidence is never accepted");
            Check(VoiceConfidencePolicy.Decide(L, L, IntentKind.Move, true) == ConfidenceDecision.Accept, "floor Low accepts a Low phrase");
            Check(VoiceConfidencePolicy.Decide(M, H, IntentKind.Move, true) == ConfidenceDecision.IgnoreWithNotice, "floor High ignores a Medium move");
        }

        // ---------- HUD text formatting shared by the desktop HUD and the headset panel ----------
        {
            Check(HudText.Wrap("short", 50) == "short", "Wrap leaves short text unchanged");
            Check(HudText.Wrap(null, 50) == "" && HudText.Wrap("", 50) == "", "Wrap handles null and empty");
            string hudLongText = "LAST: [VOICE] 'move left' -> Executed: Moved red cube left by 0.5 m and then some more words to force wrapping";
            string w = HudText.Wrap(hudLongText, 30);
            bool within = true;
            foreach (string line in w.Split('\n')) if (line.Length > 30) within = false;
            Check(within, "Wrap: no line exceeds the limit (when no single word is longer)");
            Check(w.Replace('\n', ' ') == hudLongText, "Wrap keeps every word, in order, with nothing added or lost");
            Check(HudText.Wrap("abcdefghijklmnopqrstuvwxyz tail", 10).StartsWith("abcdefghijklmnopqrstuvwxyz"), "Wrap never cuts a word longer than the limit");
            Check(HudText.CountLines("") == 0 && HudText.CountLines(null) == 0 && HudText.CountLines("a") == 1 && HudText.CountLines("a\nb\nc") == 3, "CountLines");
            string withSel = HudText.CommandHints(true, false), noSel = HudText.CommandHints(false, false);
            Check(withSel.Contains("drop it") && withSel.Contains("cancel") && withSel.Contains("move there"), "hints with a selection mention move, drop it and cancel");
            Check(noSel.Contains("select that") && noSel.Contains("reset") && !noSel.Contains("move left"), "hints without a selection offer select/reset, not move");
            Check(HudText.CommandHints(true, true).Length < withSel.Length, "compact hints are shorter than full hints");
            // every phrase a hint tells the user to say must be one the parser understands
            foreach (string spoken in new[] { "select that", "what is this", "reset", "help", "release", "drop it", "cancel", "move there", "move left", "move right", "move up", "move down", "move forward", "move back" })
                Check(VoiceIntentParser.Parse(spoken).IsActionable, "hinted phrase '" + spoken + "' is actionable");

            // WrapLimited: the headset panel caps every row so a long result cannot grow the panel into the centre of the view
            Check(HudText.WrapLimited("short", 42, 3) == "short", "WrapLimited leaves short text unchanged");
            Check(HudText.WrapLimited(null, 42, 3) == "" && HudText.WrapLimited("", 42, 3) == "", "WrapLimited handles null and empty");
            string worstResult = "LAST: [VOICE] 'select that' -> Rejected (no_focus): You are not looking at a valid target. Speech results arrive about a second after you stop talking; keep looking at the target until the result appears.";
            string capped = HudText.WrapLimited(worstResult, 42, 3);
            string[] cappedLines = capped.Split('\n');
            bool cappedFit = true;
            foreach (string cl in cappedLines) if (cl.Length > 42) cappedFit = false;
            Check(cappedLines.Length == 3, "WrapLimited: a 200-character result is cut to exactly 3 lines");
            Check(cappedFit, "WrapLimited: every kept line, including the one with the ellipsis, fits the character limit");
            Check(capped.EndsWith("..."), "WrapLimited: dropped text is marked with an ellipsis");
            Check(capped.Replace('\n', ' ').StartsWith("LAST: [VOICE] 'select that' -> Rejected (no_focus): You are not looking at a valid target."), "WrapLimited keeps the start of the message (command, outcome and reason), not the end");
            Check(HudText.WrapLimited("one two three four five six seven eight nine ten", 12, 10) == HudText.Wrap("one two three four five six seven eight nine ten", 12), "WrapLimited equals Wrap when the line cap is not reached");
            string twoLine = HudText.WrapLimited("aaa bbb ccc ddd eee", 7, 2);
            Check(HudText.CountLines(twoLine) == 2 && twoLine.EndsWith("..."), "WrapLimited: a two-line cap on a longer text gives two lines ending in an ellipsis");
            Check(HudText.CountLines(HudText.WrapLimited("anything at all here", 5, 0)) == 1, "WrapLimited treats a cap below 1 as 1 line");

            // TypedBoxText: how the F8 typed-command box is mirrored into the headset panel
            Check(HudText.TypedBoxText("", 18) == "_" && HudText.TypedBoxText(null, 18) == "_", "TypedBoxText: an empty box shows just the caret");
            Check(HudText.TypedBoxText("move left", 18) == "move left_", "TypedBoxText shows short text followed by a caret");
            string longTyped = HudText.TypedBoxText("select that and then move it somewhere else", 18);
            Check(longTyped.Length <= 18 && longTyped.StartsWith("...") && longTyped.EndsWith("else_"), "TypedBoxText keeps the END of a long entry (where the user is typing), never longer than the limit");
            Check(HudText.TypedBoxText("abcdefghijklmnop", 18) == "abcdefghijklmnop_", "TypedBoxText: text that exactly fits is not shortened");
            Check(HudText.TypedBoxText("x", 1).Length <= 4, "TypedBoxText never returns more than the minimum sane width for a tiny limit");
        }

        // ---------- Phase 03: voice telemetry CSV (schema version 1, 15 columns) ----------
        {
            string[] cols =
            {
                "schema_version", "session_id", "sequence_id", "timestamp_utc", "realtime_s", "platform", "recognized_phrase", "confidence_rank",
                "minimum_confidence_rank", "intent", "decision", "reason", "speech_end_to_event_ms", "parse_duration_ms", "action_outcome"
            };
            Check(VoiceTelemetryRecord.CsvHeader == string.Join(",", cols) && VoiceTelemetryRecord.ColumnCount == 15 && VoiceTelemetryRecord.CsvHeader.Split(',').Length == 15,
                  "voice telemetry header: exactly the 15 agreed columns in the agreed order");
            Check(GazeEventRecord.CsvHeader == "schema_version,data_origin,session_id,event_index,realtime_s,utc,event_type,target_name,input_modality,intent,outcome,reason",
                  "the existing gaze event CSV header is unchanged (voice telemetry is a separate file)");
            Check(TrialRecord.CsvHeader == "schema_version,data_origin,session_id,participant_pseudonym,condition,trial_index,trial_id,start_utc,end_utc,target_name,action,outcome,failure_reason,error_count,input_modality," +
                  "target_distance_m,target_width_m,fitts_id_bits,movement_time_raw_s,movement_time_used_s,mt_safeguard_applied,throughput_bps,recognition_latency_ms,command_text,fitts_note",
                  "the existing trial CSV header is unchanged");

            // minimal CSV line reader (RFC 4180) so the tests read rows back the way an analysis tool would
            Func<string, System.Collections.Generic.List<string>> parseLine = line =>
            {
                var fields = new System.Collections.Generic.List<string>();
                var cur = new System.Text.StringBuilder();
                bool inQ = false;
                for (int i = 0; i < line.Length; i++)
                {
                    char c = line[i];
                    if (inQ) { if (c == '"') { if (i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; } else inQ = false; } else cur.Append(c); }
                    else if (c == '"') inQ = true;
                    else if (c == ',') { fields.Add(cur.ToString()); cur.Clear(); }
                    else cur.Append(c);
                }
                fields.Add(cur.ToString());
                return fields;
            };
            Func<VoiceTelemetryRecord> good = () => new VoiceTelemetryRecord
            {
                SessionId = "example_session", SequenceId = 1, TimestampUtc = new DateTime(2026, 10, 19, 10, 0, 0, DateTimeKind.Utc), RealtimeS = 12.45f,
                Platform = "WindowsPlayer", RecognizedPhrase = "move left", ConfidenceRank = "Medium", MinimumConfidenceRank = "Medium", Intent = "Move",
                Decision = "accepted", Reason = "confidence_pass", SpeechEndToEventMs = 420f, ParseDurationMs = 0.02f, ActionOutcome = "executed"
            };

            // exact row, the missing-value rule, and invariant formatting
            string expectedRow = "1,example_session,1,2026-10-19T10:00:00.000Z,12.450,WindowsPlayer,move left,Medium,Medium,Move,accepted,confidence_pass,420,0.02,executed";
            Check(good().ToCsvRow() == expectedRow, "voice telemetry row: exact serialization (ISO UTC timestamp, 3-decimal realtime, trimmed ms values)");
            var noTiming = good(); noTiming.SpeechEndToEventMs = float.NaN; noTiming.ParseDurationMs = float.NaN; noTiming.ActionOutcome = null; noTiming.ConfidenceRank = null;
            string noTimingRow = noTiming.ToCsvRow();
            var noTimingFields = parseLine(noTimingRow);
            Check(noTimingFields.Count == 15 && noTimingFields[12] == "" && noTimingFields[13] == "" && noTimingFields[14] == "" && noTimingFields[7] == "",
                  "missing measurements and outcomes are EMPTY fields, and the row still has 15 columns");
            Check(!noTimingRow.Contains("NaN") && !noTimingRow.Contains("Infinity"), "no NaN or Infinity text is ever written");
            var infinite = good(); infinite.SpeechEndToEventMs = float.PositiveInfinity;
            Check(infinite.Validate().Count > 0, "an infinite measurement is rejected, not written");
            var savedCulture = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                Check(good().ToCsvRow() == expectedRow, "voice telemetry formatting ignores the machine culture (a German decimal comma would break the CSV)");
            }
            finally { Thread.CurrentThread.CurrentCulture = savedCulture; }
            Check(VoiceTelemetryRecord.Number(0.01f) == "0.01" && VoiceTelemetryRecord.Number(420f) == "420" && VoiceTelemetryRecord.Number(float.NaN) == "",
                  "Number(): trimmed invariant decimals, empty for an unavailable value");

            // escaping: commas, quotes and line breaks in a field must round-trip
            var tricky = good(); tricky.RecognizedPhrase = "say \"hi\", then\nstop"; tricky.ActionOutcome = "a,b";
            string trickyRow = tricky.ToCsvRow();
            var trickyFields = parseLine(trickyRow);
            Check(trickyFields.Count == 15 && trickyFields[6] == "say \"hi\", then\nstop" && trickyFields[14] == "a,b", "commas, quotes and newlines are escaped and read back unchanged");

            // validation
            Func<Action<VoiceTelemetryRecord>, bool> rejects = mutate => { var r = good(); mutate(r); return r.Validate().Count > 0; };
            Check(good().Validate().Count == 0, "a complete record validates");
            Check(rejects(r => r.Decision = "maybe") && rejects(r => r.Decision = null), "validation: decision must be accepted, ignored or error");
            Check(rejects(r => r.TimestampUtc = new DateTime(2026, 10, 19, 10, 0, 0, DateTimeKind.Local)) && rejects(r => r.TimestampUtc = new DateTime(2026, 10, 19, 10, 0, 0, DateTimeKind.Unspecified)),
                  "validation: the timestamp must be a UTC DateTime");
            Check(rejects(r => r.RealtimeS = -0.1f) && rejects(r => r.RealtimeS = float.NaN), "validation: realtime_s must be finite and non-negative");
            Check(rejects(r => r.SpeechEndToEventMs = -5f) && rejects(r => r.ParseDurationMs = -0.01f), "validation: negative durations are rejected");
            Check(rejects(r => r.SequenceId = 0) && rejects(r => r.SessionId = "vbad id/..") && rejects(r => r.SessionId = ""), "validation: positive sequence_id and a safe, non-empty session_id");
            Check(rejects(r => r.Platform = "") && rejects(r => r.Reason = null) && rejects(r => r.RecognizedPhrase = ""), "validation: platform, reason and (for non-error rows) the phrase are required");
            var errRow = good(); errRow.Decision = "error"; errRow.Reason = "recognizer_error"; errRow.RecognizedPhrase = null; errRow.ConfidenceRank = null; errRow.Intent = null;
            errRow.SpeechEndToEventMs = float.NaN; errRow.ParseDurationMs = float.NaN; errRow.ActionOutcome = null;
            Check(errRow.Validate().Count == 0 && parseLine(errRow.ToCsvRow()).Count == 15, "an error row needs no phrase and still has 15 columns");

            // decision / reason / intent / outcome mapping, driven by the real confidence policy and parser
            Action<string, int, int, string, string, string> expectClass = (phrase, rank, floor, wantDecision, wantReason, label) =>
            {
                ParsedIntent p = VoiceIntentParser.Parse(phrase);
                ConfidenceDecision d = VoiceConfidencePolicy.Decide(rank, floor, p.Kind, true);
                string dec, rea;
                VoiceTelemetryRecord.Classify(rank, floor, p, d, out dec, out rea);
                Check(dec == wantDecision && rea == wantReason, "classify: " + label + " -> " + dec + "/" + rea);
            };
            int H = VoiceConfidencePolicy.RankHigh, M = VoiceConfidencePolicy.RankMedium, L = VoiceConfidencePolicy.RankLow, R = VoiceConfidencePolicy.RankRejected;
            expectClass("select that", M, M, "accepted", "confidence_pass", "select at Medium (floor Medium)");
            expectClass("move left", H, M, "accepted", "confidence_pass", "move at High (floor Medium)");
            expectClass("select that", L, M, "ignored", "low_confidence", "select at Low is ignored");
            expectClass("move left", L, M, "ignored", "low_confidence", "move at Low is ignored");
            expectClass("drop it", L, M, "accepted", "low_confidence_undo_allowed", "release at Low is accepted as an undo/info command");
            expectClass("cancel", L, M, "accepted", "low_confidence_undo_allowed", "cancel at Low is accepted as an undo/info command");
            expectClass("select that", R, M, "ignored", "low_confidence", "a Rejected-level phrase is ignored");
            expectClass("banana", M, M, "ignored", "parse_rejected", "a phrase the parser rejects is ignored with parse_rejected");
            expectClass("select that", L, L, "accepted", "confidence_pass", "Low is enough when the floor is Low");
            Check(VoiceTelemetryRecord.IntentText(VoiceIntentParser.Parse("move left")) == "Move" && VoiceTelemetryRecord.IntentText(VoiceIntentParser.Parse("banana")) == "",
                  "intent column: the parsed intent, or empty (never 'Unknown')");
            Check(VoiceTelemetryRecord.OutcomeCode(CommandOutcome.Executed, "") == "executed" && VoiceTelemetryRecord.OutcomeCode(CommandOutcome.Cancelled, "") == "cancelled" &&
                  VoiceTelemetryRecord.OutcomeCode(CommandOutcome.Rejected, "no_focus") == "no_focus" && VoiceTelemetryRecord.OutcomeCode(CommandOutcome.Rejected, "") == "rejected" &&
                  VoiceTelemetryRecord.OutcomeCode(CommandOutcome.NotImplemented, "") == "not_implemented", "action_outcome codes: executed, cancelled, the rejection reason, not_implemented");

            // session ids
            string sid = VoiceTelemetryCsvWriter.NewSessionId(new DateTime(2026, 10, 19, 10, 0, 0, DateTimeKind.Utc), "ab12");
            Check(sid == "20261019T100000Z-ab12", "session id: UTC timestamp plus a short unique suffix");
            Check(VoiceTelemetryCsvWriter.NewSessionId(DateTime.UtcNow, "a") != VoiceTelemetryCsvWriter.NewSessionId(DateTime.UtcNow, "b"), "two sessions started in the same second get different ids");

            // the writer, on real files
            string vdir = Path.Combine(Path.GetTempPath(), "group05_voicetel_test_" + Guid.NewGuid().ToString("N"));
            try
            {
                var w = new VoiceTelemetryCsvWriter(vdir, "S1-abcd");
                Check(!Directory.Exists(vdir) && w.RowsWritten == 0, "the file and folder are not created until a row is actually written");
                Check(w.FilePath.EndsWith("voice_telemetry_S1-abcd.csv"), "file name pattern: voice_telemetry_<sessionId>.csv");
                Check(w.TryWrite(good()) && w.RowsWritten == 1 && File.Exists(w.FilePath), "the first row creates the file");
                var second = good(); second.RecognizedPhrase = "move right"; var third = good(); third.RecognizedPhrase = "cancel";
                Check(w.TryWrite(second) && w.TryWrite(third) && w.RowsWritten == 3, "later rows are appended");
                string[] vlines = File.ReadAllLines(w.FilePath);
                Check(vlines.Length == 4 && vlines[0] == VoiceTelemetryRecord.CsvHeader, "the file holds the header once, then one line per row");
                var f1 = parseLine(vlines[1]); var f2 = parseLine(vlines[2]); var f3 = parseLine(vlines[3]);
                Check(f1[2] == "1" && f2[2] == "2" && f3[2] == "3", "sequence_id counts 1, 2, 3 within the session");
                Check(f1[1] == "S1-abcd" && f2[1] == "S1-abcd" && f3[1] == "S1-abcd", "the writer, not the caller, owns session_id");
                bool allWellFormed = true;
                for (int i = 1; i < vlines.Length; i++) { var ff = parseLine(vlines[i]); if (ff.Count != 15 || !ff[3].EndsWith("Z")) allWellFormed = false; }
                Check(allWellFormed, "every row has 15 fields and a UTC timestamp ending in Z");
                string before = File.ReadAllText(w.FilePath);

                // same session id again (a restarted writer): a NEW file, the old one is never touched
                var vw2 = new VoiceTelemetryCsvWriter(vdir, "S1-abcd");
                Check(vw2.TryWrite(good()) && vw2.FilePath != w.FilePath && vw2.FilePath.EndsWith("voice_telemetry_S1-abcd_2.csv") && File.ReadAllText(w.FilePath) == before,
                      "a name collision picks a new suffix and never overwrites or appends to the earlier file");
                Check(File.ReadAllLines(vw2.FilePath).Length == 2 && parseLine(File.ReadAllLines(vw2.FilePath)[1])[2] == "1", "the new file starts its own header and sequence at 1");

                // an invalid row is rejected without consuming a sequence number
                var w3 = new VoiceTelemetryCsvWriter(vdir, "S3-zzzz");
                var vbad = good(); vbad.Decision = "maybe";
                Check(!w3.TryWrite(vbad) && w3.LastError != null && w3.RowsWritten == 0 && !File.Exists(w3.FilePath), "an invalid record is refused, reported, and creates no file");
                Check(w3.TryWrite(good()) && parseLine(File.ReadAllLines(w3.FilePath)[1])[2] == "1", "the next valid row after a refused one still gets sequence_id 1");
                Check(!w3.TryWrite(null) && w3.LastError != null, "a null record is refused without throwing");

                // write failure: the output 'folder' is actually a file; must report, never throw
                string notAFolder = Path.Combine(vdir, "blocker.txt");
                File.WriteAllText(notAFolder, "x");
                var w4 = new VoiceTelemetryCsvWriter(notAFolder, "S4-qqqq");
                bool threw = false, wrote = true;
                try { wrote = w4.TryWrite(good()); } catch (Exception) { threw = true; }
                Check(!threw && !wrote && w4.LastError != null && w4.RowsWritten == 0, "a write failure returns false with LastError set and never throws");
                Check(!new VoiceTelemetryCsvWriter(null, "S5").TryWrite(good()) && !new VoiceTelemetryCsvWriter("", "S5").TryWrite(good()), "a missing output folder is a reported failure");
                // a transient failure must not end the session's data: the same writer succeeds once the problem is gone, with no sequence number lost
                string retryDir = Path.Combine(vdir, "retry");
                File.WriteAllText(retryDir, "a file where the folder should be");
                var vw6 = new VoiceTelemetryCsvWriter(retryDir, "S6-retr");
                Check(!vw6.TryWrite(good()) && vw6.RowsWritten == 0, "retry setup: the first write fails while the output folder is blocked");
                File.Delete(retryDir);
                Check(vw6.TryWrite(good()) && vw6.RowsWritten == 1 && parseLine(File.ReadAllLines(vw6.FilePath)[1])[2] == "1",
                      "the same writer succeeds on the next event once the problem is gone, and sequence_id is still 1 (a failed write consumed none)");
                var hostile = new VoiceTelemetryCsvWriter(vdir, "..\\..\\evil");
                Check(hostile.SessionId == "evil" && hostile.FilePath.StartsWith(vdir), "a session id with path characters cannot escape the output folder");
            }
            finally
            {
                if (Directory.Exists(vdir)) Directory.Delete(vdir, true);   // a folder this test created under %TEMP% (it now has a sub-folder)
            }
        }

        // ---------- Audit additions: remaining directions, help text, rapid repeats, state-machine invariants ----------
        {
            foreach (string extra in new[] { "closer", "away", "backward" })
            {
                ParsedIntent pe = VoiceIntentParser.Parse("move " + extra);
                Check(pe.IsActionable && pe.Kind == IntentKind.Move && pe.Direction == extra, "'move " + extra + "' parses as a Move with direction " + extra);
            }
            Check(GazeInteractionModel.HelpText.Contains("drop it") && GazeInteractionModel.HelpText.Contains("release") && GazeInteractionModel.HelpText.Contains("cancel")
                  && GazeInteractionModel.HelpText.Contains("reset") && GazeInteractionModel.HelpText.Contains("select that"), "help text names select, release/drop it, cancel and reset");

            // rapid successive activations: the model has no hidden debounce, so repeats are honest; one selection survives any number of moves
            var rm = new GazeInteractionModel { MinFocusSeconds = 0.3f };
            rm.SetFocus(true, 1, "red cube", 0f);
            Check(rm.Handle(VoiceIntentParser.Parse("select that"), 1f).Outcome == CommandOutcome.Executed, "rapid: select succeeds");
            int movedOk = 0;
            for (int i = 0; i < 50; i++) if (rm.Handle(VoiceIntentParser.Parse(i % 2 == 0 ? "move left" : "move right"), 1f + i * 0.01f).Outcome == CommandOutcome.Executed) movedOk++;
            Check(movedOk == 50 && rm.HasSelection && rm.SelectedName == "red cube", "rapid: 50 consecutive moves all accepted, selection unchanged");
            CommandResult s2 = rm.Handle(VoiceIntentParser.Parse("select that"), 2f);
            Check(s2.Outcome == CommandOutcome.Rejected && s2.Reason == "already_selected" && rm.SelectedName == "red cube", "rapid: a repeated select is rejected, not applied twice");
            Check(rm.Handle(VoiceIntentParser.Parse("drop it"), 2.1f).Outcome == CommandOutcome.Executed && !rm.HasSelection, "rapid: release executes once");
            CommandResult r2 = rm.Handle(VoiceIntentParser.Parse("drop it"), 2.11f);
            Check(r2.Outcome == CommandOutcome.Rejected && r2.Reason == "nothing_selected", "rapid: a second release is rejected (nothing to release)");
            Check(rm.Handle(VoiceIntentParser.Parse("cancel"), 2.2f).Outcome == CommandOutcome.Rejected && rm.Handle(VoiceIntentParser.Parse("move left"), 2.3f).Outcome == CommandOutcome.Rejected,
                  "rapid: cancel and move after release are rejected, not silently accepted");

            // state-machine invariants over 3000 pseudo-random steps (fixed seed, so a failure is reproducible)
            string[] phrases = { "select that", "select the red cube", "select the blue sphere", "move left", "move right", "move up", "move down", "move forward", "move back", "move there",
                                 "release", "drop it", "let go", "cancel", "never mind", "reset", "start over", "what is this", "help", "", "gibberish words", "do not select that", "select and release" };
            string[] names = { "red cube", "blue sphere", "green capsule" };
            var fz = new GazeInteractionModel { MinFocusSeconds = 0.3f };
            uint seed = 12345u; Func<int, int> rnd = n => { seed = seed * 1664525u + 1013904223u; return (int)((seed >> 8) % (uint)n); };
            float now = 0f; bool invariantsHold = true; string firstViolation = null; int steps = 0;
            for (int i = 0; i < 3000 && invariantsHold; i++, steps++)
            {
                now += 0.05f * (1 + rnd(8));
                int fr = rnd(5);
                if (fr == 4) fz.SetFocus(false, 0, null, now); else fz.SetFocus(true, fr % 3 + 1, names[fr % 3], now);
                if (rnd(10) == 0) fz.SetMovedCount(rnd(4));
                string ph = phrases[rnd(phrases.Length)];
                ParsedIntent pi = VoiceIntentParser.Parse(ph);
                bool had = fz.HasSelection; string hadName = fz.SelectedName; bool hadFocus = fz.HasFocus;
                CommandResult cr;
                try { cr = fz.Handle(pi, now); }
                catch (Exception ex) { invariantsHold = false; firstViolation = "exception on '" + ph + "': " + ex.Message; break; }
                if (fz.HasSelection != (fz.SelectedName != null)) { invariantsHold = false; firstViolation = "HasSelection and SelectedName disagree after '" + ph + "'"; }
                else if (fz.MovedCount < 0) { invariantsHold = false; firstViolation = "MovedCount negative after '" + ph + "'"; }
                else if (!pi.IsActionable && cr.Outcome != CommandOutcome.Rejected) { invariantsHold = false; firstViolation = "non-actionable '" + ph + "' was not rejected"; }
                else if (pi.IsActionable && !had && (pi.Kind == IntentKind.Move || pi.Kind == IntentKind.Release || pi.Kind == IntentKind.Cancel) && cr.Outcome != CommandOutcome.Rejected) { invariantsHold = false; firstViolation = pi.Kind + " with nothing selected was not rejected ('" + ph + "')"; }
                else if (pi.IsActionable && had && pi.Kind == IntentKind.Select && !(cr.Outcome == CommandOutcome.Rejected && cr.Reason == "already_selected")) { invariantsHold = false; firstViolation = "select while selected was not already_selected ('" + ph + "')"; }
                else if (pi.IsActionable && pi.Kind == IntentKind.Select && !had && cr.Outcome == CommandOutcome.Executed && (!fz.HasSelection || !hadFocus)) { invariantsHold = false; firstViolation = "select executed without focus/selection ('" + ph + "')"; }
                else if (cr.Outcome == CommandOutcome.Executed && (cr.Action == CommandAction.Released || cr.Action == CommandAction.ClearedSelection) && fz.HasSelection) { invariantsHold = false; firstViolation = "release/clear executed but a selection remains ('" + ph + "')"; }
                else if (cr.Outcome == CommandOutcome.Rejected && cr.Message.Length == 0) { invariantsHold = false; firstViolation = "a rejection carried no message for the HUD ('" + ph + "')"; }
            }
            if (!invariantsHold) Console.WriteLine("  invariant violated after " + steps + " steps: " + firstViolation);
            Check(invariantsHold, "3000 random command/focus steps keep every state-machine invariant (no exception, no false success, no lost rejection reason)");
        }

        Console.WriteLine();
        Console.WriteLine("Passed: " + passed + "  Failed: " + failed);
        return failed == 0 ? 0 : 1;
    }
}
