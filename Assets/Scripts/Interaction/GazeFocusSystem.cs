using System;
using System.IO;
using UnityEngine;

namespace Group05.VoiceVR
{
    /// <summary>
    /// Minimal, additive gaze loop. Casts a ray from the viewing camera's forward direction, accepts ONLY objects that
    /// carry a <see cref="SpatialTarget"/> as valid targets (the floor and other colliders are never targets), shows a
    /// simple focus cue, and feeds parsed utterances into <see cref="GazeInteractionModel"/>.
    ///
    /// Focus never executes anything. A target is selected only by an explicit, actionable SELECT utterance after the
    /// gaze has rested on it (see the model). MOVE/RELEASE, snapping, and XR controllers are NOT part of this phase.
    ///
    /// The focus cue is a plain tint + text label + reticle. It is NOT a validated accessibility feature.
    /// Utterances may come from real speech or from SIMULATED keyboard/text input; the modality is always recorded.
    /// No audio is recorded here and no participant identifier is handled.
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

        [Header("Feedback")]
        [Tooltip("Optional small object placed at the gaze hit point. Its colliders must be removed.")]
        public Transform reticle;
        public Color reticleIdleColor = Color.white;
        public Color reticleFocusColor = new Color(1f, 0.92f, 0.1f);
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

        public GazeInteractionModel Model { get; } = new GazeInteractionModel();
        public SpatialTarget FocusedTarget { get; private set; }
        public SpatialTarget SelectedTarget { get; private set; }
        public bool HasCamera { get; private set; }
        public CommandResult LastResult { get; private set; }
        public bool HasLastResult { get; private set; }
        public string LastCommandLabel { get; private set; } = "(none yet)";
        public string EventLogPath { get { return eventWriter != null ? eventWriter.FilePath : null; } }

        static readonly RaycastHit[] HitBuffer = new RaycastHit[16];

        bool initialized;
        bool warnedNoCamera;
        bool reticleVisible;
        int eventIndex;
        string sessionId;
        GazeEventCsvWriter eventWriter;
        bool eventLogFailed;
        string panelText = "";
        Renderer reticleRenderer;
        MaterialPropertyBlock reticleBlock;
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

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
            RebuildPanel();
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

        void Update() { Evaluate(Time.realtimeSinceStartup); }

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

            Camera cam = gazeCamera != null ? gazeCamera : Camera.main;
            HasCamera = cam != null;
            if (cam == null)
            {
                if (!warnedNoCamera)
                {
                    warnedNoCamera = true;
                    UnityEngine.Debug.LogWarning("[GazeFocusSystem] No gaze camera assigned and no MainCamera found; gaze focus is disabled.");
                    LogEvent(now, GazeEventRecord.CameraMissing, null, null, null, null, null);
                    RebuildPanel();
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
            RebuildPanel();
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
                RebuildPanel();
            }
            if (Model.HasSelection && SelectedTarget == null)
            {
                string name = Model.SelectedName;
                bool f, s;
                Model.TargetLost(Model.SelectedId, out f, out s);
                SelectedTarget = null;
                LogEvent(now, GazeEventRecord.TargetLost, name, null, null, "selection", "destroyed");
                RebuildPanel();
            }
        }

        void RefreshVisual(SpatialTarget t)
        {
            if (t == null) return;
            t.EnsureInitialized();
            t.SetVisualState(t == SelectedTarget ? SpatialTarget.VisualState.Selected
                           : t == FocusedTarget ? SpatialTarget.VisualState.Focused
                           : SpatialTarget.VisualState.Normal);
        }

        void OnUtterance(UtteranceEvent ev)
        {
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
                RefreshVisual(previous);
                RefreshVisual(SelectedTarget);
            }
            else if (r.Action == CommandAction.ClearedSelection)
            {
                SpatialTarget previous = SelectedTarget;
                SelectedTarget = null;
                RefreshVisual(previous);
            }

            LastResult = r;
            HasLastResult = true;
            string tag = modality == InputModality.Voice ? "VOICE" : "SIMULATED " + modality + " (not speech)";
            LastCommandLabel = "[" + tag + "] '" + (rawText ?? "") + "' -> " + r.Outcome + (r.Reason.Length > 0 ? " (" + r.Reason + ")" : "") + ": " + r.Message;
            UnityEngine.Debug.Log("[GazeFocusSystem] " + LastCommandLabel);
            LogEvent(now, GazeEventRecord.Command, r.TargetName ?? Model.FocusedName, modality, parsed.Kind.ToString(), r.Outcome.ToString().ToLowerInvariant(), r.Reason);
            RebuildPanel();
            return r;
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
                Color c = onValidTarget ? reticleFocusColor : reticleIdleColor;
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

        // ---- status panel ----

        void RebuildPanel()
        {
            string cam = HasCamera ? "OK" : (warnedNoCamera ? "MISSING (gaze disabled)" : "pending");
            string focus = Model.HasFocus ? Model.FocusedName : "none";
            string sel = Model.HasSelection ? Model.SelectedName : "none";
            panelText = "GAZE LOOP (Phase 3)  Look: right-mouse drag or arrow keys (simulated head gaze)\n"
                      + "Camera: " + cam + "\n"
                      + "Focus: " + focus + "   |   Selected: " + sel + "\n"
                      + "Last command: " + LastCommandLabel + "\n"
                      + "Hold gaze " + minFocusSeconds.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " s on a target, then key 1 (select that). 4 = cancel. MOVE/RELEASE not available yet.";
        }

        void OnGUI()
        {
            if (!showStatusPanel) return;
            GUI.Box(new Rect(10, 280, 760, 120), GUIContent.none);
            GUI.Label(new Rect(18, 284, 744, 112), panelText);
        }
    }
}
