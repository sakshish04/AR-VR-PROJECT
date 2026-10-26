using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Group05.VoiceVR
{
    /// <summary>
    /// Gaze loop and spatial command executor. Casts a ray from the viewing camera's forward direction, accepts ONLY objects
    /// that carry a <see cref="SpatialTarget"/> as valid targets (the floor and other colliders are never targets), shows a
    /// focus cue, and feeds parsed utterances into <see cref="GazeInteractionModel"/>.
    ///
    /// Focus never executes anything. A target is selected only by an explicit, actionable SELECT utterance after the gaze
    /// has rested on it (see the model). Once selected, the SELECTED target (and only it) can be manipulated:
    ///   MOVE    "move left/right/up/down/forward/back" steps it <see cref="moveStepMeters"/> relative to the camera's
    ///           horizontal facing; "move there / here / to gaze" places it on the surface the gaze ray hits (never onto
    ///           another target). Positions are clamped to a play area around <see cref="arenaCenter"/>.
    ///   RELEASE keeps it where it is and deselects it.
    ///   CANCEL  deselects it and returns it to where it was when it was selected (<see cref="revertOnCancel"/>).
    ///   RESET   deselects and returns every moved target to its start position.
    /// There is no collision avoidance between targets, no rotation, no snapping, and no XR controller handling.
    ///
    /// Desktop Play Mode HUD only (IMGUI overlay); it is NOT visible in a headset. The focus cue is tint + ring + label +
    /// reticle and is NOT a validated accessibility feature. Utterances may come from real speech or from SIMULATED
    /// keyboard/text input; the modality is always shown and recorded. No audio is recorded and no participant is identified.
    /// </summary>
    [DisallowMultipleComponent]
    public class GazeFocusSystem : MonoBehaviour
    {
        [Header("Gaze ray")]
        [Tooltip("Viewing camera. If empty, Camera.main is used. If neither exists, focus stays empty and a warning is logged once.")]
        public Camera gazeCamera;
        public float maxDistance = 10f;
        public LayerMask layerMask = ~0;

        [Header("Confirmation")]
        [Tooltip("Seconds the gaze must rest on a target before SELECT is accepted. PROVISIONAL.")]
        public float minFocusSeconds = 0.3f;

        [Header("Input source (optional)")]
        public DesktopVoiceInput voiceInput;

        [Header("Moving a selected target")]
        [Tooltip("Step for 'move left/right/up/down/forward/back'. PROVISIONAL.")]
        public float moveStepMeters = 0.5f;
        [Tooltip("Seconds of smooth glide per move. 0 = jump instantly.")]
        public float moveGlideSeconds = 0.3f;
        [Tooltip("Centre of the allowed play area; moves are clamped to it. The bottom of the play area is this point's Y (the floor).")]
        public Vector3 arenaCenter = Vector3.zero;
        public float arenaHalfExtent = 7f;
        [Tooltip("Highest allowed target-bottom height above the arena floor.")]
        public float maxHeightAboveFloor = 3f;
        [Tooltip("CANCEL also returns the selected target to where it was when selected (undoes this manipulation).")]
        public bool revertOnCancel = true;

        [Header("Carrying ('grab this object' ... 'drop this object')")]
        [Tooltip("How quickly a carried target follows you (higher = tighter). 0 = no smoothing.")]
        public float carrySmoothing = 14f;
        [Tooltip("On grab, bring the object into your hands: in front of you, slightly below eye level (see Carry Hold Offset). Off = it keeps the place it had when grabbed.")]
        public bool carryInHands = true;
        [Tooltip("Where a carried object is held, in metres, in a frame that turns with your head left/right but not up/down: x right, y up from the eyes, z forward.")]
        public Vector3 carryHoldOffset = new Vector3(0.15f, -0.35f, 0.55f);

        [Header("Feedback")]
        [Tooltip("Optional small object placed at the gaze hit point. Its colliders must be removed.")]
        public Transform reticle;
        public Color reticleIdleColor = Color.white;
        public Color reticleFocusColor = new Color(1f, 0.92f, 0.1f);
        [Tooltip("Use the HUD theme's cyan for the reticle focus colour instead of 'Reticle Focus Color'.")]
        public bool useThemeColors = true;
        public bool showStatusPanel = true;

        [Header("Event log (separate proposed schema, never overwrites)")]
        public bool writeEventLog = true;
        [Tooltip("Real is refused by the event schema in this phase.")]
        public DataOrigin eventLogOrigin = DataOrigin.Selftest;
        [Tooltip("Leave empty for <project>/telemetry/selftest (git-ignored) in the Editor/desktop, persistentDataPath otherwise.")]
        public string eventLogFolderOverride = "";

        [Header("Optional bridge to the professor's controller")]
        public SpatialVoiceIntentController legacyController;
        [Tooltip("Writes only VALID focus into legacyController.focusedSpatialObject (after its own Update). The starter code itself is unchanged.")]
        public bool syncFocusToLegacyController = true;

        /// <summary>Raised after every command has been handled (result known, state updated). Observers only read; they must not call back into SubmitParsed.</summary>
        public event Action<ParsedIntent, CommandResult, InputModality> CommandHandled;

        public GazeInteractionModel Model { get; } = new GazeInteractionModel();
        public SpatialTarget FocusedTarget { get; private set; }
        public SpatialTarget SelectedTarget { get; private set; }
        public bool HasCamera { get; private set; }
        public CommandResult LastResult { get; private set; }
        public bool HasLastResult { get; private set; }
        public string LastCommandLabel { get; private set; } = "(none yet)";
        public string EventLogPath { get { return eventWriter != null ? eventWriter.FilePath : null; } }

        static readonly RaycastHit[] HitBuffer = new RaycastHit[16];
        static readonly RaycastHit[] MoveHits = new RaycastHit[16];

        bool initialized;
        bool warnedNoCamera;
        bool reticleVisible;
        int eventIndex;
        string sessionId;
        GazeEventCsvWriter eventWriter;
        bool eventLogFailed;
        Renderer reticleRenderer;
        MaterialPropertyBlock reticleBlock;
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        // targets this system has moved away from their start pose
        readonly List<SpatialTarget> moved = new List<SpatialTarget>();

        // GRAB: the selected target follows the user. The offset is kept in a yaw-only frame (turn your head and it swings with you,
        // look up or down and it does not tilt), so it stays where it was in front of you when you grabbed it.
        SpatialTarget carried;
        Vector3 carryOffsetLocal;

        // last command details for the HUD
        string lastRawText = "";
        InputModality lastModality;
        float lastParseScore;
        string lastConfidence = "n/a";
        float lastResultRealtime = -999f;

        // The camera the gaze ray (and a carried target) follows. An assigned camera that is DISABLED (the scene's old static "Main Camera" is, once the XR rig
        // is in use) must not win, or the ray would stay at the origin while the head moves; the active Camera.main (the headset) is used instead.
        Camera GazeCamera()
        {
            if (gazeCamera != null && gazeCamera.isActiveAndEnabled) return gazeCamera;
            Camera main = Camera.main;
            return main != null ? main : gazeCamera;
        }

        void Awake() { EnsureInitialized(); }

        /// <summary>Idempotent; safe to call from edit-mode tests where Awake does not run.</summary>
        public void EnsureInitialized()
        {
            if (initialized) return;
            initialized = true;
            sessionId = SessionCsvFile.NewSessionId(DateTime.UtcNow);
            if (reticle != null)
            {
                reticleRenderer = reticle.GetComponent<Renderer>();
                reticleBlock = new MaterialPropertyBlock();
            }
        }

        void OnEnable()
        {
            EnsureInitialized();
            if (voiceInput != null) voiceInput.UtteranceReceived += OnUtterance;
        }

        void OnDisable()
        {
            if (voiceInput != null) voiceInput.UtteranceReceived -= OnUtterance;
        }

        void Update()
        {
            Evaluate(Time.realtimeSinceStartup);
            UpdateCarry(Time.deltaTime);
        }

        void LateUpdate()
        {
            // Runs after every Update, including the starter controller's, so only valid focus survives.
            if (syncFocusToLegacyController && legacyController != null)
                legacyController.focusedSpatialObject = FocusedTarget != null ? FocusedTarget.gameObject : null;
        }

        /// <summary>One gaze step. Public so tests can drive it with a simulated clock.</summary>
        public void Evaluate(float now)
        {
            EnsureInitialized();
            CheckDestroyedTargets(now);

            Camera cam = GazeCamera();
            HasCamera = cam != null;
            if (cam == null)
            {
                if (!warnedNoCamera)
                {
                    warnedNoCamera = true;
                    UnityEngine.Debug.LogWarning("[GazeFocusSystem] No gaze camera assigned and no MainCamera found; gaze focus is disabled.");
                    LogEvent(now, GazeEventRecord.CameraMissing, null, null, null, null, null);
                }
                ApplyFocus(null, now);
                SetReticle(false, Vector3.zero, 0f, false);
                return;
            }
            warnedNoCamera = false;

            Transform t = cam.transform;
            var ray = new Ray(t.position, t.forward);
            int count = Physics.RaycastNonAlloc(ray, HitBuffer, Mathf.Max(0.01f, maxDistance), layerMask, QueryTriggerInteraction.Ignore);

            // The NEAREST collider decides: a wall in front of a target blocks it, and the floor/environment is never a target.
            int nearest = -1;
            float best = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (HitBuffer[i].collider == null) continue;
                // The target in your hands would sit in front of the view centre and block everything behind it.
                if (carried != null && HitBuffer[i].collider.GetComponentInParent<SpatialTarget>() == carried) continue;
                if (HitBuffer[i].distance < best) { best = HitBuffer[i].distance; nearest = i; }
            }

            SpatialTarget valid = null;
            if (nearest >= 0)
            {
                SpatialTarget candidate = HitBuffer[nearest].collider.GetComponentInParent<SpatialTarget>();
                if (candidate != null && candidate.isActiveAndEnabled) valid = candidate;
                SetReticle(true, HitBuffer[nearest].point, HitBuffer[nearest].distance, valid != null);
            }
            else
            {
                SetReticle(false, Vector3.zero, 0f, false);
            }
            ApplyFocus(valid, now);
        }

        void ApplyFocus(SpatialTarget target, float now)
        {
            Model.MinFocusSeconds = minFocusSeconds;
            SpatialTarget previous = FocusedTarget;
            bool changed = target != null
                ? Model.SetFocus(true, target.GetInstanceID(), target.DisplayName, now)
                : Model.SetFocus(false, 0, null, now);
            if (!changed) return;

            FocusedTarget = target;
            if (previous != null)
            {
                RefreshVisual(previous);
                LogEvent(now, GazeEventRecord.FocusExit, previous.DisplayName, null, null, null, null);
            }
            if (target != null)
            {
                target.EnsureInitialized();
                RefreshVisual(target);
                LogEvent(now, GazeEventRecord.FocusEnter, target.DisplayName, null, null, null, null);
            }
        }

        void CheckDestroyedTargets(float now)
        {
            // Unity's overloaded == detects destroyed objects; cached references would otherwise dangle silently.
            if (Model.HasFocus && FocusedTarget == null)
            {
                string name = Model.FocusedName;
                bool f, s;
                Model.TargetLost(Model.FocusedId, out f, out s);
                FocusedTarget = null;
                LogEvent(now, GazeEventRecord.TargetLost, name, null, null, "focus", "destroyed");
            }
            if (Model.HasSelection && SelectedTarget == null)
            {
                string name = Model.SelectedName;
                bool f, s;
                Model.TargetLost(Model.SelectedId, out f, out s);
                SelectedTarget = null;
                LogEvent(now, GazeEventRecord.TargetLost, name, null, null, "selection", "destroyed");
            }
            SyncMovedCount();
        }

        void RefreshVisual(SpatialTarget t)
        {
            if (t == null) return;
            t.EnsureInitialized();
            t.SetVisualState(t == SelectedTarget ? SpatialTarget.VisualState.Selected
                           : t == FocusedTarget ? SpatialTarget.VisualState.Focused
                           : SpatialTarget.VisualState.Normal);
        }

        string lastConfidenceForNextCommand;

        void OnUtterance(UtteranceEvent ev)
        {
            lastConfidenceForNextCommand = ev.RecognizerConfidence;
            SubmitParsed(ev.Parsed, ev.Modality, ev.Text, Time.realtimeSinceStartup);
        }

        /// <summary>
        /// Applies a parsed command. Public so fallback input and tests can use the same path as speech.
        /// Never throws for bad input; the outcome is returned and logged.
        /// </summary>
        public CommandResult SubmitParsed(ParsedIntent parsed, InputModality modality, string rawText, float now)
        {
            EnsureInitialized();
            CheckDestroyedTargets(now);
            Model.MinFocusSeconds = minFocusSeconds;

            CommandResult r = Model.Handle(parsed, now);
            if (r.Action == CommandAction.SelectedTarget)
            {
                SpatialTarget previous = SelectedTarget;
                SelectedTarget = FocusedTarget; // the model accepted only because a valid focused target exists
                if (SelectedTarget != null) SelectedTarget.BeginManipulation();
                RefreshVisual(previous);
                RefreshVisual(SelectedTarget);
            }
            else if (r.Action == CommandAction.GrabStarted)
            {
                SpatialTarget previous = SelectedTarget;
                if (SelectedTarget == null)
                {
                    SelectedTarget = FocusedTarget; // a fresh grab: the model accepted only because a valid focused target exists
                    if (SelectedTarget != null) SelectedTarget.BeginManipulation();
                }
                Camera grabCam = GazeCamera();
                if (SelectedTarget != null && grabCam != null) BeginCarry(SelectedTarget, grabCam);
                else
                {
                    Model.StopCarrying();
                    r = new CommandResult(CommandOutcome.Rejected, CommandAction.None, grabCam == null ? "no_camera" : "no_target",
                        grabCam == null ? "No camera, so the object cannot follow you." : "No target to grab.", parsed.Kind, true, r.TargetId, r.TargetName);
                }
                RefreshVisual(previous);
                RefreshVisual(SelectedTarget);
            }
            else if (r.Action == CommandAction.ClearedSelection)
            {
                carried = null;
                SpatialTarget previous = SelectedTarget;
                SelectedTarget = null;
                // CANCEL (not RESET) also undoes the moves made since the target was selected.
                if (r.Outcome == CommandOutcome.Cancelled && revertOnCancel && previous != null)
                    previous.RestoreSelectionPose(moveGlideSeconds);
                RefreshVisual(previous);
                SyncMovedCount();
            }
            else if (r.Action == CommandAction.Released)
            {
                carried = null;
                SpatialTarget previous = SelectedTarget;
                SelectedTarget = null;
                RefreshVisual(previous);
                SyncMovedCount();
            }
            else if (r.Action == CommandAction.RestoredInitialState)
            {
                carried = null;
                SpatialTarget previous = SelectedTarget;
                SelectedTarget = null;
                for (int i = 0; i < moved.Count; i++)
                    if (moved[i] != null) moved[i].ResetToInitialPose(moveGlideSeconds);
                moved.Clear();
                RefreshVisual(previous);
                Model.SetMovedCount(0);
            }
            else if (r.Action == CommandAction.MoveRequested)
            {
                r = ExecuteMove(parsed, r);
            }

            if (r.Outcome == CommandOutcome.Rejected)
            {
                SpatialTarget f = SelectedTarget != null ? SelectedTarget : FocusedTarget;
                if (f != null) f.FlashRejected();
            }

            LastResult = r;
            HasLastResult = true;
            lastRawText = rawText ?? "";
            lastModality = modality;
            lastParseScore = parsed.ParseScore;
            lastConfidence = lastConfidenceForNextCommand ?? "n/a";
            lastConfidenceForNextCommand = null;
            lastResultRealtime = Time.realtimeSinceStartup;

            string tag = modality == InputModality.Voice ? "VOICE" : "SIMULATED " + modality + " (not speech)";
            LastCommandLabel = "[" + tag + "] '" + lastRawText + "' -> " + r.Outcome + (r.Reason.Length > 0 ? " (" + r.Reason + ")" : "") + ": " + r.Message;
            if (modality == InputModality.Voice && r.Reason == "no_focus")
                LastCommandLabel += " Speech results arrive about a second after you stop talking; keep looking at the target until the result appears.";
            UnityEngine.Debug.Log("[GazeFocusSystem] " + LastCommandLabel);
            LogEvent(now, GazeEventRecord.Command, r.TargetName ?? Model.FocusedName, modality, parsed.Kind.ToString(), r.Outcome.ToString().ToLowerInvariant(), r.Reason);
            // Tell the voice telemetry row what this command did (synchronous; DesktopVoiceInput only uses it for recognizer events).
            if (voiceInput != null) voiceInput.ReportActionOutcome(VoiceTelemetryRecord.OutcomeCode(r.Outcome, r.Reason));
            Action<ParsedIntent, CommandResult, InputModality> observers = CommandHandled;
            if (observers != null)
            {
                foreach (Delegate d in observers.GetInvocationList())
                {
                    try { ((Action<ParsedIntent, CommandResult, InputModality>)d)(parsed, r, modality); }
                    catch (Exception ex) { UnityEngine.Debug.LogError("[GazeFocusSystem] CommandHandled observer threw: " + ex); }
                }
            }
            return r;
        }

        // ---- GRAB / CARRY ----

        void BeginCarry(SpatialTarget t, Camera cam)
        {
            Quaternion yaw = Quaternion.Euler(0f, cam.transform.eulerAngles.y, 0f);
            carryOffsetLocal = carryInHands ? carryHoldOffset : Quaternion.Inverse(yaw) * (t.ResolvedPosition - cam.transform.position);
            carried = t;
        }

        void UpdateCarry(float dt)
        {
            if (carried == null) return;
            if (!Model.IsCarrying || SelectedTarget != carried) { carried = null; return; }
            Camera cam = GazeCamera();
            if (cam == null) return;
            Quaternion yaw = Quaternion.Euler(0f, cam.transform.eulerAngles.y, 0f);
            Vector3 desired = ClampToArena(carried, cam.transform.position + yaw * carryOffsetLocal);
            Vector3 current = carried.transform.position;
            float k = carrySmoothing <= 0f ? 1f : 1f - Mathf.Exp(-carrySmoothing * Mathf.Max(0f, dt));
            carried.GlideTo(Vector3.Lerp(current, desired, k), 0f);
            if (!moved.Contains(carried) && carried.HasMovedFromInitial)
            {
                moved.Add(carried);                       // so RESET and the moved counter know about a carried target
                Model.SetMovedCount(moved.Count);
            }
        }

        // The same play-area limits a spoken move obeys: inside the arena, bottom never below the floor, never above the height limit.
        Vector3 ClampToArena(SpatialTarget t, Vector3 dest)
        {
            Bounds b = t.GetBounds();
            Vector3 ext = b.extents;
            Vector3 pivotOffset = t.transform.position - b.center;
            float minY = arenaCenter.y + ext.y + pivotOffset.y;
            float maxY = Mathf.Max(minY, arenaCenter.y + maxHeightAboveFloor + ext.y + pivotOffset.y);
            dest.x = Mathf.Clamp(dest.x, arenaCenter.x - arenaHalfExtent, arenaCenter.x + arenaHalfExtent);
            dest.z = Mathf.Clamp(dest.z, arenaCenter.z - arenaHalfExtent, arenaCenter.z + arenaHalfExtent);
            dest.y = Mathf.Clamp(dest.y, minY, maxY);
            return dest;
        }

        // ---- MOVE ----

        CommandResult ExecuteMove(ParsedIntent p, CommandResult accepted)
        {
            SpatialTarget t = SelectedTarget;
            Camera cam = GazeCamera();
            if (t == null) return RejectMove(p, accepted, "nothing_selected", "Nothing is selected.");
            if (cam == null) return RejectMove(p, accepted, "no_camera", "No camera, so there is no direction or gaze to move by.");

            Vector3 dest;
            string note, reason, message;
            if (!TryComputeMoveDestination(t, cam, p, out dest, out note, out reason, out message))
                return RejectMove(p, accepted, reason, message);

            t.GlideTo(dest, moveGlideSeconds);
            if (!moved.Contains(t)) moved.Add(t);
            SyncMovedCount();
            return new CommandResult(CommandOutcome.Executed, CommandAction.MoveRequested, "",
                "Moved " + t.DisplayName + " " + note, p.Kind, true, accepted.TargetId, accepted.TargetName);
        }

        CommandResult RejectMove(ParsedIntent p, CommandResult accepted, string reason, string message)
        {
            return new CommandResult(CommandOutcome.Rejected, CommandAction.None, reason, message, p.Kind, true, accepted.TargetId, accepted.TargetName);
        }

        bool TryComputeMoveDestination(SpatialTarget t, Camera cam, ParsedIntent p, out Vector3 dest, out string note, out string reason, out string message)
        {
            Vector3 cur = t.ResolvedPosition;
            Bounds b = t.GetBounds();
            Vector3 ext = b.extents;
            Vector3 pivotOffset = t.transform.position - b.center;   // pivot relative to the visual centre
            dest = cur;
            note = "";
            reason = "";
            message = "";
            float requestedStep = -1f;

            if (p.Direction != null)
            {
                Vector3 fwd = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up);
                if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.ProjectOnPlane(cam.transform.up, Vector3.up);   // looking straight up/down
                if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
                fwd.Normalize();
                Vector3 right = Vector3.Cross(Vector3.up, fwd);
                Vector3 dir;
                switch (p.Direction)
                {
                    case "left": dir = -right; break;
                    case "right": dir = right; break;
                    case "up": dir = Vector3.up; break;
                    case "down": dir = Vector3.down; break;
                    case "forward":
                    case "away": dir = fwd; break;
                    case "back":
                    case "backward":
                    case "closer": dir = -fwd; break;
                    default:
                        reason = "unknown_direction";
                        message = "'" + p.Direction + "' is not a supported direction.";
                        return false;
                }
                // A spoken distance ("move left thirty centimeters") wins; otherwise the default step (moveStepMeters, 0.5 m).
                float step = p.HasDistance ? p.DistanceMeters : Mathf.Max(0.05f, moveStepMeters);
                requestedStep = p.HasDistance ? p.DistanceMeters : -1f;
                dest = cur + dir * step;
                note = p.Direction + " by " + step.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " m";
            }
            else if (p.DestinationName == null || p.DestinationName == "gaze")
            {
                var ray = new Ray(cam.transform.position, cam.transform.forward);
                int n = Physics.RaycastNonAlloc(ray, MoveHits, Mathf.Max(0.01f, maxDistance), layerMask, QueryTriggerInteraction.Ignore);
                int nearest = -1;
                float best = float.MaxValue;
                for (int i = 0; i < n; i++)
                {
                    Collider c = MoveHits[i].collider;
                    if (c == null || c.GetComponentInParent<SpatialTarget>() == t) continue;   // ignore the target being moved
                    if (MoveHits[i].distance < best) { best = MoveHits[i].distance; nearest = i; }
                }
                if (nearest < 0)
                {
                    reason = "no_gaze_point";
                    message = "Look at a surface (the floor, for example) to say where, then try again.";
                    return false;
                }
                RaycastHit hit = MoveHits[nearest];
                if (hit.collider.GetComponentInParent<SpatialTarget>() != null)
                {
                    reason = "gaze_on_other_target";
                    message = "You are looking at another target. Look at a free surface to say where.";
                    return false;
                }
                // Rest the target against the surface: push its centre out along the surface normal by its own half-size.
                Vector3 nrm = hit.normal;
                float push = Mathf.Abs(nrm.x) * ext.x + Mathf.Abs(nrm.y) * ext.y + Mathf.Abs(nrm.z) * ext.z;
                dest = hit.point + nrm * (push + 0.01f) + pivotOffset;
                note = "to where you are looking";
            }
            else
            {
                reason = "unknown_destination";
                message = "Only 'there', 'here', 'to gaze' or a direction (left, right, up, down, forward, back) can be a destination.";
                return false;
            }

            // Clamp to the play area: the target's bottom never goes below the arena floor.
            float minY = arenaCenter.y + ext.y + pivotOffset.y;
            float maxY = Mathf.Max(minY, arenaCenter.y + maxHeightAboveFloor + ext.y + pivotOffset.y);
            dest.x = Mathf.Clamp(dest.x, arenaCenter.x - arenaHalfExtent, arenaCenter.x + arenaHalfExtent);
            dest.z = Mathf.Clamp(dest.z, arenaCenter.z - arenaHalfExtent, arenaCenter.z + arenaHalfExtent);
            dest.y = Mathf.Clamp(dest.y, minY, maxY);
            float minMove = requestedStep > 0f ? Mathf.Min(0.01f, requestedStep * 0.5f) : 0.01f;
            float actual = (dest - cur).magnitude;
            if (actual < minMove)
            {
                reason = "at_limit";
                message = "Cannot move further that way: edge of the play area.";
                return false;
            }
            if (requestedStep > 0f && actual < requestedStep - 0.005f)
                note += " (limited by the play area: moved " + actual.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " m)";
            return true;
        }

        void SyncMovedCount()
        {
            for (int i = moved.Count - 1; i >= 0; i--)
                if (moved[i] == null || !moved[i].HasMovedFromInitial) moved.RemoveAt(i);
            Model.SetMovedCount(moved.Count);
        }

        void SetReticle(bool hit, Vector3 point, float distance, bool onValidTarget)
        {
            if (reticle == null) return;
            if (hit != reticleVisible)
            {
                reticleVisible = hit;
                reticle.gameObject.SetActive(hit);
            }
            if (!hit) return;
            reticle.position = point;
            reticle.localScale = Vector3.one * Mathf.Clamp(distance * 0.02f, 0.02f, 0.25f);
            if (reticleRenderer != null && reticleBlock != null)
            {
                Color c = onValidTarget ? (useThemeColors ? HudTheme.Focus : reticleFocusColor) : reticleIdleColor;
                reticleBlock.SetColor(ColorId, c);
                reticleBlock.SetColor(BaseColorId, c);
                reticleRenderer.SetPropertyBlock(reticleBlock);
            }
        }

        // ---- event log ----

        void LogEvent(float now, string type, string target, InputModality? modality, string intent, string outcome, string reason)
        {
            if (!writeEventLog || eventLogFailed) return;
            try
            {
                if (eventWriter == null)
                {
                    eventWriter = new GazeEventCsvWriter(ResolveLogFolder(), sessionId, eventLogOrigin);
                }
                var rec = new GazeEventRecord
                {
                    Origin = eventLogOrigin,
                    SessionId = sessionId,
                    EventIndex = ++eventIndex,
                    RealtimeS = now,
                    Utc = DateTime.UtcNow,
                    EventType = type,
                    TargetName = target,
                    Modality = modality,
                    Intent = intent,
                    Outcome = outcome,
                    Reason = reason
                };
                if (!eventWriter.TryWrite(rec))
                {
                    eventLogFailed = true; // log once, then stop trying; interaction must keep working
                    UnityEngine.Debug.LogWarning("[GazeFocusSystem] Event log disabled: " + eventWriter.LastError);
                }
            }
            catch (Exception ex)
            {
                eventLogFailed = true;
                UnityEngine.Debug.LogWarning("[GazeFocusSystem] Event log disabled: " + ex.Message);
            }
        }

        string ResolveLogFolder()
        {
            if (!string.IsNullOrEmpty(eventLogFolderOverride)) return eventLogFolderOverride;
#if UNITY_EDITOR || UNITY_STANDALONE
            return Path.Combine(Path.Combine(Application.dataPath, ".."), Path.Combine("telemetry", "selftest"));
#else
            return Path.Combine(Application.persistentDataPath, "telemetry_selftest");
#endif
        }

        // ---- HUD (desktop IMGUI overlay, fixed rectangles only; the top status bar is drawn by DesktopVoiceInput) ----

        void OnGUI()
        {
            if (!showStatusPanel) return;
            Vector2 v = HudTheme.Begin();
            DrawResultPanel(v);
            DrawBottomHud(v);
            HudTheme.End();
        }

        void DrawResultPanel(Vector2 v)
        {
            bool compact = HudTheme.IsCompact(v);
            float w = Mathf.Min(480f, v.x - 2f * HudTheme.EdgeMargin), h = HudTheme.ResultHeight(v);
            var r = new Rect(v.x - HudTheme.EdgeMargin - w, HudTheme.ResultTop, w, h);
            float age = Time.realtimeSinceStartup - lastResultRealtime;
            Color accent = HudTheme.TextDim;
            string head = "WAITING";
            string heard = "Say a command while looking at a target.";
            string message = "Try 'select that', or press 1 (simulated).";
            string detail = "";
            string modality = "";
            Color modalityColor = HudTheme.Cyan;

            if (HasLastResult)
            {
                switch (LastResult.Outcome)
                {
                    case CommandOutcome.Executed: accent = HudTheme.Success; head = "EXECUTED"; break;
                    case CommandOutcome.Rejected: accent = HudTheme.Reject; head = "REJECTED"; break;
                    case CommandOutcome.Cancelled: accent = HudTheme.Warn; head = "CANCELLED"; break;
                    default: accent = HudTheme.Info; head = "NOT IMPLEMENTED"; break;
                }
                heard = "Heard: '" + HudTheme.Fit(lastRawText, 38) + "'";
                message = LastResult.Message;
                if (lastModality == InputModality.Voice && LastResult.Reason == "no_focus")
                    message += " Speech arrives ~1 s after you stop talking: keep looking at the target.";
                modality = lastModality == InputModality.Voice ? "VOICE"
                         : lastModality == InputModality.KeyboardFallback ? "KEYS (SIMULATED)" : "TYPED (SIMULATED)";
                modalityColor = lastModality == InputModality.Voice ? HudTheme.Cyan : HudTheme.Warn;
                detail = (LastResult.Reason.Length > 0 ? LastResult.Reason + "  |  " : "")
                       + "parse " + lastParseScore.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
                       + "  |  recognizer " + HudTheme.Fit(lastConfidence, 12);
            }

            // Newest result is full strength, then settles slightly so it never competes with the scene (never below 0.85: stays readable).
            float fade = age < 0.5f ? 1f : Mathf.Lerp(1f, 0.85f, Mathf.Clamp01((age - 6f) / 6f));
            Color saved = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, fade);
            HudTheme.Panel(r);
            HudTheme.Brackets(r, accent, 10f, 2f);
            HudTheme.Rule(new Rect(r.x + 6f, r.y + 14f, 3f, r.height - 28f), accent);
            HudTheme.Chip(new Rect(r.x + 20f, r.y + 12f, head == "NOT IMPLEMENTED" ? 152f : 112f, 26f), head, accent, HudTheme.FontSmall);
            if (modality.Length > 0)
            {
                float mw = modality.Length > 6 ? 166f : 74f;
                HudTheme.Chip(new Rect(r.xMax - 14f - mw, r.y + 12f, mw, 26f), modality, modalityColor, HudTheme.FontSmall);
            }
            // Compact (small Game view): drop the "Heard" line, keep the result message and the reason line at full size.
            if (!compact) HudTheme.Label(new Rect(r.x + 20f, r.y + 44f, w - 36f, 20f), heard, HudTheme.FontBody, HudTheme.TextDim);
            float msgTop = compact ? 44f : 66f, msgH = compact ? 40f : 56f;
            int msgChars = Mathf.Max(24, Mathf.FloorToInt((w - 36f) / 8.2f) * (compact ? 2 : 3) - 6);   // 15 pt bold is about 8 px per character
            HudTheme.Label(new Rect(r.x + 20f, r.y + msgTop, w - 36f, msgH), HudTheme.Fit(message, msgChars), HudTheme.FontLead, HudTheme.Text, true, TextAnchor.UpperLeft, true);
            HudTheme.Label(new Rect(r.x + 20f, r.yMax - 24f, w - 36f, 18f), detail, HudTheme.FontSmall, HudTheme.TextDim);
            GUI.color = saved;
        }

        /// <summary>
        /// The "say ..." hint line. ONE source for the desktop HUD (IMGUI) and the headset panel (XrHudPanel), so they cannot disagree.
        /// Mentions "drop it": in the session logs the recognizer delivered "drop it" reliably and the one-word "release" never.
        /// </summary>
        public string CommandHints(bool compact) { return HudText.CommandHints(Model.HasSelection, compact, Model.IsCarrying); }

        void DrawBottomHud(Vector2 v)
        {
            bool compact = HudTheme.IsCompact(v);
            float h = HudTheme.BottomHeight(v);   // shared with the typed-text box, which sits just above this bar
            var r = new Rect(HudTheme.EdgeMargin, v.y - HudTheme.EdgeMargin - h, v.x - 2f * HudTheme.EdgeMargin, h);
            HudTheme.Panel(r);
            HudTheme.Brackets(r, HudTheme.Cyan, 12f, 2f);
            float colW = compact ? Mathf.Clamp((r.width - 40f) * 0.30f, 190f, 300f) : Mathf.Clamp((r.width - 40f) * 0.26f, 230f, 340f);
            float rowName = compact ? 26f : 32f, rowSub = compact ? 54f : 68f, rowBar = compact ? 62f : 76f;
            float x1 = r.x + 20f, x2 = x1 + colW + 14f, x3 = x2 + colW + 14f;
            float now = Time.realtimeSinceStartup;
            int title = HudTheme.FontTitle, small = HudTheme.FontSmall;

            // --- focus ---
            HudTheme.Label(new Rect(x1, r.y + 10f, colW, 16f), "GAZE FOCUS", small, HudTheme.TextDim, true);
            if (!HasCamera && warnedNoCamera)
            {
                HudTheme.Label(new Rect(x1, r.y + rowName, colW, 28f), "NO CAMERA - gaze disabled", HudTheme.FontLead, HudTheme.Reject, true, TextAnchor.MiddleLeft);
            }
            else if (Model.HasFocus)
            {
                HudTheme.Dot(new Vector2(x1 + 6f, r.y + rowName + 14f), 5f, HudTheme.Focus);
                HudTheme.Label(new Rect(x1 + 20f, r.y + rowName, colW - 20f, 28f), HudTheme.Fit(Model.FocusedName, 20), title, HudTheme.Focus, true, TextAnchor.MiddleLeft);
                float ready = Model.MinFocusSeconds <= 0f ? 1f : Model.FocusAge(now) / Model.MinFocusSeconds;
                HudTheme.Bar(new Rect(x1, r.y + rowBar, colW - 76f, 6f), ready, HudTheme.Focus);
                HudTheme.Label(new Rect(x1 + colW - 66f, r.y + rowSub, 66f, 20f), ready >= 1f ? "READY" : "HOLD...", small, ready >= 1f ? HudTheme.Success : HudTheme.TextDim, true, TextAnchor.MiddleLeft);
            }
            else
            {
                HudTheme.Dot(new Vector2(x1 + 6f, r.y + rowName + 14f), 5f, HudTheme.WithAlpha(HudTheme.TextDim, 0.7f));
                HudTheme.Label(new Rect(x1 + 20f, r.y + rowName, colW - 20f, 28f), "nothing", title, HudTheme.TextDim, false, TextAnchor.MiddleLeft);
                HudTheme.Label(new Rect(x1, r.y + rowSub, colW, 20f), "Look at a target to focus it", HudTheme.FontBody, HudTheme.TextDim);
            }

            // --- selection ---
            HudTheme.Rule(new Rect(x2 - 8f, r.y + 14f, 1f, h - 28f), HudTheme.WithAlpha(Color.white, 0.16f));
            HudTheme.Label(new Rect(x2, r.y + 10f, colW, 16f), "SELECTED", small, HudTheme.TextDim, true);
            if (Model.HasSelection)
            {
                HudTheme.Dot(new Vector2(x2 + 6f, r.y + rowName + 14f), 5f, HudTheme.Selected);
                HudTheme.Label(new Rect(x2 + 20f, r.y + rowName, colW - 20f, 28f), HudTheme.Fit(Model.SelectedName, 20), title, HudTheme.Selected, true, TextAnchor.MiddleLeft);
                HudTheme.Label(new Rect(x2, r.y + rowSub, colW, 20f), Model.IsCarrying ? "CARRYING - say 'drop this object'" : "move / grab / release / cancel", HudTheme.FontBody, Model.IsCarrying ? HudTheme.Selected : HudTheme.TextDim);
            }
            else
            {
                HudTheme.Dot(new Vector2(x2 + 6f, r.y + rowName + 14f), 5f, HudTheme.WithAlpha(HudTheme.TextDim, 0.7f));
                HudTheme.Label(new Rect(x2 + 20f, r.y + rowName, colW - 20f, 28f), "none", title, HudTheme.TextDim, false, TextAnchor.MiddleLeft);
                HudTheme.Label(new Rect(x2, r.y + rowSub, colW, 20f), Model.MovedCount > 0 ? Model.MovedCount + " moved - say 'reset'" : "say 'select that'", HudTheme.FontBody, HudTheme.TextDim);
            }

            // --- hints ---
            float w3 = r.xMax - 16f - x3;
            if (w3 < 240f) return;   // not enough room for readable hints: keep focus and selection, drop the hints column
            HudTheme.Rule(new Rect(x3 - 8f, r.y + 14f, 1f, h - 28f), HudTheme.WithAlpha(Color.white, 0.16f));
            HudTheme.Label(new Rect(x3, r.y + 10f, w3, 16f), "SAY", small, HudTheme.TextDim, true);
            HudTheme.Label(new Rect(x3, r.y + 28f, w3, 40f), CommandHints(compact),
                HudTheme.FontBody, HudTheme.Text, false, TextAnchor.UpperLeft, true);
            if (!compact && (voiceInput == null || voiceInput.enableKeyboardFallback))
                HudTheme.Label(new Rect(x3, r.y + 76f, w3, 20f), "SIMULATED keys: 1 select  2 move  3 release  4 cancel  5 reset  6 focus  7 help  |  F8 type", small, HudTheme.Warn);
        }
    }
}
