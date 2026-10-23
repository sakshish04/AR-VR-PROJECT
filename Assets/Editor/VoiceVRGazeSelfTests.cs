#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Group05.VoiceVR;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Group05.VoiceVR.EditorTools
{
    /// <summary>
    /// Unity-side self-tests of the gaze loop, run in EDIT mode (no Play Mode, no visuals, no real input).
    /// They build a throwaway additive scene, drive the camera and a simulated clock, and assert focus,
    /// focus loss, switching, selection gating and rejection. Results are labelled SELFTEST.
    /// They do NOT test rendering, the on-screen panel, real speech, or a headset.
    ///
    /// Menu: Group05 > VoiceVR > Run Gaze Self-Tests
    /// Batch: Unity -batchmode -projectPath ... -executeMethod Group05.VoiceVR.EditorTools.VoiceVRGazeSelfTests.RunBatch -quit  (Editor must be closed)
    /// The results text file is written to telemetry/selftest/ (git-ignored) with a unique name; nothing is overwritten.
    /// </summary>
    public static class VoiceVRGazeSelfTests
    {
        /// <summary>Number of checks a complete run must record (passed + failed + blocked). Update when checks are added or removed.</summary>
        const int ExpectedChecks = 40;

        /// <summary>Exit codes: 0 = every expected check ran and passed; 1 = at least one check failed; 3 = nothing failed but the run was blocked or incomplete.</summary>
        public const int ExitAllPassed = 0, ExitFailed = 1, ExitBlockedOrIncomplete = 3;

        /// <summary>
        /// The whole test fixture is built at this offset. In the interactive Editor the test scene is loaded ADDITIVELY next to the
        /// user's scene, and Physics raycasts see colliders from every loaded scene. Without the offset, the production scene's
        /// identical targets (same positions/sizes as the fixture) tie with the fixture's and can win the raycast, which breaks
        /// every focus/selection check. 2000 m is far outside the production scene and beyond the gaze ray's max distance.
        /// </summary>
        static readonly Vector3 FixtureOffset = new Vector3(2000f, 0f, 2000f);

        static Vector3 V(float x, float y, float z) { return new Vector3(x, y, z) + FixtureOffset; }

        static readonly List<string> Lines = new List<string>();
        static int passed, failed, blocked;
        static string lastSummary = "";

        [MenuItem("Group05/VoiceVR/Run Gaze Self-Tests")]
        public static void RunFromMenu()
        {
            int code = Run();
            string title = code == ExitAllPassed ? "Gaze self-tests: ALL PASSED"
                         : code == ExitFailed ? "Gaze self-tests: FAILED"
                         : "Gaze self-tests: INCOMPLETE (not a pass)";
            EditorUtility.DisplayDialog(title, lastSummary + "\n\nSee the Console and telemetry/selftest/.", "OK");
        }

        /// <summary>Batch entry point. Exit code is 0 ONLY when every expected check ran and passed.</summary>
        public static void RunBatch()
        {
            int code;
            try { code = Run(); }
            catch (Exception ex)
            {
                // Run() handles its own failures; this is a last resort so an exception can never look like a pass.
                Debug.LogError("[GazeSelfTest] Run() threw: " + ex);
                code = ExitBlockedOrIncomplete;
            }
            EditorApplication.Exit(code);
        }

        static void Check(bool ok, string name)
        {
            if (ok) passed++; else failed++;
            string line = (ok ? "PASS  " : "FAIL  ") + name;
            Lines.Add(line);
            if (ok) Debug.Log("[GazeSelfTest] " + line); else Debug.LogError("[GazeSelfTest] " + line);
        }

        /// <summary>Records a check that could not be run. Never counted as a pass; makes the whole run incomplete.</summary>
        static void Block(string name, string reason)
        {
            blocked++;
            string line = "BLOCKED  " + name + " (" + reason + ")";
            Lines.Add(line);
            Debug.LogWarning("[GazeSelfTest] " + line);
        }

        static ParsedIntent P(string text) { return VoiceIntentParser.Parse(text); }

        static GameObject Prim(PrimitiveType t, string name, Vector3 pos, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(t);
            go.name = name;
            go.transform.position = pos;
            go.transform.localScale = scale;
            return go;
        }

        static SpatialTarget Target(PrimitiveType t, string name, string spoken, Vector3 pos)
        {
            var go = Prim(t, name, pos, Vector3.one * 0.5f);
            var st = go.AddComponent<SpatialTarget>();
            st.displayName = spoken;
            st.showLabel = false; // keep the edit-mode test free of font/TextMesh dependencies
            st.showRing = false;  // ... and of shader/material creation (the ring is a runtime-only visual)
            st.EnsureInitialized();
            return st;
        }

        static bool Near(Vector3 x, Vector3 y, float tol = 0.02f) { return (x - y).magnitude <= tol; }

        static void Aim(Camera cam, Vector3 point)
        {
            cam.transform.rotation = Quaternion.LookRotation((point - cam.transform.position).normalized);
            Physics.SyncTransforms();
        }

        public static int Run()
        {
            Lines.Clear(); passed = 0; failed = 0; blocked = 0; lastSummary = "";
            Scene previous = default(Scene);
            Scene testScene = default(Scene);
            bool setupComplete = false;      // true once the temporary test scene exists and is active
            bool additiveTestScene = false;  // true when the test scene must be closed again (interactive Editor only)
            try
            {
                // --- test-scene setup (inside the try: a setup failure is recorded as BLOCKED/INCOMPLETE, never an unhandled exception) ---
                previous = SceneManager.GetActiveScene();
                if (Application.isBatchMode)
                {
                    // Batch mode has no interactive scene to preserve, and Unity refuses an additive scene on top of an
                    // untitled unsaved one, so replace it with a fresh empty scene. Nothing is saved to disk.
                    testScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                }
                else
                {
                    // Interactive Editor: never replace the user's scene. Unity cannot add a scene next to an untitled one.
                    if (string.IsNullOrEmpty(previous.path))
                        throw new InvalidOperationException("the open scene is untitled/unsaved; save it (or open a saved scene) and run the self-tests again. Nothing was changed.");
                    testScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                    additiveTestScene = true;
                }
                SceneManager.SetActiveScene(testScene);
                setupComplete = true;

                var camGo = new GameObject("TestCamera");
                camGo.transform.position = V(0f, 1.6f, 0f);
                var cam = camGo.AddComponent<Camera>();   // passed to the system via gazeCamera below; Camera.main is never used for these checks

                GameObject floor = Prim(PrimitiveType.Plane, "Floor", V(0f, 0f, 0f), new Vector3(1.5f, 1f, 1.5f));
                GameObject distractor = Prim(PrimitiveType.Cube, "Distractor", V(2.6f, 0.6f, 3f), new Vector3(0.8f, 1.2f, 0.8f));
                SpatialTarget a = Target(PrimitiveType.Cube, "A", "red cube", V(-1.4f, 1.3f, 3f));
                SpatialTarget b = Target(PrimitiveType.Sphere, "B", "blue sphere", V(0f, 1.3f, 3f));
                SpatialTarget c = Target(PrimitiveType.Capsule, "C", "green capsule", V(1.2f, 1.3f, 3f));

                var sysGo = new GameObject("Systems");
                var sys = sysGo.AddComponent<GazeFocusSystem>();
                sys.gazeCamera = cam;
                sys.writeEventLog = false;
                sys.minFocusSeconds = 0.3f;
                sys.arenaCenter = FixtureOffset;   // the play area follows the offset fixture (floor at the fixture's y = 0)
                sys.EnsureInitialized();
                Vector3 aStart = a.transform.position;
                Vector3 bStart = b.transform.position, cStart = c.transform.position;
                Vector3 floorStart = floor.transform.position, distractorStart = distractor.transform.position;

                float t = 0f;
                // --- focus on each target ---
                Aim(cam, a.transform.position); sys.Evaluate(t += 0.02f);
                Check(sys.FocusedTarget == a && sys.Model.Focus == FocusState.Focused, "gaze at A focuses A");
                Aim(cam, b.transform.position); sys.Evaluate(t += 0.02f);
                Check(sys.FocusedTarget == b, "gaze at B focuses B");
                Aim(cam, c.transform.position); sys.Evaluate(t += 0.02f);
                Check(sys.FocusedTarget == c, "gaze at C focuses C");
                Check(c.CurrentVisualState == SpatialTarget.VisualState.Focused &&
                      a.CurrentVisualState == SpatialTarget.VisualState.Normal &&
                      b.CurrentVisualState == SpatialTarget.VisualState.Normal, "only one target shows Focused after switching (previous restored)");

                // --- non-targets ---
                Aim(cam, V(0f, 0f, 3f)); sys.Evaluate(t += 0.02f);
                Check(sys.FocusedTarget == null && !sys.Model.HasFocus, "gaze at the floor clears focus (floor is not a target)");
                Check(c.CurrentVisualState == SpatialTarget.VisualState.Normal, "leaving a target restores its appearance");
                Aim(cam, V(2.6f, 0.9f, 3f)); sys.Evaluate(t += 0.02f);
                Check(sys.FocusedTarget == null, "gaze at a collider without SpatialTarget is not a target");
                Aim(cam, V(0f, 8f, 3f)); sys.Evaluate(t += 0.02f);
                Check(sys.FocusedTarget == null, "gaze at empty space clears focus");

                // --- confirmation gating ---
                Aim(cam, a.transform.position); sys.Evaluate(t += 0.02f);
                CommandResult early = sys.SubmitParsed(P("select that"), InputModality.KeyboardFallback, "select that", t + 0.05f);
                Check(early.Outcome == CommandOutcome.Rejected && early.Reason == "focus_too_recent" && sys.SelectedTarget == null, "SELECT right after focus is rejected (no accidental selection)");
                sys.Evaluate(t += 0.5f);
                CommandResult mismatch = sys.SubmitParsed(P("select the blue sphere"), InputModality.TextFallback, "select the blue sphere", t);
                Check(mismatch.Outcome == CommandOutcome.Rejected && mismatch.Reason == "target_mismatch" && sys.SelectedTarget == null, "naming a different target is rejected");
                CommandResult neg = sys.SubmitParsed(P("do not select that"), InputModality.TextFallback, "do not select that", t);
                Check(neg.Outcome == CommandOutcome.Rejected && sys.SelectedTarget == null, "negated command is rejected");
                CommandResult amb = sys.SubmitParsed(P("select and release"), InputModality.TextFallback, "select and release", t);
                Check(amb.Outcome == CommandOutcome.Rejected && sys.SelectedTarget == null, "ambiguous command is rejected");
                Check(sys.Model.HasFocus && sys.FocusedTarget == a, "rejections do not change focus");

                CommandResult ok = sys.SubmitParsed(P("select that"), InputModality.KeyboardFallback, "select that", t);
                Check(ok.Outcome == CommandOutcome.Executed && ok.Action == CommandAction.SelectedTarget && sys.SelectedTarget == a, "explicit SELECT after held gaze selects the focused target");
                Check(a.CurrentVisualState == SpatialTarget.VisualState.Selected, "selected target shows Selected");

                // --- selection survives gaze moving away; separate states ---
                Aim(cam, b.transform.position); sys.Evaluate(t += 0.02f);
                Check(sys.FocusedTarget == b && sys.SelectedTarget == a, "focus moves independently of selection");
                Check(a.CurrentVisualState == SpatialTarget.VisualState.Selected && b.CurrentVisualState == SpatialTarget.VisualState.Focused, "selected + focused visuals coexist");
                sys.Evaluate(t += 0.5f);
                CommandResult dup = sys.SubmitParsed(P("select that"), InputModality.KeyboardFallback, "select that", t);
                Check(dup.Outcome == CommandOutcome.Rejected && dup.Reason == "already_selected" && sys.SelectedTarget == a, "second SELECT while one is selected is rejected");

                // --- MOVE (relative): only the selected target moves, one step, relative to the camera (it faces +Z, so right is +X) ---
                CommandResult mv = sys.SubmitParsed(P("move right"), InputModality.KeyboardFallback, "move right", t);
                Check(mv.Outcome == CommandOutcome.Executed && mv.Action == CommandAction.MoveRequested && Near(a.transform.position, aStart + new Vector3(0.5f, 0f, 0f)),
                      "MOVE right steps the selected target 0.5 m to the camera's right");
                Check(sys.SelectedTarget == a && Near(b.transform.position, bStart) && Near(c.transform.position, cStart) &&
                      Near(floor.transform.position, floorStart) && Near(distractor.transform.position, distractorStart),
                      "MOVE moves only the selected target; others, the floor and the distractor stay put");

                // --- cancel (also undoes the moves made since SELECT) ---
                CommandResult cn = sys.SubmitParsed(P("cancel"), InputModality.KeyboardFallback, "cancel", t);
                Check(cn.Outcome == CommandOutcome.Cancelled && sys.SelectedTarget == null && a.CurrentVisualState == SpatialTarget.VisualState.Normal, "CANCEL clears the selection and restores appearance");
                Check(Near(a.transform.position, aStart) && !a.HasMovedFromInitial && sys.Model.MovedCount == 0, "CANCEL returns the target to where it was when selected");
                CommandResult cn2 = sys.SubmitParsed(P("cancel"), InputModality.KeyboardFallback, "cancel", t);
                Check(cn2.Outcome == CommandOutcome.Rejected && cn2.Reason == "nothing_selected", "CANCEL with nothing selected is rejected harmlessly");
                CommandResult unk = sys.SubmitParsed(P("make it rain"), InputModality.TextFallback, "make it rain", t);
                Check(unk.Outcome == CommandOutcome.Rejected, "unknown command is rejected, no exception");

                // --- manipulation: limits, gaze placement, rejections, RELEASE, RESET ---
                Aim(cam, a.transform.position); sys.Evaluate(t += 0.02f); sys.Evaluate(t += 0.5f);
                CommandResult reselect = sys.SubmitParsed(P("select that"), InputModality.KeyboardFallback, "select that", t);
                Check(reselect.Outcome == CommandOutcome.Executed && sys.SelectedTarget == a, "SELECT A again for the manipulation checks");

                CommandResult d1 = sys.SubmitParsed(P("move down"), InputModality.KeyboardFallback, "move down", t);
                CommandResult d2 = sys.SubmitParsed(P("move down"), InputModality.KeyboardFallback, "move down", t);
                CommandResult d3 = sys.SubmitParsed(P("move down"), InputModality.KeyboardFallback, "move down", t);
                Check(d1.Outcome == CommandOutcome.Executed && d2.Outcome == CommandOutcome.Executed && d3.Outcome == CommandOutcome.Executed &&
                      a.GetBounds().min.y >= FixtureOffset.y - 0.001f && Mathf.Abs(a.transform.position.y - 0.25f) < 0.01f,
                      "MOVE down is clamped at the floor: the target's bottom never goes below it");
                CommandResult d4 = sys.SubmitParsed(P("move down"), InputModality.KeyboardFallback, "move down", t);
                Check(d4.Outcome == CommandOutcome.Rejected && d4.Reason == "at_limit" && Mathf.Abs(a.transform.position.y - 0.25f) < 0.01f,
                      "MOVE further down is rejected (at_limit) and changes nothing");

                Aim(cam, V(1f, 0f, 2f)); sys.Evaluate(t += 0.02f);
                CommandResult mg = sys.SubmitParsed(P("move there"), InputModality.KeyboardFallback, "move there", t);
                Check(mg.Outcome == CommandOutcome.Executed && Near(a.transform.position, V(1f, 0.26f, 2f)),
                      "MOVE there places the target on the surface under the gaze");

                Vector3 beforeReject = a.transform.position;
                Aim(cam, b.transform.position); sys.Evaluate(t += 0.02f);
                CommandResult mo = sys.SubmitParsed(P("move there"), InputModality.KeyboardFallback, "move there", t);
                Check(mo.Outcome == CommandOutcome.Rejected && mo.Reason == "gaze_on_other_target" && Near(a.transform.position, beforeReject),
                      "MOVE there while looking at another target is rejected and moves nothing");
                Aim(cam, V(0f, 8f, 3f)); sys.Evaluate(t += 0.02f);
                CommandResult mn = sys.SubmitParsed(P("move there"), InputModality.KeyboardFallback, "move there", t);
                Check(mn.Outcome == CommandOutcome.Rejected && mn.Reason == "no_gaze_point" && Near(a.transform.position, beforeReject),
                      "MOVE there with nothing under the gaze is rejected and moves nothing");
                CommandResult mu = sys.SubmitParsed(P("move it to the shelf"), InputModality.TextFallback, "move it to the shelf", t);
                Check(mu.Outcome == CommandOutcome.Rejected && mu.Reason == "unknown_destination" && Near(a.transform.position, beforeReject),
                      "MOVE to an unknown named destination is rejected and moves nothing");

                CommandResult rel = sys.SubmitParsed(P("release"), InputModality.KeyboardFallback, "release", t);
                Check(rel.Outcome == CommandOutcome.Executed && rel.Action == CommandAction.Released && sys.SelectedTarget == null &&
                      Near(a.transform.position, beforeReject) && a.CurrentVisualState != SpatialTarget.VisualState.Selected,
                      "RELEASE keeps the target where it is and deselects it");
                Check(sys.Model.MovedCount == 1 && a.HasMovedFromInitial, "a released, moved target is tracked as moved");

                Aim(cam, b.transform.position); sys.Evaluate(t += 0.02f);
                CommandResult mvNone = sys.SubmitParsed(P("move right"), InputModality.KeyboardFallback, "move right", t);
                CommandResult rlNone = sys.SubmitParsed(P("release"), InputModality.KeyboardFallback, "release", t);
                Check(mvNone.Outcome == CommandOutcome.Rejected && mvNone.Reason == "nothing_selected" && rlNone.Outcome == CommandOutcome.Rejected && Near(a.transform.position, beforeReject),
                      "MOVE and RELEASE with nothing selected are rejected and move nothing");
                Check(b.IsFlashing, "a rejected command flashes the focused target");

                CommandResult rst = sys.SubmitParsed(P("reset"), InputModality.KeyboardFallback, "reset", t);
                Check(rst.Outcome == CommandOutcome.Executed && rst.Action == CommandAction.RestoredInitialState && Near(a.transform.position, aStart) &&
                      sys.SelectedTarget == null && sys.Model.MovedCount == 0 && !a.HasMovedFromInitial,
                      "RESET returns the moved target to its start position");
                Check(Near(b.transform.position, bStart) && Near(c.transform.position, cStart) &&
                      Near(floor.transform.position, floorStart) && Near(distractor.transform.position, distractorStart),
                      "after all manipulation the other targets, the floor and the distractor never moved");

                // --- destroyed target ---
                Aim(cam, c.transform.position); sys.Evaluate(t += 0.02f);
                sys.Evaluate(t += 0.5f);
                sys.SubmitParsed(P("select that"), InputModality.KeyboardFallback, "select that", t);
                Check(sys.SelectedTarget == c, "selected C before destroying it");
                UnityEngine.Object.DestroyImmediate(c.gameObject);
                Physics.SyncTransforms();
                bool threw = false;
                try { sys.Evaluate(t += 0.02f); sys.SubmitParsed(P("what is this"), InputModality.KeyboardFallback, "what is this", t); }
                catch (Exception) { threw = true; }
                Check(!threw && sys.SelectedTarget == null && !sys.Model.HasSelection, "destroyed selected/focused target is handled without exceptions");

                // --- missing camera ---
                if (Camera.main != null)
                {
                    // Another scene already provides a MainCamera, so "no camera" cannot be simulated honestly here: Camera.main is a
                    // global lookup and the production camera must not be disabled, retagged or removed to make this check run.
                    // This is recorded as BLOCKED (never as a pass) and makes the whole run incomplete. All other checks use the
                    // fixture's own camera explicitly. For a complete run use batch mode or a saved scene without a MainCamera.
                    Block("missing camera: no exception, no focus",
                          "a MainCamera ('" + Camera.main.name + "' in scene '" + Camera.main.gameObject.scene.name + "') already exists in another open scene");
                }
                else
                {
                    var sys2Go = new GameObject("Systems2");
                    var sys2 = sys2Go.AddComponent<GazeFocusSystem>();
                    sys2.gazeCamera = null;
                    sys2.writeEventLog = false;
                    bool threw2 = false;
                    try { sys2.Evaluate(1f); sys2.SubmitParsed(P("select that"), InputModality.KeyboardFallback, "select that", 1f); }
                    catch (Exception) { threw2 = true; }
                    Check(!threw2 && !sys2.HasCamera && !sys2.Model.HasFocus, "missing camera: no exception, no focus");
                }
            }
            catch (Exception ex)
            {
                if (!setupComplete)
                {
                    // No check could run. Recorded as BLOCKED (never a pass); the run is INCOMPLETE and exits with code 3.
                    Block("test-scene setup (all " + ExpectedChecks + " checks could not run)", ex.GetType().Name + ": " + ex.Message);
                }
                else
                {
                    failed++;
                    Lines.Add("FAIL  unexpected exception: " + ex);
                    Debug.LogError("[GazeSelfTest] unexpected exception: " + ex);
                }
            }
            finally
            {
                // Cleanup must never hide the results, so it is guarded. In batch mode the temporary scene is the only open
                // scene and is simply discarded when the process exits (it is never saved).
                try
                {
                    if (additiveTestScene)
                    {
                        if (previous.IsValid()) SceneManager.SetActiveScene(previous);
                        if (testScene.IsValid()) EditorSceneManager.CloseScene(testScene, true);
                    }
                }
                catch (Exception ex) { Debug.LogWarning("[GazeSelfTest] Cleanup problem: " + ex.Message); }
            }

            int total = passed + failed + blocked;
            bool complete = total == ExpectedChecks;
            int exitCode = failed > 0 ? ExitFailed : (blocked > 0 || !complete) ? ExitBlockedOrIncomplete : ExitAllPassed;
            string verdict = exitCode == ExitAllPassed ? "ALL PASSED"
                           : exitCode == ExitFailed ? "FAILED"
                           : "INCOMPLETE - NOT A PASS";
            lastSummary = "Passed: " + passed + "   Failed: " + failed + "   Blocked: " + blocked
                        + "   (recorded " + total + " of " + ExpectedChecks + " expected checks)\nVerdict: " + verdict;

            Lines.Add("");
            Lines.Add("SELFTEST (edit mode, developer-run; not participant data).");
            Lines.Add("Passed: " + passed + "  Failed: " + failed + "  Blocked: " + blocked + "  Recorded: " + total + " of " + ExpectedChecks + " expected");
            if (!complete) Lines.Add("WARNING: the run recorded " + total + " checks but " + ExpectedChecks + " were expected; it did not finish normally.");
            Lines.Add("Verdict: " + verdict + "  (exit code " + exitCode + ")");
            Debug.Log("[GazeSelfTest] " + lastSummary.Replace("\n", " | ") + " | exit code " + exitCode);
            try
            {
                string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "telemetry", "selftest"));
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, "unity_gaze_selftest_" + SessionCsvFile.NewSessionId(DateTime.UtcNow) + ".txt");
                for (int n = 2; File.Exists(path); n++) path = Path.Combine(folder, "unity_gaze_selftest_" + SessionCsvFile.NewSessionId(DateTime.UtcNow) + "_" + n + ".txt");
                File.WriteAllText(path, string.Join("\n", Lines.ToArray()) + "\n", new UTF8Encoding(false));
                Debug.Log("[GazeSelfTest] Results written to " + path);
            }
            catch (Exception ex) { Debug.LogWarning("[GazeSelfTest] Could not write results file: " + ex.Message); }
            return exitCode;
        }
    }
}
#endif
