using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace Group05.VoiceVR
{
    public enum TrialCondition { Controller, GazeVoice }

    /// <summary>
    /// Phase 05: runs a Fitts-style target acquisition session (Phase 04 protocol, section 3, PROPOSED) and writes one validated row per trial.
    ///
    /// What happens: practice trials (one per distance x width combination), then measured trials (3 blocks of the 9 combinations in random order).
    /// Each trial shows a bright GOAL sphere (width W) exactly D to the side of a dim PREVIOUS marker where the participant is already looking.
    /// The participant selects the goal with the controller trigger (condition Controller) or by looking at it and saying "select that" (condition GazeVoice).
    /// The existing TrialRecord / TrialCsvWriter schema is used unchanged; practice trials go to their own file and are never analysed.
    ///
    /// Data integrity (protocol section 9):
    ///  * the default data origin is Selftest (the developers' own runs); nothing recorded here is participant data;
    ///  * data origin Real is REFUSED unless <see cref="realDataEnabled"/> is ticked, which must only happen after the professor has approved the protocol and
    ///    the ethics route is settled, and then needs a pseudonym such as P01 (never a name);
    ///  * every session writes a sidecar file stating the Fitts formula used (WORKING DECISION: Shannon, professor confirmation pending), the seed and the settings.
    /// Keys (desktop, when not typing): F5 start a session, F6 abort, F7 switch condition (only while idle).
    /// Nothing here executes a voice command; it only watches the results of the existing gaze/voice system.
    /// </summary>
    [DisallowMultipleComponent]
    public class LiveTrialManager : MonoBehaviour
    {
        [Header("Session")]
        public TrialCondition condition = TrialCondition.GazeVoice;
        [Tooltip("Selftest = the developers' own runs (default). Real is refused unless 'Real Data Enabled' is ticked.")]
        public DataOrigin dataOrigin = DataOrigin.Selftest;
        [Tooltip("Only for Real data: the pseudonym issued after consent, for example P01. Never a name, roll number or email.")]
        public string participantCode = "";
        [Tooltip("Leave OFF until the professor has approved the protocol and the ethics route is settled (decisions D-05, Q-07). While off, Real data cannot be recorded.")]
        public bool realDataEnabled = false;

        [Header("Task (PROPOSED values, UNRESOLVED until the pilot, decision Q-03)")]
        public float[] distancesM = { 0.5f, 1.0f, 2.0f };
        public float[] widthsM = { 0.45f, 0.32f, 0.22f };
        public int repetitions = 3;
        [Tooltip("PROVISIONAL: a trial with no accepted selection after this many seconds is a timeout failure.")]
        public float timeLimitSeconds = 15f;
        public float interTrialDelaySeconds = 0.8f;
        public float practiceToMeasuredDelaySeconds = 5f;
        [Tooltip("Distance in metres from the participant to the plane the targets appear on.")]
        public float targetDepthMeters = 2.5f;
        [Tooltip("0 = pick a seed from the clock. The seed used is always written to the session file so a trial order can be reproduced.")]
        public int seed = 0;

        [Header("Keys and display")]
        public KeyCode startKey = KeyCode.F5;
        public KeyCode abortKey = KeyCode.F6;
        public KeyCode conditionKey = KeyCode.F7;
        public bool showHud = true;

        public static LiveTrialManager Instance { get; private set; }

        enum Phase { Idle, Delay, Active, Finished }

        Phase phase = Phase.Idle;
        readonly TrialEngine engine = new TrialEngine();
        List<TrialSpec> plan = new List<TrialSpec>();
        int cursor;
        float nextActionAt;
        string sessionId, practiceSessionId, folder;
        uint usedSeed;
        string pseudonym;
        TrialCsvWriter measuredWriter, practiceWriter;
        DateTime trialStartUtc, sessionStartUtc;
        Vector3 anchorPos, anchorRight;
        GameObject goalGo, previousGo, homeMarker;
        string message = "Press F5 to start a trial session.";
        float messageUntil;
        string lastResultText = "";
        readonly List<TrialResult> measuredResults = new List<TrialResult>();
        int rowsFailed;
        GazeFocusSystem gaze;
        DesktopVoiceInput voice;
        readonly bool[] triggerWas = new bool[2];
        bool eventHooked;

        public bool IsRunning { get { return phase == Phase.Delay || phase == Phase.Active; } }

        void OnEnable() { Instance = this; }

        void OnDisable()
        {
            if (Instance == this) Instance = null;
            Unhook();
            DestroyMarkers();
        }

        void Update()
        {
            float now = Time.realtimeSinceStartup;
            if (gaze == null) gaze = FindFirstObjectByType<GazeFocusSystem>();
            if (voice == null) voice = FindFirstObjectByType<DesktopVoiceInput>();

            HandleKeys();
            if (phase == Phase.Delay)
            {
                if (now >= nextActionAt) SpawnTrial(now);
            }
            else if (phase == Phase.Active)
            {
                TrialResult? timeout = engine.Tick(now);
                if (timeout.HasValue) { OnResult(timeout.Value, DefaultModality(), "", float.NaN, now); return; }
                if (condition == TrialCondition.Controller) PollController(now);
            }
        }

        // ---------------- control ----------------

        void HandleKeys()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            if (DesktopVoiceInput.IsTypingText) return;
            if (Input.GetKeyDown(startKey) && !IsRunning) StartSession();
            else if (Input.GetKeyDown(abortKey) && IsRunning) AbortSession("experimenter_abort");
            else if (Input.GetKeyDown(conditionKey) && !IsRunning)
            {
                condition = condition == TrialCondition.Controller ? TrialCondition.GazeVoice : TrialCondition.Controller;
                Say("Condition: " + ConditionLabel() + ". Press F5 to start.", 5f);
            }
#endif
        }

        string ConditionLabel() { return condition == TrialCondition.Controller ? "controller" : "gaze + voice"; }
        string ConditionCode() { return condition == TrialCondition.Controller ? TrialRecord.ConditionController : TrialRecord.ConditionGazeVoice; }
        InputModality DefaultModality() { return condition == TrialCondition.Controller ? InputModality.Controller : InputModality.Voice; }

        public bool StartSession()
        {
            if (IsRunning) return false;
            if (dataOrigin == DataOrigin.Real)
            {
                if (!realDataEnabled) { Say("REFUSED: real data is disabled until the protocol is approved (tick 'Real Data Enabled' only then).", 8f); return false; }
                string p = (participantCode ?? "").Trim();
                if (!System.Text.RegularExpressions.Regex.IsMatch(p, "^P[0-9]{2,3}$")) { Say("REFUSED: real data needs a participant code like P01 (no names).", 8f); return false; }
                pseudonym = p;
            }
            else pseudonym = TrialRecordFactory.PseudonymFor(dataOrigin, null);

            if (gaze == null) gaze = FindFirstObjectByType<GazeFocusSystem>();
            if (voice == null) voice = FindFirstObjectByType<DesktopVoiceInput>();
            Camera cam = Camera.main;
            if (cam == null) { Say("No camera: cannot place the targets.", 6f); return false; }

            usedSeed = seed != 0 ? (uint)seed : FittsTrialPlanner.SeedFromTicks(DateTime.UtcNow.Ticks);
            try { plan = FittsTrialPlanner.Build(usedSeed, distancesM, widthsM, repetitions, true); }
            catch (Exception ex) { Say("Bad task settings: " + ex.Message, 8f); return false; }

            sessionStartUtc = DateTime.UtcNow;
            sessionId = TrialCsvWriter.NewSessionId(sessionStartUtc);
            practiceSessionId = sessionId + "-practice";
            folder = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "telemetry", dataOrigin == DataOrigin.Real ? "real" : "selftest"));
            measuredWriter = new TrialCsvWriter(folder, sessionId, dataOrigin);
            practiceWriter = new TrialCsvWriter(folder, practiceSessionId, dataOrigin);
            rowsFailed = 0;
            measuredResults.Clear();

            // Anchor: the plane the targets live on, straight ahead of the participant at eye height, captured once.
            Vector3 fwd = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up);
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            anchorRight = Vector3.Cross(Vector3.up, fwd);
            anchorPos = cam.transform.position + fwd * targetDepthMeters;

            // One condition at a time: in the controller condition the microphone is switched off, in the voice condition it is switched on.
            if (voice != null)
            {
                if (condition == TrialCondition.Controller) voice.StopListening();
                else if (voice.State == VoiceInputState.Idle) voice.StartListening();
            }
            Hook();
            WriteSessionInfo(false);

            cursor = 0;
            phase = Phase.Delay;
            nextActionAt = Time.realtimeSinceStartup + 2f;
            Say("Session " + sessionId + " (" + dataOrigin.ToString().ToLowerInvariant() + ", " + ConditionLabel() + "). Practice starts in 2 s.", 4f);
            UnityEngine.Debug.Log("[LiveTrialManager] session " + sessionId + " origin=" + dataOrigin + " condition=" + ConditionLabel() + " seed=" + usedSeed + " files in " + folder);
            return true;
        }

        public void AbortSession(string reason)
        {
            if (phase == Phase.Idle || phase == Phase.Finished) return;
            if (phase == Phase.Active)
            {
                TrialResult? r = engine.Abandon(Time.realtimeSinceStartup, reason);
                if (r.HasValue) Record(r.Value, DefaultModality(), "", float.NaN);
            }
            FinishSession("Session stopped (" + reason + ").");
        }

        void FinishSession(string headline)
        {
            phase = Phase.Finished;
            DestroyMarkers();
            Unhook();
            WriteSessionInfo(true);
            Say(headline + " " + Summary(), 20f);
            UnityEngine.Debug.Log("[LiveTrialManager] " + headline + " " + Summary());
        }

        string Summary()
        {
            int ok = 0, fail = 0; var times = new List<float>();
            foreach (TrialResult r in measuredResults)
            {
                if (r.Outcome == TrialOutcome.Success) { ok++; times.Add(r.MovementTimeS); } else fail++;
            }
            times.Sort();
            string med = times.Count == 0 ? "n/a" : (times.Count % 2 == 1 ? times[times.Count / 2] : 0.5f * (times[times.Count / 2 - 1] + times[times.Count / 2])).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " s";
            return "Measured: " + ok + " ok, " + fail + " failed, median time " + med + (rowsFailed > 0 ? ". WARNING: " + rowsFailed + " row(s) could not be written." : ".");
        }

        // ---------------- trial flow ----------------

        void SpawnTrial(float now)
        {
            if (cursor >= plan.Count) { FinishSession("All trials done."); return; }
            TrialSpec spec = plan[cursor];
            DestroyMarkers();

            float w = spec.WidthM;
            previousGo = MakeMarker("TrialPrevious", PositionFor(spec.PreviousX), w, new Color(0.45f, 0.5f, 0.55f), "previous");
            goalGo = MakeMarker("TrialGoal", PositionFor(spec.TargetX), w, new Color(1f, 0.85f, 0.1f), "goal");

            trialStartUtc = DateTime.UtcNow;
            engine.Start(spec, now, timeLimitSeconds);
            messageUntil = 0f;                         // the status line now shows the trial instruction, not an old notice
            phase = Phase.Active;
            Array.Clear(triggerWas, 0, triggerWas.Length);
        }

        Vector3 PositionFor(float x) { return anchorPos + anchorRight * x; }

        GameObject MakeMarker(string name, Vector3 pos, float width, Color color, string display)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.SetActive(false);                       // so SpatialTarget's Awake sees the final settings
            go.transform.position = pos;
            go.transform.localScale = Vector3.one * width;
            var r = go.GetComponent<Renderer>();
            Shader sh = Shader.Find("Unlit/Color");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            if (sh != null) r.material = new Material(sh) { color = color };
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var st = go.AddComponent<SpatialTarget>();
            st.displayName = display;
            st.showLabel = false;
            go.SetActive(true);
            return go;
        }

        void DestroyMarkers()
        {
            // Destroying (rather than deselecting) also clears the gaze system's focus/selection of these objects without logging a command.
            DestroyNow(goalGo);
            DestroyNow(previousGo);
            goalGo = null; previousGo = null;
        }

        void OnResult(TrialResult r, InputModality modality, string commandText, float latencyMs, float now)
        {
            Record(r, modality, commandText, latencyMs);
            messageUntil = 0f;
            string mt = r.MovementTimeS.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            lastResultText = (r.Spec.Practice ? "Practice " : "Trial ") + r.Spec.Index + ": "
                             + (r.Outcome == TrialOutcome.Success ? "hit in " + mt + " s" : "failed (" + r.FailureReason + ")");
            DestroyMarkers();
            cursor++;
            if (cursor >= plan.Count) { FinishSession("All trials done."); return; }
            bool switchingToMeasured = plan[cursor - 1].Practice && !plan[cursor].Practice;
            phase = Phase.Delay;
            nextActionAt = now + (switchingToMeasured ? practiceToMeasuredDelaySeconds : interTrialDelaySeconds);
            if (switchingToMeasured) Say("Practice finished. The measured trials start in " + practiceToMeasuredDelaySeconds.ToString("0") + " s.", practiceToMeasuredDelaySeconds);
        }

        // Destroy in Play Mode; DestroyImmediate in Edit Mode (where Destroy is not allowed), so the flow can also be exercised by an editor test.
        static void DestroyNow(GameObject go)
        {
            if (go == null) return;
            if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
        }

        void Record(TrialResult r, InputModality modality, string commandText, float latencyMs)
        {
            try
            {
                string id = r.Spec.Practice ? practiceSessionId : sessionId;
                TrialRecord rec = TrialRecordFactory.Create(r, id, dataOrigin, pseudonym, ConditionCode(), modality, trialStartUtc, DateTime.UtcNow, commandText, latencyMs);
                TrialCsvWriter w = r.Spec.Practice ? practiceWriter : measuredWriter;
                if (!w.TryWrite(rec))
                {
                    rowsFailed++;
                    UnityEngine.Debug.LogWarning("[LiveTrialManager] trial row not written: " + w.LastError);
                }
                if (!r.Spec.Practice) measuredResults.Add(r);
            }
            catch (Exception ex)
            {
                rowsFailed++;
                UnityEngine.Debug.LogWarning("[LiveTrialManager] trial row failed: " + ex.Message);
            }
        }

        // ---------------- selection sources ----------------

        void Hook()
        {
            if (eventHooked || gaze == null) return;
            gaze.CommandHandled += OnCommandHandled;
            eventHooked = true;
        }

        void Unhook()
        {
            if (!eventHooked) return;
            if (gaze != null) gaze.CommandHandled -= OnCommandHandled;
            eventHooked = false;
        }

        // Gaze + voice: every Select (or Grab) result from the existing command system is one selection attempt.
        void OnCommandHandled(ParsedIntent p, CommandResult r, InputModality modality)
        {
            if (phase != Phase.Active || condition != TrialCondition.GazeVoice) return;
            if (p.Kind != IntentKind.Select && p.Kind != IntentKind.Grab) return;
            if (dataOrigin == DataOrigin.Real && modality != InputModality.Voice) return;   // real data: spoken commands only
            float now = Time.realtimeSinceStartup;
            string text = ""; float latency = float.NaN;
            if (modality == InputModality.Voice && voice != null && voice.HasLastUtterance)
            {
                text = voice.LastUtterance.Text;
                latency = voice.LastUtterance.SpeechEndToEventMs;
            }
            else text = p.RawText ?? "";

            bool executed = r.Outcome == CommandOutcome.Executed && (r.Action == CommandAction.SelectedTarget || r.Action == CommandAction.GrabStarted);
            if (!executed)
            {
                engine.Select(false, false, "", now);     // a rejected attempt (no focus, dwell too short, ...) is an error; the trial continues
                return;
            }
            SpatialTarget sel = gaze != null ? gaze.SelectedTarget : null;
            bool onGoal = sel != null && goalGo != null && sel.gameObject == goalGo;
            string name = sel != null ? sel.DisplayName : "";
            TrialResult? res = engine.Select(true, onGoal, name, now);
            if (res.HasValue) OnResult(res.Value, modality, text, latency, now);
        }

        // Controller: a trigger press casts a ray from the controller (headset or simulator). Off-headset in Selftest runs only, a left mouse click stands in
        // for the controller so the task can be rehearsed at a desk; those rows say so in command_text and are never participant data.
        void PollController(float now)
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            Ray ray; bool pressed = false; string source = "controller";
            ray = new Ray(cam.transform.position, cam.transform.forward);

            if (XRSettings.isDeviceActive)
            {
                XRNode[] nodes = { XRNode.RightHand, XRNode.LeftHand };
                for (int i = 0; i < nodes.Length && !pressed; i++)
                {
                    InputDevice dev = InputDevices.GetDeviceAtXRNode(nodes[i]);
                    bool down = false;
                    if (dev.isValid)
                    {
                        // Any easy-to-reach controller input selects: trigger, grip, or the A/B/X/Y face buttons. Some runtimes (the simulator among them)
                        // report only the analogue values, not the buttons.
                        bool b; float analog;
                        if (dev.TryGetFeatureValue(CommonUsages.triggerButton, out b) && b) down = true;
                        else if (dev.TryGetFeatureValue(CommonUsages.gripButton, out b) && b) down = true;
                        else if (dev.TryGetFeatureValue(CommonUsages.primaryButton, out b) && b) down = true;
                        else if (dev.TryGetFeatureValue(CommonUsages.secondaryButton, out b) && b) down = true;
                        else if ((dev.TryGetFeatureValue(CommonUsages.trigger, out analog) && analog >= 0.55f) ||
                                 (dev.TryGetFeatureValue(CommonUsages.grip, out analog) && analog >= 0.55f)) down = true;
                    }
                    bool edge = down && !triggerWas[i];
                    triggerWas[i] = down;
                    if (!edge) continue;
                    Vector3 pos; Quaternion rot;
                    if (dev.TryGetFeatureValue(CommonUsages.devicePosition, out pos) && dev.TryGetFeatureValue(CommonUsages.deviceRotation, out rot))
                    {
                        Transform space = cam.transform.parent;
                        Vector3 wp = space != null ? space.TransformPoint(pos) : pos;
                        Quaternion wr = space != null ? space.rotation * rot : rot;
                        ray = new Ray(wp, wr * Vector3.forward);
                        pressed = true;
                    }
                }
            }
            // Meta XR Core SDK path (the one that sees the simulator's and the headset's controllers); only reached if the generic XR path found nothing.
            if (!pressed)
            {
                Ray ovrRay;
                if (OvrControllerReader.TryGetPress(cam, out ovrRay)) { ray = ovrRay; pressed = true; }
            }
#if ENABLE_LEGACY_INPUT_MANAGER
            // Selftest only: Space or T (aimed along the head/camera direction) or a left mouse click also select, so the task can be done with no controller at all.
            if (!pressed && dataOrigin != DataOrigin.Real && (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.T)))
            {
                pressed = true;
                source = "keyboard_surrogate";
            }
            else if (!pressed && dataOrigin != DataOrigin.Real && Input.GetMouseButtonDown(0))
            {
                if (!XRSettings.isDeviceActive) ray = cam.ScreenPointToRay(Input.mousePosition);   // with a headset/simulator the camera is the headset: aim along it
                pressed = true;
                source = "mouse_surrogate";
            }
#endif
            if (!pressed) return;

            RaycastHit hit;
            bool any = Physics.Raycast(ray, out hit, 30f, ~0, QueryTriggerInteraction.Ignore);
            GameObject hitGo = any ? hit.collider.gameObject : null;
            // Selftest only: aiming a controller ray at a small sphere is hard (especially in the simulator), so if the controller ray hits neither target, the head
            // direction ("look at it and press") is tried too, and the row says so in command_text.
            if (dataOrigin != DataOrigin.Real && source == "controller" && (hitGo == null || (hitGo != goalGo && hitGo != previousGo)))
            {
                RaycastHit headHit;
                if (Physics.Raycast(new Ray(cam.transform.position, cam.transform.forward), out headHit, 30f, ~0, QueryTriggerInteraction.Ignore)
                    && (headHit.collider.gameObject == goalGo || headHit.collider.gameObject == previousGo))
                {
                    hitGo = headHit.collider.gameObject;
                    source = "controller_head_ray";
                }
            }
            bool hitTarget = hitGo != null && (hitGo == goalGo || hitGo == previousGo);
            bool onGoal = hitGo != null && hitGo == goalGo;
            TrialResult? res = engine.Select(hitTarget, onGoal, hitTarget ? (onGoal ? "goal" : "previous") : "", now);
            if (res.HasValue) OnResult(res.Value, InputModality.Controller, source, float.NaN, now);
        }

        // ---------------- files, status, HUD ----------------

        void WriteSessionInfo(bool finished)
        {
            try
            {
                System.IO.Directory.CreateDirectory(folder);
                string route = voice != null ? voice.recognizerRoute.ToString() : "none";
                string text = TrialSessionInfo.Build(sessionId, dataOrigin, pseudonym, ConditionCode(), usedSeed, timeLimitSeconds, repetitions, distancesM, widthsM, route,
                                                     "draft v0.4, NOT approved by the professor; no participant data unless data_origin is real", sessionStartUtc);
                if (finished) text += "finished_utc: " + DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture) + "\n" + "summary: " + Summary() + "\n";
                System.IO.File.WriteAllText(System.IO.Path.Combine(folder, "session_info_" + sessionId + ".txt"), text, new System.Text.UTF8Encoding(false));
            }
            catch (Exception ex) { UnityEngine.Debug.LogWarning("[LiveTrialManager] session info not written: " + ex.Message); }
        }

        void Say(string text, float seconds) { message = text; messageUntil = Time.realtimeSinceStartup + seconds; }

        /// <summary>One line for the headset panel and the desktop HUD.</summary>
        public string StatusLine()
        {
            if (phase == Phase.Idle) return Time.realtimeSinceStartup < messageUntil ? message : "Trials idle (F5 starts a session, F7 switches condition: " + ConditionLabel() + ")";
            if (phase == Phase.Finished) return message;
            if (phase == Phase.Delay && Time.realtimeSinceStartup < messageUntil) return message;
            if (cursor >= plan.Count) return "Finishing...";
            TrialSpec s = plan[Math.Min(cursor, plan.Count - 1)];
            int total = 0; foreach (TrialSpec t in plan) if (t.Practice == s.Practice) total++;
            string head = (s.Practice ? "PRACTICE " : "TRIAL ") + s.Index + "/" + total;
            string task = condition == TrialCondition.Controller ? "point at the BRIGHT sphere and press the trigger" : "look at the BRIGHT sphere and say \"select that\"";
            string tail = lastResultText.Length > 0 ? "   (last: " + lastResultText + ")" : "";
            return head + ": " + (phase == Phase.Delay ? "get ready..." : task) + tail;
        }

        void OnGUI()
        {
            if (!showHud) return;
            Vector2 v = HudTheme.Begin();
            float w = Mathf.Min(560f, v.x - 2f * HudTheme.EdgeMargin);
            var r = new Rect((v.x - w) * 0.5f, HudTheme.ResultTop + 60f, w, 54f);
            bool selftest = dataOrigin != DataOrigin.Real;
            HudTheme.Panel(r, HudTheme.PanelFill, IsRunning ? HudTheme.Warn : HudTheme.PanelBorder);
            HudTheme.Label(new Rect(r.x + 14f, r.y + 6f, w - 28f, 22f),
                           "TRIALS  " + ConditionLabel() + (selftest ? "  [" + dataOrigin.ToString().ToUpperInvariant() + " - not participant data]" : "  [REAL]"),
                           HudTheme.FontLead, selftest ? HudTheme.Info : HudTheme.Success, true, TextAnchor.MiddleLeft);
            HudTheme.Label(new Rect(r.x + 14f, r.y + 28f, w - 28f, 22f), HudTheme.Fit(StatusLine(), Mathf.Max(20, Mathf.FloorToInt((w - 28f) / 7.2f))),
                           HudTheme.FontBody, HudTheme.Text, false, TextAnchor.MiddleLeft);
            HudTheme.End();
        }
    }
}
